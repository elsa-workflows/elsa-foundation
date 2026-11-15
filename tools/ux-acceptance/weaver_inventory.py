"""Opt-in shipped-host observations; unavailable provider answers remain explicitly unverified."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import shutil

BASELINE = 'Weaver shipped inventory and structured logs deselection'
ENABLED = 'Weaver shipped inventory with ExtensionBuilder enabled'
JOURNEYS = {BASELINE, ENABLED}
CAPTURES = {BASELINE: ['reference', 'inventory-baseline', 'inventory-deselected', 'inventory-unknown'],
            ENABLED: ['reference', 'inventory-enabled']}
SEMANTIC_BASELINE = 'Weaver external protocol support and current route'
SEMANTIC_ENABLED = 'Weaver external protocol support after root restart'
SEMANTIC_JOURNEYS = {SEMANTIC_BASELINE, SEMANTIC_ENABLED}
SEMANTIC_STATES = {SEMANTIC_BASELINE: ['baseline', 'deselected-list', 'deselected', 'unknown'], SEMANTIC_ENABLED: ['enabled']}
SEMANTIC_CAPTURES = {SEMANTIC_BASELINE: ['reference', 'semantic-baseline', 'semantic-deselected-list', 'semantic-deselected', 'semantic-unknown'],
                     SEMANTIC_ENABLED: ['reference', 'semantic-enabled']}
SEMANTIC_DIALOGUE_CAPTURES = {title: [f'semantic-{part}-{state}' for state in states
                                      for part in ('question', 'answer-start', 'answer-end')]
                              for title, states in SEMANTIC_STATES.items()}
for _title, _captures in SEMANTIC_DIALOGUE_CAPTURES.items():
    SEMANTIC_CAPTURES[_title] += _captures


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def readiness_fact(status, payload):
    """A bounded native startup cause; no response message, path or arbitrary code enters shared reports."""
    try:
        body = json.loads(payload)
    except (ValueError, UnicodeError):
        body = {}
    state = body.get('status') if isinstance(body, dict) else None
    allowed = {'ready', 'pending', 'initializing', 'warming', 'failed', 'cancelled', 'disabled', 'notstarted', 'live'}
    result = {'httpStatus': status, 'status': state if state in allowed else 'unclassified'}
    if isinstance(body, dict):
        if body.get('code') == 'shell_activation_failed': result['code'] = 'shell_activation_failed'
        if isinstance(body.get('generation'), int): result['generation'] = body['generation']
    return result


def failure_fact(error, stage, log=None):
    """Concrete safe process/build classification retained before the owned logs are cleaned up."""
    kind = type(error).__name__
    result = {'stage': stage, 'category': {'CalledProcessError': 'process-failed', 'TimeoutExpired': 'process-timeout', 'TimeoutError': 'readiness-timeout',
              'ValueError': 'evidence-invalid', 'InterruptedError': 'interrupted'}.get(kind, 'runtime-failed')}
    if isinstance(getattr(error, 'returncode', None), int): result['exitCode'] = error.returncode
    if log is not None and Path(log).is_file():
        result['buildCodes'] = sorted(set(re.findall(r'\b(?:NETSDK|NU|CS|MSB)\d{3,5}\b', Path(log).read_text(errors='replace'))))[:8]
    return result


def inventory_verdict(value, *, source, phase):
    """A successful process or existing screenshot cannot replace native state and disclosure evidence."""
    return _native_verdict(value, source=source, phase=phase, semantic=False)


def _native_verdict(value, *, source, phase, semantic):
    require(value.get('source') == source and value.get('phase') == phase, 'Inventory source/phase mismatch')
    require(value.get('completed') is True and value.get('failedRequests') == [], 'Inventory journey failed or has failed native requests')
    observations = value.get('observations', [])
    expected = ['enabled'] if phase == 'enabled' else ['baseline', *(['deselected-list'] if semantic else []), 'deselected', 'unknown']
    rows = [item for item in observations if item.get('kind') == 'inventory']
    require([item.get('state') for item in rows] == expected, 'Incomplete or duplicate inventory state observations')
    for item in rows:
        require(item.get('registry', {}).get('complete') is True and item.get('support', {}).get('support', {}).get('completeness') == 'complete',
                'Unknown/incomplete native discovery cannot qualify inventory')
        state = item['state']
        def feature(side, identity, selected):
            matches = [row for row in item.get(side + 'Features', []) if row.get('id') == identity]
            require(len(matches) == 1 and matches[0].get('enabled') is selected and matches[0].get('runtimeAvailable') is True,
                    'Missing independent discovered/selected feature: ' + identity)
        logs = state == 'baseline'
        feature('studio', 'StructuredLogsStudio', logs)
        feature('backend', 'DiagnosticsStructuredLogs', logs)
        feature('backend', 'DiagnosticsStructuredLogsEntityFrameworkCore', logs)
        feature('studio', 'ExtensionBuilderStudio', phase == 'enabled')
        modules = item['registry'].get('modules', [])
        for identity, status in [('structured-logs', 'available' if logs else 'disabled'),
                                 ('extension-builder', 'available' if phase == 'enabled' else 'disabled')]:
            matching = [row for row in modules if row.get('id') == identity]
            require(len(matching) == 1 and matching[0].get('status') == status, 'Deselection must retain an independently discovered module row: ' + identity)
        if semantic:
            require(item.get('provider') == {'enabled': True, 'providerStatus': 'available'}, 'External protocol provider was not available')
        else:
            require(item.get('question', {}).get('status') == 'unanswered-provider-unavailable'
                and item.get('provider', {}).get('enabled') is False
                and item.get('provider', {}).get('providerStatus') == 'unavailable',
                'Normal shipped observation cannot claim an answer or admitted/provider evidence')
            require(item.get('disclosure') == {'status': 'not-observed', 'reason': 'provider-unavailable'},
                'Normal shipped profile must preserve the unavailable disclosure boundary')
        readiness = item.get('readiness', {})
        require(readiness.get('status') == 'ready' and isinstance(readiness.get('generation'), int), 'Missing actual shell generation')
    selections = [item for item in observations if item.get('kind') == 'selection']
    expected_selections = [('studio', {'ExtensionBuilderStudio': True})] if phase == 'enabled' else [
        ('backend', {'DiagnosticsStructuredLogsEntityFrameworkCore': False, 'DiagnosticsStructuredLogs': False}),
        ('studio', {'StructuredLogsStudio': False})]
    require([(item.get('side'), item.get('states')) for item in selections] == expected_selections, 'Independent native selection observations are missing')
    for selection in selections:
        require(set(selection.get('retainedEnabledIds', [])) <= set(selection.get('submittedIds', []))
                and set(selection['states']) <= set(selection.get('submittedIds', [])), 'Native Apply omitted the full desired feature state')
        require(selection.get('retainedConfigurationMatched') is True, 'Native Apply did not preserve retained configurations')
    root = [item for item in observations if item.get('kind') == 'root-extension']
    require(len(root) == 1 and root[0].get('enabled') is (phase == 'enabled')
            and root[0].get('status') in ({401, 403} if phase == 'enabled' else {404}), 'Missing actual root ExtensionBuilder route observation')
    return len(rows)


def text_hash(value):
    require(isinstance(value, str) and bool(value), 'Missing observed text or identity')
    return hashlib.sha256(value.encode()).hexdigest()


def _canonical(value):
    """Compare bounded admitted projections, allowing JSON omission/null and row order differences."""
    if isinstance(value, dict): return {key: _canonical(item) for key, item in sorted(value.items()) if item is not None}
    if isinstance(value, list): return sorted((_canonical(item) for item in value), key=lambda item: json.dumps(item, sort_keys=True))
    return value


def _pick(row, keys):
    return {key: row[key] for key in keys if key in row}


def _studio_facts(studio):
    return {'completeness': studio.get('completeness'), 'registrationCompleteness': studio.get('registrationCompleteness'),
            'modules': [_pick(row, ['id', 'status']) for row in studio.get('modules', [])],
            'loader': [_pick(row, ['moduleId', 'status']) for row in studio.get('loader', [])],
            'registrations': [_pick(row, ['kind', 'id', 'originModuleId', 'declaredModuleId', 'resourceModuleIds', 'status']) for row in studio.get('registrations', [])]}


def _backend_descriptors(support):
    """Installed descriptors precede policy hints; compare shape/identity without equating authority."""
    result = {}
    for kind, keys in [('tools', ['name', 'moduleId', 'contractVersion', 'executionLocation']),
                       ('contexts', ['id', 'moduleId', 'scopeKind', 'contextKinds', 'schemaVersions', 'resourceTypes'])]:
        rows = support.get(kind)
        require(isinstance(rows, list) and all(isinstance(row, dict) and all(key in row for key in keys) for row in rows),
                'Missing native/admitted backend descriptor shape')
        result[kind] = [_pick(row, keys) for row in rows]
    return _canonical(result)


def semantic_verdict(value, peer_records, *, source, phase):
    """Join private actual boundaries. No phase/native expected answer is ever supplied to the service."""
    _native_verdict(value, source=source, phase=phase, semantic=True)
    require(value.get('studioSource') == source, 'Semantic Studio source differs from owned source')
    rows = [item for item in value['observations'] if item.get('kind') == 'inventory']
    turns, posts = value.get('turns', []), value.get('messagePosts', [])
    expected = ['enabled'] if phase == 'enabled' else ['baseline', 'deselected-list', 'deselected', 'unknown']
    require([turn.get('state') for turn in turns] == expected and len(posts) == len(turns) == len(peer_records),
            'Missing, duplicate or retried browser/SDK turns')
    require(len({turn.get('nonce') for turn in turns}) == len(turns), 'Duplicate browser nonce')
    require(len({turn.get('request', {}).get('requestId') for turn in turns}) == len(turns), 'Duplicate browser request')
    require(len({turn.get('assistantId') for turn in turns}) == len(turns), 'Stale rendered assistant')
    require(len({turn.get('accepted', {}).get('message', {}).get('id') for turn in turns}) == len(turns), 'Duplicate accepted message')
    # JSON-RPC integer IDs are connection-scoped; the shipped adapter opens a fresh client per turn.
    require(len({item.get('requestHash') for item in peer_records}) == len(peer_records)
            and len({item.get('messageId') for item in peer_records}) == len(peer_records), 'Duplicate SDK request/message')
    joined = []
    for turn, native, post in zip(turns, rows, posts):
        require(native.get('support', {}).get('policyScope') == 'hostDefault', 'Native sessionless support scope is missing')
        state, request = turn['state'], turn.get('request', {})
        nonce, question, session = turn.get('nonce'), turn.get('question'), turn.get('sessionId')
        require(isinstance(nonce, str) and re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9-]{7,63}', nonce), 'Invalid private turn nonce')
        suffix = ' What is known about the current route?' if state == 'unknown' else ''
        require(question == f'What support is installed for modules structured-logs, extension-builder and absent-module?{suffix} nonce={nonce}',
                'Question supplied route/expected facts or departed from bounded query')
        require(request.get('message') == request.get('content') == question and post.get('body') == request
                and post.get('path') == f'/_elsa/weaver/sessions/{session}/messages', 'Actual browser POST does not match turn')
        surface = request.get('activeSurface', {})
        require(surface.get('route') == turn.get('route') == native.get('route'), 'Actual current route mismatch')
        accepted = turn.get('accepted', {})
        message = accepted.get('message', {})
        require(accepted.get('warnings') == [] and message.get('sessionId') == session
                and message.get('requestId') == request.get('requestId') and message.get('content') == question
                and bool(message.get('id')), 'Missing or mismatched actual admission receipt')
        matches = [item for item in peer_records if item.get('nonce') == nonce]
        require(len(matches) == 1, 'Missing/ambiguous browser to SDK nonce')
        peer = matches[0]
        require(peer.get('sessionId') == session and type(peer.get('requestId')) is int and peer.get('wireComplete') is True
                and peer.get('userRequestHash') == text_hash(question), 'Incomplete or foreign SDK delivery')
        for key in ('promptHash', 'requestHash', 'responseHash'):
            require(isinstance(peer.get(key), str) and re.fullmatch(r'[a-f0-9]{64}', peer[key]), 'Missing actual SDK byte identity')
        require(isinstance(peer.get('messageId'), str) and bool(peer['messageId']), 'Missing SDK message identity')
        context = message.get('context', [])
        host_support = [item for item in context if item.get('contentType') == 'weaver.host-support']
        require(len(host_support) == 2, 'Ambiguous/incomplete admitted host support')
        by_side = {item.get('source'): item for item in host_support}
        require(set(by_side) == {'studio', 'backend'}, 'Missing admitted support side')
        metadata = []
        for side, origin in [('studio', 'ClientSnapshot'), ('backend', 'AuthoritativeRead')]:
            item = by_side[side]
            require(item.get('moduleId') == 'Elsa.Weaver' and item.get('schemaVersion') == '1'
                    and item.get('origin') == origin[0].lower() + origin[1:] and item.get('target') is None
                    and item.get('client') is None and item.get('sourceId') is None
                    and not item.get('references') and not item.get('weaverScopes'), 'Support origin/authority mismatch')
            metadata.append({'moduleId': 'Elsa.Weaver', 'contentType': 'weaver.host-support', 'schemaVersion': '1', 'target': None, 'origin': origin})
        require(_canonical(metadata) == _canonical(peer.get('metadata')), 'SDK metadata differs from actual admission')
        studio, backend = by_side['studio'].get('content', {}), by_side['backend'].get('content', {})
        caller = [item for item in request.get('contextAttachments', []) if item.get('contentType') == 'weaver.host-support']
        require(len(caller) == 1 and caller[0].get('moduleId') == 'Elsa.Weaver'
                and _canonical(_studio_facts(caller[0].get('content', {}))) == _canonical(_studio_facts(studio)),
                'Current submitted Studio facts did not survive admission')
        require(studio.get('side') == 'studio' and studio.get('clientAsserted') is True
                and studio.get('completeness') == studio.get('registrationCompleteness') == 'complete'
                and _canonical(_studio_facts(studio)['modules']) == _canonical([_pick(item, ['id', 'status']) for item in native['registry']['modules']]),
                'Stale/incomplete Studio discovery relative to independent native read')
        require(backend.get('side') == 'backend' and backend.get('policyScope') == 'session'
                and backend.get('support', {}).get('completeness') == 'complete', 'Missing authoritative session support hints')
        support = backend['support']
        require(_backend_descriptors(support) == _backend_descriptors(native['support']['support']),
                'Admitted backend descriptors differ from independent native inventory')
        facts = {'studioCompleteness': studio['completeness'], 'registrationCompleteness': studio['registrationCompleteness'],
                 'backendCompleteness': support['completeness'], 'authority': 'unchecked', 'modules': [],
                 'backendTools': support.get('tools', []), 'backendContexts': support.get('contexts', [])}
        for identity in ('structured-logs', 'extension-builder', 'absent-module'):
            found = [item for item in studio['modules'] if item.get('id', '').casefold() == identity]
            require(len(found) <= 1, 'Ambiguous module discovery')
            registrations = [item for item in studio.get('registrations', []) if (item.get('originModuleId') or '').casefold() == identity]
            facts['modules'].append({'id': identity, 'status': found[0]['status'] if found else 'absent',
                'weaverContributions': 'observed' if registrations else 'none', 'registrations': registrations})
        require(facts['modules'][-1]['status'] == 'absent', 'Explicit absent module unexpectedly installed')
        if state == 'unknown':
            require(studio.get('activeRoute') == surface['route'], 'Missing actual admitted current route')
            facts['activeRoute'] = studio['activeRoute']
            require(not surface.get('resourceType') and not surface.get('resourceId') and len(context) == 2
                    and len(request.get('contextAttachments', [])) == 1, 'Unknown route acquired resource attachments or an inferred owner')
        require(_canonical(facts) == _canonical(peer.get('facts')), 'SDK derived facts differ from actual admitted projections')
        rendered, recorded = turn.get('rendered'), turn.get('recorded', {})
        require(peer.get('response') == rendered == recorded.get('content') and peer['responseHash'] == text_hash(rendered)
                and recorded.get('id') == turn.get('assistantId') and recorded.get('role') == 'assistant'
                and recorded.get('status') == 'completed', 'Derived SDK response did not become the completed rendered assistant')
        require(turn.get('before') == turn.get('after') == native.get('readiness')
                and type(turn['before'].get('generation')) is int and turn['before'].get('status') == 'ready', 'Native generation changed during turn')
        joined.append({'state': state, 'nonceHash': text_hash(nonce), 'sessionHash': text_hash(session),
            'clientRequestHash': text_hash(request['requestId']), 'acceptedMessageHash': text_hash(message['id']),
            'assistantMessageHash': text_hash(turn['assistantId']), 'sdkMessageHash': text_hash(peer['messageId']),
            'sdkRequestId': peer['requestId'], 'promptHash': peer['promptHash'], 'sdkRequestHash': peer['requestHash'],
            'userRequestHash': peer['userRequestHash'], 'responseHash': peer['responseHash'], 'wireComplete': True,
            'moduleCount': len(facts['modules']), 'admittedMetadataCount': len(metadata),
            'studioOrigin': 'clientSnapshot', 'backendOrigin': 'authoritativeRead', 'backendPolicyScope': 'session', 'authority': 'unchecked',
            'backendToolCount': len(facts['backendTools']), 'backendContextCount': len(facts['backendContexts']),
            'generation': turn['before']['generation']})
    # Keep unrelated global inventory: compare native package registry and backend sessionless support,
    # never derive a backend owner from the similarly named Studio IDs.
    packages = [[_pick(row, ['packageId', 'version']) for row in item.get('modules', [])]
                for item in value['observations'] if item.get('kind') == 'native-packages']
    require(len(packages) == len(turns) and all(_canonical(item) == _canonical(packages[0]) for item in packages), 'Unrelated package inventory changed')
    return joined


def protocol_projection(value):
    """Allowlist the peer's safe snapshots too; future peer fields cannot silently become public."""
    require(value.get('kind') == 'external-copilot-protocol-observation' and value.get('targetSdkVersion') == '1.0.4'
            and value.get('protocolVersion') == 3 and value.get('closed') is True, 'Missing closed pinned protocol receipt')
    require(value.get('errors') == [], 'Peer observed protocol errors')
    result = _pick(value, ['kind', 'targetSdkVersion', 'protocolVersion', 'connections', 'requests', 'closed'])
    require(all(type(result.get(key)) is int and result[key] >= 0 for key in ('connections', 'requests')), 'Invalid protocol counters')
    turns = value.get('turns', [])
    require(len(turns) == 5, 'A complete appearance requires exactly five physical protocol turns')
    result['turns'] = []
    for row in turns:
        projected = _pick(row, ['sessionHash', 'requestId', 'nonceHash', 'promptHash', 'requestHash', 'userRequestHash', 'responseHash',
                                'wireComplete', 'moduleCount', 'admittedMetadataCount'])
        require(all(isinstance(projected.get(key), str) and re.fullmatch(r'[a-f0-9]{64}', projected[key])
                    for key in ('sessionHash', 'nonceHash', 'promptHash', 'requestHash', 'userRequestHash', 'responseHash'))
                and type(projected.get('requestId')) is int and projected.get('wireComplete') is True
                and projected.get('moduleCount') == 3 and projected.get('admittedMetadataCount') == 2, 'Incomplete safe protocol turn')
        result['turns'].append(projected)
    close = value.get('close', {})
    require(close.get('kind') == 'owned-copilot-peer-close' and close.get('complete') is True
            and close.get('liveThreads') == close.get('openConnections') == 0 and close.get('turns') == 5
            and close.get('connections') == result['connections'] and close.get('errors') == [], 'Owned peer did not close cleanly')
    result['close'] = _pick(close, ['kind', 'complete', 'liveThreads', 'openConnections', 'connections', 'turns'])
    require(len({row['nonceHash'] for row in result['turns']}) == 5, 'Duplicate protocol nonce hashes')
    return result


def _dialogue_verdict(value, payloads):
    """Admit only unchanged open-panel frames bound to already-correlated actual DOM messages."""
    admitted = []
    known = []
    for turn in value['turns']:
        require(isinstance(turn.get('userDomId'), str) and bool(turn['userDomId']), 'Missing actual question DOM identity')
        current = [{'id': turn['userDomId'], 'role': 'user', 'status': 'completed', 'content': turn['question']},
                   {'id': turn['assistantId'], 'role': 'assistant', 'status': 'completed', 'content': turn['rendered']}]
        known.extend(current)
        captures = turn.get('dialogueCaptures', {})
        expected = {f'semantic-{part}-{turn["state"]}' for part in ('question', 'answer-start', 'answer-end')}
        require(set(captures) == expected, 'Missing or extra actual dialogue capture')
        for name in sorted(expected):
            record = captures[name]
            messages = record.get('messages', [])
            require(isinstance(messages, list) and len(messages) >= 2
                    and len({row.get('id') for row in messages}) == len(messages)
                    and all(row in known for row in messages) and messages[-2:] == current,
                    'Unvalidated or stale dialogue DOM message')
            for suffix, key in [('png', 'pngHash'), ('aria.yml', 'ariaHash')]:
                data = payloads.get(name + '.' + suffix)
                require(data is not None and hashlib.sha256(data).hexdigest() == record.get(key), 'Dialogue capture bytes differ from actual browser capture')
            aria = payloads[name + '.aria.yml'].decode('utf-8')
            require('region "Weaver assistant"' in aria and 'textbox "Ask Weaver"' in aria,
                    'Dialogue ARIA does not show the actual open assistant')
            message_aria = record.get('messageListAria', '')
            require(isinstance(message_aria, str) and message_aria.startswith('- list'), 'Missing actual dialogue list ARIA')
            # Compare the actual list subtree at any body indentation; this is not a general YAML parser.
            lines, found = aria.splitlines(), False
            for index, line in enumerate(lines):
                if line.lstrip() != message_aria.splitlines()[0]: continue
                depth = len(line) - len(line.lstrip())
                end = index + 1
                while end < len(lines) and (not lines[end].strip() or len(lines[end]) - len(lines[end].lstrip()) > depth): end += 1
                if '\n'.join(item[depth:] for item in lines[index:end]).strip() == message_aria.strip(): found = True
            require(found, 'Dialogue ARIA differs from actual captured message list')
            for row in messages:
                text = row['content']
                require(text in aria or json.dumps(text, ensure_ascii=False)[1:-1] in aria,
                        'Dialogue ARIA omitted an actual correlated question/answer')
            admitted.append(name)
    return sorted(admitted)


def safe_export(private_report, private_root, destination, *, source, project, title, phase,
                forbidden_values=(), peer_records=None):
    """Export an allowlisted projection, never recursively copy a Playwright directory.

    Every selected byte is checked before the destination is created, including on failure.
    Raw trace/auth state, API bodies, provider prompts, logs and original report paths stay private.
    """
    semantic = title in SEMANTIC_JOURNEYS
    require(semantic == (peer_records is not None), 'Semantic export requires its actual private SDK records')
    receipt_name = 'weaver-semantic.json' if semantic else 'weaver-inventory.json'
    private_root, destination = Path(private_root).resolve(), Path(destination).resolve()
    require(not destination.exists(), 'Safe inventory export destination must be new')
    report = json.loads(Path(private_report).read_text())
    rows = []
    def walk(suites):
        for suite in suites:
            for spec in suite.get('specs', []):
                for test in spec.get('tests', []):
                    rows.append((spec.get('title'), test))
            walk(suite.get('suites', []))
    walk(report.get('suites', []))
    require(len(rows) == 1 and rows[0][0] == title and rows[0][1].get('projectName') == project,
            'Safe export requires one exact appearance/journey execution')
    test = rows[0][1]
    results = test.get('results', [])
    require(len(results) == 1, 'Safe export refuses retry/ambiguous execution')
    result = results[0]
    successful = (not report.get('errors') and test.get('expectedStatus') == 'passed' and test.get('status') == 'expected'
                  and result.get('status') == 'passed')
    attachments = result.get('attachments', [])
    names = [item.get('name') for item in attachments]
    require(len(names) == len(set(names)), 'Safe export refuses duplicate attachment identities')
    allowed_captures = {f'{name}.{suffix}' for name in (SEMANTIC_CAPTURES if semantic else CAPTURES)[title] for suffix in ('png', 'aria.yml')}
    dialogue_captures = {f'{name}.{suffix}' for name in SEMANTIC_DIALOGUE_CAPTURES.get(title, []) for suffix in ('png', 'aria.yml')}
    original_hashes, payloads = {}, {}
    def read(item):
        path = Path(item.get('path', '')).resolve()
        require(path.is_relative_to(private_root) and path.is_file() and not path.is_symlink(), 'Private attachment escaped its owned root')
        value = path.read_bytes()
        require(bool(value), 'Private attachment is empty')
        return value
    receipt = None
    for item in attachments:
        name = item.get('name')
        if name in dialogue_captures and not successful:
            continue # Unvalidated failure dialogue is never selected, even when its private capture exists.
        if name not in allowed_captures | {'trace', receipt_name}:
            continue # No generic source/error/auth attachment is selected for export.
        value = read(item)
        original_hashes[name] = hashlib.sha256(value).hexdigest()
        if name in allowed_captures:
            if name.endswith('.png'):
                require(value.startswith(b'\x89PNG\r\n\x1a\n'), 'Named screenshot is not a PNG')
            else:
                value.decode('utf-8')
            payloads[name] = value
        elif name == receipt_name:
            receipt = json.loads(value)
    admitted_dialogue = []
    if successful:
        require(allowed_captures <= set(payloads) and 'trace' in original_hashes and receipt is not None,
                'Successful inventory lacks required private originals')
        if semantic:
            correlation = semantic_verdict(receipt, peer_records, source=source, phase=phase)
            admitted_dialogue = _dialogue_verdict(receipt, payloads)
        else:
            inventory_verdict(receipt, source=source, phase=phase)
    # Project only the documented metadata fields. No raw request, prompt, config, path, exception or credential survives.
    def pick(row, keys):
        return {key: row[key] for key in keys if key in row}
    def support_projection(support):
        return {'completeness': support.get('completeness'),
                'tools': [pick(item, ['name', 'moduleId', 'contractVersion', 'executionLocation', 'admission', 'authority']) for item in support.get('tools', [])],
                'contexts': [pick(item, ['id', 'moduleId', 'scopeKind', 'contextKinds', 'schemaVersions', 'resourceTypes', 'admission', 'authority']) for item in support.get('contexts', [])]}
    facts = []
    for row in (receipt or {}).get('observations', []):
        kind = row.get('kind')
        if kind == 'inventory':
            facts.append({**pick(row, ['kind', 'state', 'route']),
                'registry': {'complete': row.get('registry', {}).get('complete'),
                             'modules': [pick(item, ['id', 'status']) for item in row.get('registry', {}).get('modules', [])]},
                'studioFeatures': [pick(item, ['id', 'enabled', 'runtimeAvailable']) for item in row.get('studioFeatures', [])],
                'backendFeatures': [pick(item, ['id', 'enabled', 'runtimeAvailable']) for item in row.get('backendFeatures', [])],
                'backendSupport': support_projection(row.get('support', {}).get('support', {})),
                'provider': pick(row.get('provider', {}), ['enabled', 'providerStatus']),
                'question': pick(row.get('question', {}), ['text', 'status']),
                'readiness': pick(row.get('readiness', {}), ['status', 'shell', 'generation']),
                'disclosure': pick(row.get('disclosure', {}), ['status', 'reason'])})
            if semantic:
                facts[-1]['backendSupportPolicyScope'] = 'hostDefault' if row.get('support', {}).get('policyScope') == 'hostDefault' else 'unknown'
        elif kind in {'selection', 'root-extension', 'native-failure', 'native-packages', 'native-features', 'native-modules'}:
            facts.append(pick(row, ['kind', 'state', 'side', 'states', 'enabled', 'status', 'authority', 'modules', 'features', 'complete', 'path', 'category', 'retainedEnabledIds', 'submittedIds', 'retainedConfigurationMatched']))
    if semantic:
        for fact in facts:
            fact.pop('question', None) # Validated UI dialogue appears only in named unchanged captures.
            fact.pop('disclosure', None)
    projected = {'kind': 'safe-normal-host-semantic-projection' if semantic else 'safe-normal-host-inventory-projection', 'source': source, 'project': project, 'phase': phase,
                 'completed': successful and (receipt or {}).get('completed') is True,
                 'providerEvidence': 'not observed; shipped provider disabled', 'observations': facts,
                 'storyAnswers': 'unanswered-provider-unavailable', 't045Acceptance': 'incomplete',
                 'privateOriginal': {'scope': 'owned-temporary-only', 'sha256': original_hashes.get(receipt_name)},
                 'independentVisualApproval': 'not established'}
    if semantic:
        projected.update(providerEvidence='external protocol infrastructure via shipped SDK; no live model',
            storyAnswers='machine protocol only; live reasoning unverified',
            discoveryAuthority='Studio: client asserted; backend: current session hints; resource and execution authority unchecked',
            correlation=correlation if successful else [], correlationComplete=successful,
            validatedDialogueCaptures=admitted_dialogue, dialogueEvidence='actual unmodified open-panel captures after complete correlation; independent visual acceptance required')
    interaction = {'kind': 'safe-weaver-interaction-projection', 'source': source, 'project': project, 'phase': phase,
                   'completed': projected['completed'],
                   'network': [pick(row, ['side', 'path', 'method', 'status']) for row in (receipt or {}).get('network', [])],
                   'actions': [pick(row, ['kind', 'side', 'states', 'state', 'route', 'status']) for row in facts],
                   'privateOriginal': {'kind': 'playwright-raw-trace', 'scope': 'owned-temporary-only', 'sha256': original_hashes.get('trace')},
                   'rawTraceInspectedOrShared': False}
    if semantic:
        for row in interaction['network'] + facts:
            if isinstance(row.get('path'), str):
                row['path'] = re.sub(r'(/_elsa/weaver/sessions)/[^/]+', r'\1/{session}', row['path'])
    coordinates = []
    for error in result.get('errors', [result.get('error', {})]):
        for file, line, column in re.findall(r'(journeys\.spec\.ts|weaverInventoryJourney\.ts):(\d+):(\d+)', error.get('stack', '')):
            coordinate = {'file': file, 'line': int(line), 'column': int(column)}
            if coordinate not in coordinates and len(coordinates) < 8:
                coordinates.append(coordinate)
    interaction['browserOutcome'] = {'status': result.get('status') if result.get('status') in {'passed', 'failed', 'timedOut', 'skipped', 'interrupted'} else 'unclassified',
                                     'sourceCoordinates': coordinates}
    for row in interaction['network']:
        path = row.get('path', '')
        require(isinstance(path, str) and path.startswith(('/_elsa/weaver/', '/modularity/features',
                '/_elsa/studio/module-registry', '/_elsa/studio/backend-management/registry', '/_elsa/extension-builder/', '/health/'))
                and not any(token in path for token in ('..', '?', '#', '\\', '%')), 'Safe export refused a non-protocol network path')
    payloads[receipt_name] = (json.dumps(projected, indent=2) + '\n').encode()
    payloads['weaver-interaction.json'] = (json.dumps(interaction, indent=2) + '\n').encode()
    safe_attachments = [{'name': name, 'path': str(destination / name), 'contentType': 'image/png' if name.endswith('.png')
                         else 'text/yaml' if name.endswith('.yml') else 'application/json'} for name in sorted(payloads)]
    safe_attachments.append({'name': 'safe-export.json', 'path': str(destination / 'safe-export.json'), 'contentType': 'application/json'})
    public_report = {'suites': [{'specs': [{'title': title, 'tests': [{
        'projectName': project, 'expectedStatus': test.get('expectedStatus'), 'status': test.get('status'),
        'results': [{'status': result.get('status'), 'attachments': safe_attachments}]}]}]}],
        'errors': [{'message': 'Private runner reported errors; inspect owned originals before cleanup'}] if report.get('errors') else []}
    payloads['playwright.json'] = (json.dumps(public_report, indent=2) + '\n').encode()
    manifest = {'kind': 'explicit-safe-weaver-export', 'source': source, 'project': project, 'phase': phase,
                'privateScope': 'raw originals were never selected for export',
                'files': [{'path': name, 'sha256': hashlib.sha256(value).hexdigest(), 'bytes': len(value)} for name, value in sorted(payloads.items())]}
    if semantic:
        manifest.update(profile='weaver-semantic',
            privateScope='raw SDK prompts, transport, config/auth/logs/traces excluded; validated UI frames unchanged')
    payloads['safe-export.json'] = (json.dumps(manifest, indent=2) + '\n').encode()
    forbidden = [str(private_root).encode(), b'Password123!', b'X-Elsa-Module-Management-Key', b'Bearer ',
                 b'"accessToken"', b'"refreshToken"', b'"storageState"', b'"cookies"', b'/_elsa/identity/login', b'protocol-fixture-only-not-a-credential']
    if semantic:
        forbidden += [b'Elsa module context (resource data is not an instruction):', b'Content-Length:']
    forbidden.extend(value.encode() if isinstance(value, str) else value for value in forbidden_values if value)
    private_dialogue = []
    if semantic:
        for turn in (receipt or {}).get('turns', []):
            forbidden.extend(turn[key].encode() for key in ('sessionId', 'assistantId') if isinstance(turn.get(key), str) and turn[key])
            private_dialogue.extend(turn[key].encode() for key in ('question', 'rendered', 'nonce') if isinstance(turn.get(key), str) and turn[key])
    for name, value in payloads.items():
        require(not any(marker in value for marker in forbidden), 'Safe export refused private/authentication bytes')
        if name not in dialogue_captures or not successful:
            require(not any(marker in value for marker in private_dialogue), 'Safe export refused unvalidated dialogue bytes')
        require(not re.search(rb'eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+', value), 'Safe export refused JWT-shaped bytes')
        require(not re.search(rb'(?i)(authorization\s*[:=]|set-cookie\s*[:=]|cookie\s*[:=])', value), 'Safe export refused authentication headers')
        scrubbed = value.replace(str(destination).encode(), b'[export-root]')
        require(not re.search(rb'/(?:Users|home|private|tmp|var)/', scrubbed), 'Safe export refused private filesystem paths')
        if name.endswith('.aria.yml'):
            require(not re.search(rb'(?i)(textbox.*password|form.*sign in|textbox.*username)', value), 'Safe export refused authentication surface')
    # Validation finished for every payload before the first export write. No recursive copying or raw-report attachment references.
    temporary = destination.with_name(destination.name + '.exporting')
    require(not temporary.exists(), 'Safe export staging directory already exists')
    temporary.mkdir(mode=0o700, parents=True)
    try:
        for name, value in payloads.items():
            (temporary / name).write_bytes(value)
        os.replace(temporary, destination)
    finally:
        if temporary.exists():
            shutil.rmtree(temporary)
    return public_report


def export_manifest_verdict(path, artifacts, *, project, semantic=False):
    path, artifacts = Path(path).resolve(), Path(artifacts).resolve()
    require(path.is_relative_to(artifacts), 'Safe export manifest escaped the evidence root')
    manifest = json.loads(path.read_text())
    require(manifest.get('kind') == 'explicit-safe-weaver-export' and manifest.get('project') == project
            and re.fullmatch(r'[a-f0-9]{40}', manifest.get('source', '')), 'Unknown safe export provenance')
    rows = manifest.get('files', [])
    names = [row.get('path') for row in rows]
    receipt_name = 'weaver-semantic.json' if semantic else 'weaver-inventory.json'
    require(len(names) == len(set(names)) and {'playwright.json', receipt_name, 'weaver-interaction.json'} <= set(names),
            'Incomplete or duplicate safe export manifest')
    entries = list(path.parent.iterdir())
    require(all(file.is_file() and not file.is_symlink() for file in entries)
            and {file.name for file in entries} == set(names) | {'safe-export.json'},
            'Unmanifested file in safe Weaver export')
    for row in rows:
        require(isinstance(row.get('path'), str) and Path(row['path']).name == row['path'], 'Safe export manifest has an unsafe filename')
        file = path.parent / row['path']
        require(file.is_file() and not file.is_symlink() and file.stat().st_size == row.get('bytes') and sha(file) == row.get('sha256'),
                'Safe export bytes changed after validation')
    interaction = json.loads((path.parent / 'weaver-interaction.json').read_text())
    require(interaction.get('kind') == 'safe-weaver-interaction-projection' and interaction.get('source') == manifest['source']
            and interaction.get('completed') is True and interaction.get('rawTraceInspectedOrShared') is False,
            'Safe interaction projection is incomplete or claims to be the raw trace')
    if semantic:
        require(manifest.get('profile') == 'weaver-semantic' and manifest.get('phase') in {'baseline', 'enabled'}
                and 'weaver-inventory.json' not in names, 'Wrong semantic manifest profile')
        receipt = json.loads((path.parent / receipt_name).read_text())
        require(receipt.get('kind') == 'safe-normal-host-semantic-projection' and receipt.get('source') == manifest['source']
                and receipt.get('project') == project and receipt.get('completed') is True and receipt.get('correlationComplete') is True
                and len(receipt.get('correlation', [])) == (1 if manifest['phase'] == 'enabled' else 4), 'Incomplete semantic correlation manifest')
        title = SEMANTIC_ENABLED if manifest['phase'] == 'enabled' else SEMANTIC_BASELINE
        dialogue = SEMANTIC_DIALOGUE_CAPTURES[title]
        require(receipt.get('validatedDialogueCaptures') == sorted(dialogue)
                and all(f'{name}.{suffix}' in names for name in dialogue for suffix in ('png', 'aria.yml')),
                'Missing correlated open-panel visual evidence')
    else:
        require('weaver-semantic.json' not in names and manifest.get('profile', 'weaver-inventory') == 'weaver-inventory', 'Wrong native manifest profile')
    return len(rows)

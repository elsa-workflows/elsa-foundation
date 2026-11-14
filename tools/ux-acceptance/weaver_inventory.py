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
    result = {'stage': stage, 'category': {'CalledProcessError': 'process-failed', 'TimeoutError': 'readiness-timeout',
              'ValueError': 'evidence-invalid', 'InterruptedError': 'interrupted'}.get(kind, 'runtime-failed')}
    if isinstance(getattr(error, 'returncode', None), int): result['exitCode'] = error.returncode
    if log is not None and Path(log).is_file():
        result['buildCodes'] = sorted(set(re.findall(r'\b(?:NETSDK|NU|CS|MSB)\d{3,5}\b', Path(log).read_text(errors='replace'))))[:8]
    return result


def inventory_verdict(value, *, source, phase):
    """A successful process or existing screenshot cannot replace native state and disclosure evidence."""
    require(value.get('source') == source and value.get('phase') == phase, 'Inventory source/phase mismatch')
    require(value.get('completed') is True and value.get('failedRequests') == [], 'Inventory journey failed or has failed native requests')
    observations = value.get('observations', [])
    expected = ['enabled'] if phase == 'enabled' else ['baseline', 'deselected', 'unknown']
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


def safe_export(private_report, private_root, destination, *, source, project, title, phase,
                forbidden_values=()):
    """Export an allowlisted projection, never recursively copy a Playwright directory.

    Every selected byte is checked before the destination is created, including on failure.
    Raw trace/auth state, API bodies, provider prompts, logs and original report paths stay private.
    """
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
    attachments = result.get('attachments', [])
    names = [item.get('name') for item in attachments]
    require(len(names) == len(set(names)), 'Safe export refuses duplicate attachment identities')
    allowed_captures = {f'{name}.{suffix}' for name in CAPTURES[title] for suffix in ('png', 'aria.yml')}
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
        if name not in allowed_captures | {'trace', 'weaver-inventory.json'}:
            continue # No generic source/error/auth attachment is selected for export.
        value = read(item)
        original_hashes[name] = hashlib.sha256(value).hexdigest()
        if name in allowed_captures:
            if name.endswith('.png'):
                require(value.startswith(b'\x89PNG\r\n\x1a\n'), 'Named screenshot is not a PNG')
            else:
                value.decode('utf-8')
            payloads[name] = value
        elif name == 'weaver-inventory.json':
            receipt = json.loads(value)
    successful = (not report.get('errors') and test.get('expectedStatus') == 'passed' and test.get('status') == 'expected'
                  and result.get('status') == 'passed')
    if successful:
        require(allowed_captures <= set(payloads) and 'trace' in original_hashes and receipt is not None,
                'Successful inventory lacks required private originals')
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
        elif kind in {'selection', 'root-extension', 'native-failure', 'native-packages', 'native-features', 'native-modules'}:
            facts.append(pick(row, ['kind', 'state', 'side', 'states', 'enabled', 'status', 'authority', 'modules', 'features', 'complete', 'path', 'category', 'retainedEnabledIds', 'submittedIds', 'retainedConfigurationMatched']))
    projected = {'kind': 'safe-normal-host-inventory-projection', 'source': source, 'project': project, 'phase': phase,
                 'completed': successful and (receipt or {}).get('completed') is True,
                 'providerEvidence': 'not observed; shipped provider disabled', 'observations': facts,
                 'storyAnswers': 'unanswered-provider-unavailable', 't045Acceptance': 'incomplete',
                 'privateOriginal': {'scope': 'owned-temporary-only', 'sha256': original_hashes.get('weaver-inventory.json')},
                 'independentVisualApproval': 'not established'}
    interaction = {'kind': 'safe-weaver-interaction-projection', 'source': source, 'project': project, 'phase': phase,
                   'completed': projected['completed'],
                   'network': [pick(row, ['side', 'path', 'method', 'status']) for row in (receipt or {}).get('network', [])],
                   'actions': [pick(row, ['kind', 'side', 'states', 'state', 'route', 'status']) for row in facts],
                   'privateOriginal': {'kind': 'playwright-raw-trace', 'scope': 'owned-temporary-only', 'sha256': original_hashes.get('trace')},
                   'rawTraceInspectedOrShared': False}
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
    payloads['weaver-inventory.json'] = (json.dumps(projected, indent=2) + '\n').encode()
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
    payloads['safe-export.json'] = (json.dumps(manifest, indent=2) + '\n').encode()
    forbidden = [str(private_root).encode(), b'Password123!', b'X-Elsa-Module-Management-Key', b'Bearer ',
                 b'"accessToken"', b'"refreshToken"', b'"storageState"', b'"cookies"', b'/_elsa/identity/login', b'protocol-fixture-only-not-a-credential']
    forbidden.extend(value.encode() if isinstance(value, str) else value for value in forbidden_values if value)
    for name, value in payloads.items():
        require(not any(marker in value for marker in forbidden), 'Safe export refused private/authentication bytes')
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


def export_manifest_verdict(path, artifacts, *, project):
    path, artifacts = Path(path).resolve(), Path(artifacts).resolve()
    require(path.is_relative_to(artifacts), 'Safe export manifest escaped the evidence root')
    manifest = json.loads(path.read_text())
    require(manifest.get('kind') == 'explicit-safe-weaver-export' and manifest.get('project') == project
            and re.fullmatch(r'[a-f0-9]{40}', manifest.get('source', '')), 'Unknown safe export provenance')
    rows = manifest.get('files', [])
    names = [row.get('path') for row in rows]
    require(len(names) == len(set(names)) and {'playwright.json', 'weaver-inventory.json', 'weaver-interaction.json'} <= set(names),
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
    return len(rows)

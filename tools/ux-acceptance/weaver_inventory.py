"""Opt-in normal-host inventory proof. Reuses the package qualifier's retained author schema.

This module never builds a replacement Workbench/package consumer or supplies module answers.
The caller owns actual source-built hosts and their existing lifecycle.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import struct
import sys
from types import SimpleNamespace
import uuid
import zipfile

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


def mvid(data):
    """Read Module.Mvid from an actual CLI image, without loading or executing that assembly."""
    def u16(offset): return struct.unpack_from('<H', data, offset)[0]
    def u32(offset): return struct.unpack_from('<I', data, offset)[0]
    pe = u32(0x3c)
    require(data[pe:pe + 4] == b'PE\0\0', 'Not a PE image')
    optional = pe + 24
    magic = u16(optional)
    require(magic in {0x10b, 0x20b}, 'Unknown PE optional header')
    directories = optional + (112 if magic == 0x20b else 96)
    sections = optional + u16(pe + 20)
    def offset(rva):
        for index in range(u16(pe + 6)):
            section = sections + index * 40
            start, size = u32(section + 12), max(u32(section + 8), u32(section + 16))
            if start <= rva < start + size:
                return u32(section + 20) + rva - start
        raise ValueError('CLI RVA is outside the image sections')
    cli = offset(u32(directories + 14 * 8))
    metadata = offset(u32(cli + 8))
    require(data[metadata:metadata + 4] == b'BSJB', 'CLI metadata signature missing')
    stream_header = (metadata + 16 + u32(metadata + 12) + 3) & ~3
    count = u16(stream_header + 2)
    cursor, streams = stream_header + 4, {}
    for _ in range(count):
        end = data.index(b'\0', cursor + 8)
        name = data[cursor + 8:end].decode('ascii')
        streams[name] = metadata + u32(cursor)
        cursor = (end + 4) & ~3
    tables = streams.get('#~', streams.get('#-'))
    require(tables is not None and '#GUID' in streams, 'CLI metadata tables missing')
    heap_sizes = data[tables + 6]
    valid = struct.unpack_from('<Q', data, tables + 8)[0]
    require(valid & 1, 'CLI Module table missing')
    module = tables + 24 + valid.bit_count() * 4
    require(u32(tables + 24) == 1, 'CLI Module table is not unique')
    guid_column = module + 2 + (4 if heap_sizes & 1 else 2)
    guid = u32(guid_column) if heap_sizes & 2 else u16(guid_column)
    require(guid > 0, 'CLI Module MVID missing')
    start = streams['#GUID'] + (guid - 1) * 16
    return str(uuid.UUID(bytes_le=data[start:start + 16]))


def witness_input(root, source):
    root = Path(root).resolve()
    report = json.loads((root / 'report.json').read_text())
    require(report.get('source') == source, 'Weaver witness source differs from the normal-host source')
    require(re.fullmatch(r'4\.0\.0-[A-Za-z0-9.-]+', report.get('version', '')), 'Expected an explicit private qualification version')
    author = report.get('authors', {}).get('protocol', {})
    require(author.get('source') == source and author.get('status') == 'passed' and author.get('sourceReferences') == [],
            'Supply the existing passed, same-source package-only protocol author receipt')
    def retained(item):
        path = Path(item['path']).resolve()
        require(path.is_relative_to(root) and path.is_file() and sha(path) == item['sha256'], 'Retained witness input is missing, changed or outside its root')
        return path
    receipt = json.loads(retained(author['receipt']).read_text())
    require(receipt == {key: value for key, value in author.items() if key != 'receipt'}, 'Protocol author receipt differs from the witness report')
    retained(author['assets']); retained(author['lock'])
    package = author['packed']['package']
    require(package.get('id') == 'Acme.Weaver.ProtocolFixtures', 'Unexpected protocol fixture package')
    retained(package)
    for item in author['cachedArchives']:
        retained(item['archive'])
    return SimpleNamespace(root=root, source=source, report=report, author=author, version=report['version'])


def configure_protocol(root, witness, backend, settings, shells, evidence):
    """Stage genuine archives with the existing locked-graph selector, then use ordinary Nuplane discovery."""
    dependencies = json.loads((backend / 'Elsa.Workbench.deps.json').read_text())
    libraries = {identity.rsplit('/', 1)[0]: identity.rsplit('/', 1)[1] for identity in dependencies['libraries']}
    shared = settings['Nuplane']['Loading']['SharedAssemblies']
    contract_names = ['Elsa.Weaver.GitHubCopilot', 'Microsoft.Extensions.AI.Abstractions', 'Microsoft.Extensions.DependencyInjection.Abstractions']
    if 'Elsa.Weaver.Anthropic' in libraries and (backend / 'Elsa.Weaver.Anthropic.dll').is_file():
        contract_names.append('Elsa.Weaver.Anthropic')
    for name in contract_names:
        require(name in libraries and (backend / (name + '.dll')).is_file(), 'Normal Workbench does not ship required shared contract: ' + name)
        if not any(row['Name'] == name for row in shared):
            shared.append({'Name': name, 'PublicKeyToken': None, 'MajorVersion': int(libraries[name].split('.')[0])})
    # Every declaration is backed by this actual host's dependency graph and deployed image.
    provided_names = {row['Name'] for row in shared} | set(settings['Nuplane']['HostProvidedPackages'])
    provided = {name: libraries[name] for name in provided_names
                if name in libraries and (backend / (name + '.dll')).is_file()}
    require(set(settings['Nuplane']['HostProvidedPackages']) <= set(provided), 'Normal Workbench is missing an existing host-provided Nuplane contract')
    require(all(version == witness.version for name, version in provided.items() if name.startswith('Elsa.')),
            'Source-built Workbench did not use the witness private candidate version')
    settings['Nuplane']['HostProvidedPackages'] = sorted(provided)
    evidence.mkdir(mode=0o700, parents=True, exist_ok=False)
    protocol = evidence / 'protocol'
    protocol.mkdir(mode=0o700)
    feed = backend / 'packages'
    feed.mkdir(exist_ok=False)
    # This is the existing qualifier selector, not an invented manifest or package resolver.
    helper_path = root / 'tools/studio/Weaver.Conformance.Probe'
    sys.path.insert(0, str(helper_path))
    try:
        spec = importlib.util.spec_from_file_location('weaver_inventory_package_inputs', helper_path / 'consumers.py')
        helper = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(helper)
        selection = helper.select_native_archives(SimpleNamespace(root=witness.root, source=witness.source,
            profiles={'backend': {'hostProvidedPackages': provided}}), 'backend', [witness.author['packed']['package']], witness.author)
    finally:
        sys.path.remove(str(helper_path))
    for item in selection:
        shutil.copy2(item['path'], feed / Path(item['path']).name)
    settings['Nuplane']['Setup']['Feeds'] = [{'Name': 'owned-inventory-protocol', 'DirectoryPath': str(feed), 'IncludePatterns': ['*']}]
    settings['Nuplane']['Setup']['AutomaticReconciliation'] = True
    settings.setdefault('Elsa', {}).setdefault('Shells', {})['ReloadOnPackageChange'] = True
    features = shells['CShells']['Shells']['default']['Features']
    require('WeaverAnthropic' not in features, 'Normal inventory selects only the actual Copilot adapter')
    features['WeaverGitHubCopilot'] = {'Enabled': True, 'RuntimeUrl': 'http://127.0.0.1:1', 'UseLoggedInUser': False,
        'GitHubToken': '', 'GitHubTokenEnvironmentVariable': '', 'RuntimeConnectionToken': ''}
    features['AcmeCopilotProtocolFixture'] = {'CommandFile': str(protocol / 'command.json'), 'TraceFile': str(protocol / 'trace.jsonl')}
    (protocol / 'command.json').write_text(json.dumps({'commandId': 'owned-readiness', 'mode': 'observe'}))
    (protocol / 'command.json').chmod(0o600)
    assemblies = {name: {'version': version, 'sha256': sha(backend / (name + '.dll')),
                        'mvid': mvid((backend / (name + '.dll')).read_bytes())} for name, version in provided.items()}
    # Keep source-built bytes and independently packed bytes distinct; record both actual identities.
    candidate_images = {}
    for name, candidate in witness.report.get('packages', {}).items():
        if name not in provided:
            continue
        path = Path(candidate['path']).resolve()
        require(path.is_relative_to(witness.root) and sha(path) == candidate['sha256'], 'Candidate archive changed')
        with zipfile.ZipFile(path) as archive:
            entry = 'lib/net10.0/' + name + '.dll'
            image = archive.read(entry)
        candidate_images[name] = {'archiveSha256': candidate['sha256'], 'sha256': hashlib.sha256(image).hexdigest(), 'mvid': mvid(image)}
    receipt = {'source': witness.source, 'version': witness.version, 'kind': 'normal-source-host', 'hostProvidedPackages': provided,
               'hostAssemblies': assemblies, 'independentCandidateImages': candidate_images, 'runtimeArchiveInputs': selection,
               'nativeAdmission': 'not yet observed; require selected runtime descriptor and provider observation'}
    (evidence / 'composition.json').write_text(json.dumps(receipt, indent=2) + '\n')
    return {'UX_WEAVER_COMMAND': str(protocol / 'command.json'), 'UX_WEAVER_TRACE': str(protocol / 'trace.jsonl'),
            'UX_WEAVER_FIXTURE_VERSION': witness.author['packed']['package']['version']}


def inventory_verdict(value, *, source, phase, require_protocol=True):
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
        if require_protocol:
            disclosure = item.get('disclosure', {})
            require(value.get('providerEvidence') == 'deterministic-provider-protocol' and isinstance(disclosure.get('admitted'), list),
                    'Missing actual backend-admitted context')
            provider = disclosure.get('provider', [])
            require([entry.get('kind') for entry in provider] == ['copilot.request', 'copilot.observation'], 'Missing, failed or tool-calling provider observation')
            admitted = disclosure['admitted']
            require({row.get('id') for row in admitted} >= {'weaver.studio-support', 'weaver.backend-support'}, 'Both admitted host observations are required')
            require(all(entry.get('evidenceKind') == 'deterministic-provider-protocol' for entry in provider), 'Wrong provider evidence category')
            studio = next(row for row in admitted if row.get('id') == 'weaver.studio-support')
            backend = next(row for row in admitted if row.get('id') == 'weaver.backend-support')
            require(studio.get('origin') == 'clientSnapshot' and backend.get('origin') == 'authoritativeRead', 'Host observations lost their authority attribution')
            content = studio.get('content', {})
            require(content.get('side') == 'studio' and content.get('clientAsserted') is True
                    and content.get('completeness') == 'complete' and content.get('registrationCompleteness') == 'complete',
                    'Missing complete actual loader/registration context')
            require(any(row.get('moduleId') == 'workflows' and row.get('status') == 'loaded' for row in content.get('loader', [])), 'Unrelated module loader evidence missing')
            prompt = provider[0].get('data', {}).get('prompt', '')
            contents = [json.loads(line[11:]) for line in prompt.splitlines() if line.startswith('  Content: ')]
            require(studio['content'] in contents and backend['content'] in contents, 'Provider did not receive the actual admitted host observations')
            if state == 'unknown':
                require(item.get('route') == '/qualification-unknown-module'
                        and all(row.get('contentType') == 'weaver.host-support' for row in admitted), 'Unknown route acquired resource/module context')
            admissions = [row for row in observations if row.get('kind') == 'native-package-admission' and row.get('state') == state]
            require(len(admissions) == 1 and any(row.get('packageId') == 'Acme.Weaver.ProtocolFixtures' and row.get('status') == 'loaded'
                    for row in admissions[0].get('modules', [])), 'Missing actual Nuplane package admission')
    selections = [item for item in observations if item.get('kind') == 'selection']
    expected_selections = [('studio', {'ExtensionBuilderStudio': True})] if phase == 'enabled' else [
        ('backend', {'DiagnosticsStructuredLogsEntityFrameworkCore': False, 'DiagnosticsStructuredLogs': False}),
        ('studio', {'StructuredLogsStudio': False})]
    require([(item.get('side'), item.get('states')) for item in selections] == expected_selections, 'Independent native selection observations are missing')
    root = [item for item in observations if item.get('kind') == 'root-extension']
    require(len(root) == 1 and root[0].get('enabled') is (phase == 'enabled')
            and root[0].get('status') in ({401, 403} if phase == 'enabled' else {404}), 'Missing actual root ExtensionBuilder route observation')
    return len(rows)


def safe_export(private_report, private_root, destination, *, source, project, title, phase,
                require_protocol, forbidden_values=(), composition=None):
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
        inventory_verdict(receipt, source=source, phase=phase, require_protocol=require_protocol)
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
            disclosure = row.get('disclosure', {})
            admitted = disclosure.get('admitted', [])
            provider = disclosure.get('provider', [])
            support_rows = []
            for attachment in admitted:
                if attachment.get('id') not in {'weaver.studio-support', 'weaver.backend-support'}:
                    continue
                content = attachment.get('content', {})
                support_rows.append({**pick(attachment, ['id', 'contentType', 'origin']),
                    'contentSha256': hashlib.sha256(json.dumps(content, sort_keys=True).encode()).hexdigest(),
                    'content': {**pick(content, ['side', 'clientAsserted', 'completeness', 'registrationCompleteness', 'host', 'generation']),
                        'support': support_projection(content.get('support', {})),
                        'modules': [pick(item, ['id', 'status']) for item in content.get('modules', [])],
                        'loader': [pick(item, ['moduleId', 'status']) for item in content.get('loader', [])],
                        'registrations': [pick(item, ['kind', 'id', 'originModuleId', 'declaredModuleId', 'resourceModuleIds', 'status'])
                                          for item in content.get('registrations', [])]}})
            facts.append({**pick(row, ['kind', 'state', 'route']),
                'registry': {'complete': row.get('registry', {}).get('complete'),
                             'modules': [pick(item, ['id', 'status']) for item in row.get('registry', {}).get('modules', [])]},
                'studioFeatures': [pick(item, ['id', 'enabled', 'runtimeAvailable']) for item in row.get('studioFeatures', [])],
                'backendFeatures': [pick(item, ['id', 'enabled', 'runtimeAvailable']) for item in row.get('backendFeatures', [])],
                'backendSupport': support_projection(row.get('support', {}).get('support', {})),
                'admittedSupport': support_rows,
                'providerObservations': [{**pick(item, ['kind', 'evidenceKind']),
                    'commandId': item.get('data', {}).get('commandId'),
                    'payloadSha256': hashlib.sha256(json.dumps(item, sort_keys=True).encode()).hexdigest()} for item in provider]})
        elif kind in {'selection', 'root-extension', 'native-package-admission'}:
            facts.append(pick(row, ['kind', 'state', 'side', 'states', 'enabled', 'status', 'authority', 'modules']))
    projected = {'kind': 'safe-normal-host-inventory-projection', 'source': source, 'project': project, 'phase': phase,
                 'completed': successful and (receipt or {}).get('completed') is True,
                 'providerEvidence': (receipt or {}).get('providerEvidence', 'not observed'), 'observations': facts,
                 'privateOriginal': {'scope': 'owned-temporary-only', 'sha256': original_hashes.get('weaver-inventory.json')},
                 'independentVisualApproval': 'not established'}
    interaction = {'kind': 'safe-weaver-interaction-projection', 'source': source, 'project': project, 'phase': phase,
                   'completed': projected['completed'],
                   'network': [pick(row, ['side', 'path', 'method', 'status']) for row in (receipt or {}).get('network', [])],
                   'actions': [pick(row, ['kind', 'side', 'states', 'state', 'route', 'status']) for row in facts],
                   'privateOriginal': {'kind': 'playwright-raw-trace', 'scope': 'owned-temporary-only', 'sha256': original_hashes.get('trace')},
                   'rawTraceInspectedOrShared': False}
    for row in interaction['network']:
        path = row.get('path', '')
        require(isinstance(path, str) and path.startswith(('/_elsa/weaver/', '/modularity/features',
                '/_elsa/studio/module-registry', '/_elsa/studio/backend-management/registry', '/_elsa/extension-builder/'))
                and not any(token in path for token in ('..', '?', '#', '\\', '%')), 'Safe export refused a non-protocol network path')
    payloads['weaver-inventory.json'] = (json.dumps(projected, indent=2) + '\n').encode()
    payloads['weaver-interaction.json'] = (json.dumps(interaction, indent=2) + '\n').encode()
    if composition is not None:
        value = {**pick(composition, ['source', 'version', 'kind', 'hostProvidedPackages', 'hostAssemblies', 'independentCandidateImages']),
                 'runtimeArchiveInputs': [pick(row, ['identity', 'sha256']) for row in composition.get('runtimeArchiveInputs', [])],
                 'nativeAdmission': 'see actual native-package-admission observations; preparation alone is not admission'}
        payloads['composition.json'] = (json.dumps(value, indent=2) + '\n').encode()
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

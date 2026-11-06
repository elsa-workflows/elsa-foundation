#!/usr/bin/env python3
"""Freeze, pack and optionally run a distinct Jobs native/BPMN PackageReference closure.

The standalone verifier and its forbidden-package oracle remain unchanged. Execution
requires a retained passing M2 all-groups receipt. This driver publishes no package feed.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import shutil
import signal
import subprocess
import sys
from types import SimpleNamespace
import uuid
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
HELPER = ROOT / 'tools/jobs/verify-packaged-jobs.py'
BASE = runpy.run_path(str(HELPER))
MODULES = ('Elsa.Jobs.Core', 'Elsa.Jobs', 'Elsa.Jobs.Persistence.EntityFrameworkCore',
           'Elsa.Jobs.Workflows.Runtime', 'Elsa.Jobs.Bpmn', 'Elsa.Activities.Runtime',
           'Elsa.Activities.Primitives', 'Elsa.Activities.Sequence', 'Elsa.Activities.Flowchart',
           'Elsa.Activities.Bpmn', 'Elsa.Workflows.Runtime',
           'Elsa.Workflows.Runtime.Resumption', 'Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore',
           'Elsa.Serialization.SystemText')
sha256, write_json, git = BASE['sha256'], BASE['write_json'], BASE['git']


def validate_m2_gate(path, source_commit, publish_state_commit, provider='sqlite'):
    """Read a complete prior-source M2 package evidence tree without executing product code."""
    path = path.resolve(strict=True)
    root = path.parent
    receipt = json.loads(path.read_text())
    required = ('verificationState', 'packState', 'consumerPublishState', 'consumerExecutionState', 'standaloneSliceState')
    if any(receipt.get(key) != 'PASSED' for key in required) or receipt.get('stage') != 'execute':
        raise ValueError('M2 did not pass every immutable package/execution stage.')
    for key in ('sourceCommit', 'publishStateCommit'):
        if re.fullmatch(r'[0-9a-f]{40}', receipt.get(key, '')) is None:
            raise ValueError('M2 lacks a full immutable ' + key + '.')
    if receipt['sourceCommit'] != source_commit or receipt['publishStateCommit'] != publish_state_commit:
        raise ValueError('M2 source/publish-state identities differ from the explicitly selected prerequisite.')
    if receipt.get('witnessGroup') != 'all' or receipt.get('sourceCleanAfterVerification') is not True or receipt.get('harnessUnchanged') is not True:
        raise ValueError('M2 all-groups, clean-source and unchanged-harness proof is required.')
    if receipt.get('fullAcceptanceState') != 'NOT RUN' or receipt.get('standaloneAcceptanceState') != 'INCOMPLETE':
        raise ValueError('M2 must preserve its product/model/UI acceptance boundary.')
    # These are the reviewed source-owned identities, not a count or a plan supplied by the receipt.
    controller = runpy.run_path(str(ROOT / 'tools/jobs/PackageProbe/run_witness.py'))
    controls = runpy.run_path(str(ROOT / 'tools/jobs/PackageProbe/m2_witness.py'))
    expected = list(controller['M1_CASE_NAMES']) + controls['planned_names'](SimpleNamespace(provider=provider), 'all')
    if len(expected) != 27 or len(set(expected)) != 27:
        raise ValueError('The source-owned M2 plan changed; review the gate before execution.')

    def require_cases(cases):
        if not isinstance(cases, list) or [case.get('name') for case in cases] != expected or any(case.get('state') != 'PASSED' for case in cases):
            raise ValueError('M2 requires the exact 27 unique passed source-owned case identities.')

    cases = receipt.get('standaloneCases')
    require_cases(cases)
    consumer = receipt['consumer']
    if consumer.get('provider') != provider or consumer.get('restoredProjectLibraries') != 0 or tuple(consumer.get('packageRoots', [])) != tuple(BASE['MODULES']):
        raise ValueError('M2 consumer provider/package roots/project-library boundary differs.')
    recorded_deployment = Path(consumer['deployment'])
    recorded_entry = Path(consumer['entryAssembly'])
    recorded_witness = Path(receipt['standaloneWitnessReceipt'])
    if recorded_deployment.name != 'deployment' or recorded_entry != recorded_deployment / 'Jobs.PackageProbe.dll' or \
            recorded_witness != recorded_deployment.parent / 'standalone-witness/witness-receipt.json':
        raise ValueError('M2 entry and nested witness do not belong to the same package evidence root.')
    # Canonical relative locations preserve original hashed receipts when the complete raw tree is relocated.
    witness_path = root / 'standalone-witness/witness-receipt.json'
    if sha256(witness_path) != receipt.get('standaloneWitnessReceiptSha256'):
        raise ValueError('M2 nested witness hash differs.')
    witness = json.loads(witness_path.read_text())
    if witness.get('state') != 'PASSED' or witness.get('ownedResourcesCleaned') is not True or witness.get('provider') != provider or \
            witness.get('witnessGroup') != 'all' or witness.get('negativeControl') is not None or witness.get('fullJobsAcceptanceState') != 'NOT RUN':
        raise ValueError('M2 nested state/provider/cleanup/positive-slice boundary differs.')
    require_cases(witness.get('cases'))
    if witness['cases'] != cases or witness.get('plannedCaseNames') != expected:
        raise ValueError('M2 nested cases or complete plan differ from the package receipt.')
    version = witness.get('providerVersion')
    if not isinstance(version, dict) or version.get('provider') != ('Sqlite' if provider == 'sqlite' else 'PostgreSql') or \
            not isinstance(version.get('version'), str) or not version['version'] or version != receipt.get('executedProviderVersion'):
        raise ValueError('M2 actual provider engine/version proof differs.')
    if witness.get('entryAssembly') != str(recorded_entry):
        raise ValueError('M2 witness executed a different consumer entry.')
    deployment = root / 'deployment'
    files = consumer['deploymentFilesSha256']
    entry_hash = sha256(deployment / recorded_entry.name)
    if entry_hash != witness.get('entrySha256') or entry_hash != files.get(recorded_entry.name):
        raise ValueError('M2 executed/deployed consumer entry hash differs.')
    source = root / 'source'
    if sha256(source / 'tools/jobs/verify-packaged-jobs.py') != receipt.get('harnessSha256'):
        raise ValueError('M2 retained source harness differs from its frozen hash.')
    controller_hash = sha256(source / 'tools/jobs/PackageProbe/run_witness.py')
    if controller_hash != receipt.get('consumerControllerSha256') or controller_hash != witness.get('controllerSha256'):
        raise ValueError('M2 executed controller differs from its immutable source.')
    controller_files = receipt.get('consumerControllerFilesSha256', {})
    if not {'tools/jobs/PackageProbe/run_witness.py', 'tools/jobs/PackageProbe/m2_witness.py'} <= controller_files.keys():
        raise ValueError('M2 source lacks the complete controller/control-group hash set.')
    for name, digest in controller_files.items():
        target = (source / name).resolve(strict=True)
        if not target.is_relative_to(source.resolve()) or sha256(target) != digest:
            raise ValueError('M2 retained controller source hash/boundary differs.')

    def identities(rows):
        if not isinstance(rows, list) or not rows or len({row['id'].casefold() for row in rows}) != len(rows):
            raise ValueError('M2 package identity set is empty or duplicated.')
        return {row['id']: row['version'] for row in rows}

    packages, restored, closure = receipt['packages'], consumer['resolvedPackedPackages'], receipt['closure']
    if identities(packages) != identities(restored) or identities(packages) != identities(closure):
        raise ValueError('M2 complete packed/restored/source closure identities differ.')
    BASE['require_standalone'](identities(packages))
    restored_by_id = {row['id']: row for row in restored}
    package_by_id = {row['id']: row for row in packages}
    computation = json.loads((root / 'computation.json').read_text())
    if computation.get('commit') != source_commit or computation.get('last_publish_commit') != receipt.get('lastPublishCommit'):
        raise ValueError('M2 version computation source/publish boundary differs.')
    computed = {row['package_id']: row for row in computation['packages']}
    for package in packages:
        if package.get('sourceCommit') != source_commit or package.get('fingerprint') != computed[package['id']]['fingerprint'] or \
                package.get('archiveSha512') != restored_by_id[package['id']].get('archiveSha512'):
            raise ValueError('M2 package source/restored archive binding differs.')
        actual = BASE['package_metadata'](root / 'feed' / Path(package['path']).name)
        if any(actual[key] != package.get(key) for key in ('id', 'version', 'sourceCommit', 'fingerprint', 'archiveSha256', 'archiveSha512')):
            raise ValueError('M2 retained package archive differs from its immutable provenance.')
    deployed = consumer['deployedJobsAssets']
    if len(deployed) != 3 or set(identities(deployed)) != set(BASE['MODULES']):
        raise ValueError('M2 lacks exactly three verified deployed Jobs roots.')
    for asset in deployed:
        name = Path(asset['asset']).name
        if asset['version'] != package_by_id[asset['id']]['version'] or asset.get('sha256') != files.get(name) or sha256(deployment / name) != asset['sha256']:
            raise ValueError('M2 actual deployed Jobs assembly binding differs.')
    if not receipt.get('commands') or any(command.get('exitCode') != 0 or command.get('timedOutSeconds') is not None for command in receipt['commands']):
        raise ValueError('M2 package command failed, timed out or was not completed.')
    calculations = [record['command'] for record in receipt['commands'] if any('tools/versioning/Elsa.Versioning.Calculator' in str(part) for part in record['command'])]
    if len(calculations) != 1:
        raise ValueError('M2 lacks one exact recorded version-calculation invocation.')
    for flag, expected_value in (('--commit', source_commit), ('--record-ref', publish_state_commit)):
        command = calculations[0]
        if command.count(flag) != 1 or command[command.index(flag) + 1] != expected_value:
            raise ValueError('M2 calculation did not use its selected immutable ' + flag + '.')
    return {'path': str(path), 'sha256': sha256(path), 'sourceCommit': receipt['sourceCommit'],
        'publishStateCommit': receipt['publishStateCommit'], 'provider': provider, 'engine': version,
        'witnessReceiptSha256': sha256(witness_path), 'entrySha256': entry_hash, 'cases': expected,
        'sourceBoundary': 'PRIOR_SOURCE_M2_SLICE_ONLY', 'claimsCurrentM3SourceQualification': False}


def model_closure(graph):
    if graph.get('schema_version') != 2:
        raise ValueError('Unknown dependency map schema.')
    nodes = graph['nodes']
    paths = {node['path']: node for node in nodes}
    packages = {node['package_id'].casefold(): node for node in nodes if node['packable']}
    if len(paths) != len(nodes) or len(packages) != sum(bool(node['packable']) for node in nodes):
        raise ValueError('Duplicate immutable map project/package identity.')
    ordered, seen, active = [], set(), set()

    def visit(path):
        if path in active:
            raise ValueError('Dependency cycle: ' + path)
        if path in seen:
            return
        active.add(path)
        for edge in paths[path]['edges']:
            if edge['type'] == 'internal':
                visit(edge['path'])
        active.remove(path)
        seen.add(path)
        ordered.append(paths[path])

    for package in MODULES:
        if package.casefold() not in packages:
            raise ValueError('Immutable model source lacks package: ' + package)
        visit(packages[package.casefold()]['path'])
    return ordered


def publish_consumer(receipt, evidence, source, feed, run, environment):
    consumer, deployment = evidence / 'consumer', evidence / 'deployment'
    consumer.mkdir()
    for ancestor in consumer.parents:
        if any((ancestor / name).exists() for name in ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')):
            raise ValueError('External consumer would inherit ambient build configuration.')
    template = source / 'tools/jobs/ModelPackageProbe'
    templates = sorted(template.glob('*.cs'))
    if not templates or not (template / 'Program.cs').is_file():
        raise ValueError('The immutable model probe is missing.')
    for path in templates:
        shutil.copyfile(path, consumer / path.name)
    receipt['consumerSourceSha256'] = {str(path.relative_to(source)): sha256(path) for path in templates}
    # Compile the identical existing controlled report handler. Its producer helper is irrelevant
    # to the model consumer, so retain the exact using declarations and handler/DTO source span.
    shared = source / 'tools/jobs/PackageProbe/ControlledReport.cs'
    body = shared.read_text()
    prefix, marker = 'internal static partial class Probe', 'internal sealed record ControlledReportInput'
    if body.count(prefix) != 1 or body.count(marker) != 1:
        raise ValueError('The immutable shared handler source layout changed; review the extraction.')
    extracted = body[:body.index(prefix)] + body[body.index(marker):]
    handler = consumer / 'ControlledReport.shared.cs'
    handler.write_text(extracted)
    receipt['sharedHandler'] = {'source': str(shared.relative_to(source)), 'sourceSha256': sha256(shared),
                                'compiledSourceSha256': sha256(handler), 'handler': 'ControlledReportHandler',
                                'support': 'package.report/2', 'implementation': 'package.report.implementation.v2'}
    shutil.copyfile(source / 'global.json', consumer / 'global.json')
    versions = {item['id']: item['version'] for item in receipt['closure']}
    central = {item.attrib['Include']: item.attrib['Version'] for item in ET.parse(source / 'Directory.Packages.props').findall('.//PackageVersion')}
    references = {package: versions[package] for package in MODULES}
    for package in ('Microsoft.Extensions.Hosting', 'Microsoft.EntityFrameworkCore.Sqlite', 'SQLitePCLRaw.bundle_e_sqlite3'):
        references[package] = central[package]
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    properties = ET.SubElement(project, 'PropertyGroup')
    for key, value in {'OutputType': 'Exe', 'TargetFramework': 'net10.0', 'ImplicitUsings': 'enable',
                       'Nullable': 'enable', 'RestorePackagesWithLockFile': 'true'}.items():
        ET.SubElement(properties, key).text = value
    items = ET.SubElement(project, 'ItemGroup')
    for package, version in sorted(references.items()):
        ET.SubElement(items, 'PackageReference', Include=package, Version='[' + version + ']')
    ET.indent(project)
    project_path = consumer / 'Jobs.ModelPackageProbe.csproj'
    ET.ElementTree(project).write(project_path, encoding='utf-8', xml_declaration=True)
    config = consumer / 'NuGet.Config'
    BASE['public_nuget_config'](source, config, feed, versions)
    env = environment.copy()
    env['NUGET_PACKAGES'] = str(evidence / 'consumer-packages')
    env['NUGET_HTTP_CACHE_PATH'] = str(evidence / 'consumer-http-cache')
    run(['dotnet', '--version'], consumer, env)
    run(['dotnet', 'restore', project_path, '--configfile', config], consumer, env)
    assets_path = consumer / 'obj/project.assets.json'
    assets = json.loads(assets_path.read_text())
    libraries = assets['libraries']
    if any(item.get('type') != 'package' for item in libraries.values()):
        raise ValueError('Model consumer resolved a source project or non-package library.')
    packed = {item['id'].casefold(): item for item in receipt['packages']}
    restored = []
    for key, item in libraries.items():
        package, version = key.rsplit('/', 1)
        expected = packed.get(package.casefold())
        if expected is None:
            if package.casefold().startswith('elsa.') and not package.casefold().startswith('elsa.specifications.'):
                raise ValueError('An Elsa dependency escaped the packed model closure: ' + key)
            continue
        archive = Path(env['NUGET_PACKAGES']) / item['path'] / (package.lower() + '.' + version.lower() + '.nupkg')
        digest = base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode()
        if version != expected['version'] or digest != expected['archiveSha512']:
            raise ValueError('Restored model package differs from its frozen archive: ' + key)
        restored.append({'id': package, 'version': version, 'archiveSha512': digest})
    for package in MODULES:
        if package + '/' + versions[package] not in libraries:
            raise ValueError('The external consumer omitted a selected model package root: ' + package)
    run(['dotnet', 'restore', project_path, '--locked-mode', '--configfile', config], consumer, env)
    run(['dotnet', 'publish', project_path, '-c', 'Release', '--no-restore', '--output', deployment], consumer, env)
    entry = deployment / 'Jobs.ModelPackageProbe.dll'
    deps_path, runtime_path = entry.with_suffix('.deps.json'), entry.with_suffix('.runtimeconfig.json')
    deps = json.loads(deps_path.read_text())
    project_libraries = [key.rsplit('/', 1)[0] for key, item in deps['libraries'].items() if item.get('type') == 'project']
    if project_libraries != ['Jobs.ModelPackageProbe']:
        raise ValueError('The model deployment contains an unexpected project reference.')
    target = deps['targets'][deps['runtimeTarget']['name']]
    deployed = []
    # Verify every deployed packed runtime assembly, including the native/BPMN/runtime closure.
    for key, item in target.items():
        package, version = key.rsplit('/', 1)
        expected = packed.get(package.casefold())
        if expected is None:
            continue
        if version != expected['version'] or deps['libraries'].get(key, {}).get('type') != 'package':
            raise ValueError('A deployed model dependency lost its exact packed identity.')
        with zipfile.ZipFile(expected['path']) as archive:
            for asset in item.get('runtime', {}):
                if not asset.endswith('.dll'):
                    continue
                output = deployment / Path(asset).name
                if not output.is_file() or hashlib.sha256(archive.read(asset)).hexdigest() != sha256(output):
                    raise ValueError('Deployed model assembly differs from its packed asset: ' + key + '/' + asset)
                deployed.append({'id': package, 'version': version, 'asset': asset, 'sha256': sha256(output)})
    for package in MODULES:
        if not any(item['id'].casefold() == package.casefold() for item in deployed):
            raise ValueError('Selected model package has no verified deployed assembly: ' + package)
    receipt['consumer'] = {'packageRoots': list(MODULES), 'references': references, 'restoredProjectLibraries': 0,
        'resolvedPackedPackages': restored, 'deployedPackedAssets': deployed,
        'resolvedExternalPackages': [{'id': key.rsplit('/', 1)[0], 'version': key.rsplit('/', 1)[1], 'sha512': item.get('sha512')}
            for key, item in sorted(libraries.items()) if key.rsplit('/', 1)[0].casefold() not in packed],
        'restoreDiagnostics': [{key: item[key] for key in ('code', 'level', 'libraryId', 'message') if key in item} for item in assets.get('logs', [])],
        'assetsSha256': sha256(assets_path), 'lockSha256': sha256(consumer / 'packages.lock.json'),
        'nugetConfigSha256': sha256(config), 'entryAssembly': str(entry), 'deployment': str(deployment),
        'runtimeConfigSha256': sha256(runtime_path),
        'deploymentFilesSha256': {str(path.relative_to(deployment)): sha256(path) for path in sorted(deployment.rglob('*')) if path.is_file()}}
    receipt['consumerPublishState'] = 'PASSED'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    if not __debug__:
        parser.error('Run without Python optimization; witness assertions must remain enabled.')
    parser.add_argument('--source-ref', required=True, type=BASE['immutable_commit'])
    parser.add_argument('--publish-state-ref', required=True, type=BASE['immutable_commit'])
    parser.add_argument('--evidence-dir', required=True, type=Path)
    parser.add_argument('--stage', choices=('pack', 'publish', 'execute'), default='pack')
    parser.add_argument('--m2-receipt', type=Path)
    parser.add_argument('--m2-source-ref', type=BASE['immutable_commit'])
    parser.add_argument('--m2-publish-state-ref', type=BASE['immutable_commit'])
    parser.add_argument('--command-timeout-seconds', type=int, default=3600)
    args = parser.parse_args()
    if args.command_timeout_seconds <= 0:
        parser.error('Command timeout must be positive.')
    m2 = None
    if args.stage == 'execute':
        if any(value is None for value in (args.m2_receipt, args.m2_source_ref, args.m2_publish_state_ref)):
            parser.error('M3 execution requires the full raw M2 receipt tree and its explicitly selected source/publish-state commits.')
        try:
            m2 = validate_m2_gate(args.m2_receipt, args.m2_source_ref, args.m2_publish_state_ref)
        except (OSError, ValueError, KeyError, TypeError, IndexError, zipfile.BadZipFile) as error:
            parser.error('The selected M2 prerequisite is not qualified: ' + str(error))
    base = args.evidence_dir.expanduser().resolve()
    if base.is_relative_to(ROOT) or ROOT.is_relative_to(base):
        parser.error('Use a dedicated evidence directory outside the checkout.')
    evidence = base / ('run-' + uuid.uuid4().hex)
    evidence.mkdir(parents=True)
    source, feed = evidence / 'source', evidence / 'feed'
    feed.mkdir()
    receipt = {'schemaVersion': 1, 'profile': 'jobs-native-genuine-bpmn', 'sourceCommit': args.source_ref,
        'publishStateCommit': args.publish_state_ref, 'stage': args.stage, 'packState': 'NOT RUN',
        'consumerPublishState': 'NOT RUN', 'consumerExecutionState': 'NOT RUN', 'modelSliceState': 'NOT RUN',
        'fullAcceptanceState': 'INCOMPLETE', 'commands': [], 'harnessSha256': sha256(Path(__file__)), 'helperSha256': sha256(HELPER)}
    if m2 is not None:
        receipt['m2Gate'] = m2
    environment = BASE['clean_environment']()
    environment['NUGET_PACKAGES'] = str(evidence / 'source-packages')
    environment['NUGET_HTTP_CACHE_PATH'] = str(evidence / 'source-http-cache')

    def run(command, cwd=source, env=environment):
        path = evidence / f'{len(receipt["commands"]) + 1:02d}-command.log'
        record = {'command': list(map(str, command)), 'cwd': str(cwd), 'log': str(path)}
        receipt['commands'].append(record)
        print(f'[{len(receipt["commands"])}] ' + ' '.join(map(str, command[:5])), flush=True)
        try:
            with path.open('w') as output:
                process = subprocess.Popen(list(map(str, command)), cwd=cwd, env=env, stdin=subprocess.DEVNULL,
                    stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
                try:
                    code = process.wait(timeout=args.command_timeout_seconds)
                except subprocess.TimeoutExpired:
                    try:
                        os.killpg(process.pid, signal.SIGTERM)
                        process.wait(timeout=10)
                    except ProcessLookupError:
                        pass
                    except subprocess.TimeoutExpired:
                        os.killpg(process.pid, signal.SIGKILL)
                        process.wait(timeout=10)
                    raise
            record['exitCode'] = code
            if code:
                raise RuntimeError('Command failed; inspect ' + str(path))
        finally:
            record['logSha256'] = sha256(path)
            write_json(evidence / 'receipt.json', receipt)

    print('Jobs model evidence: ' + str(evidence), flush=True)
    code = 1
    try:
        run(['git', 'clone', '--shared', '--no-checkout', ROOT, source], evidence)
        run(['git', 'checkout', '--detach', args.source_ref])
        if git('rev-parse', 'HEAD', cwd=source) != args.source_ref or git('status', '--porcelain', cwd=source):
            raise ValueError('The source clone is not the clean immutable commit.')
        if sha256(source / 'tools/jobs/verify-packaged-job-models.py') != receipt['harnessSha256'] or sha256(source / HELPER.relative_to(ROOT)) != receipt['helperSha256']:
            raise ValueError('The executed driver/helpers do not match the immutable source.')
        plans = runpy.run_path(str(source / 'tools/jobs/ModelPackageProbe/run_models.py'))
        receipt['modelCases'] = [{'name': name, 'state': 'NOT RUN'} for name in plans['PLANNED']]
        receipt['remainingCases'] = [{'name': name, 'state': 'NOT RUN'} for name in plans['REMAINING']]
        props = evidence / 'package-versions.props'
        run(['dotnet', 'run', '--project', 'tools/versioning/Elsa.Versioning.Calculator', '--', '--repo', source,
             '--commit', args.source_ref, '--record-ref', args.publish_state_ref, '--output', evidence / 'computation.json',
             '--pack-properties', props, '--branch', 'agent/codex/3586-background-jobs'])
        computation = json.loads((evidence / 'computation.json').read_text())
        if computation['commit'] != args.source_ref:
            raise ValueError('The version calculator returned another source commit.')
        receipt['lastPublishCommit'] = computation['last_publish_commit']
        run(['dotnet', 'run', '--project', 'tools/maps/Elsa.Maps.Generator', '--', 'dependency-map'])
        closure = model_closure(json.loads((source / 'docs/maps/dependency-map.json').read_text()))
        versions = BASE['computed_versions'](props)
        receipt['closure'] = [{'id': node['package_id'], 'path': node['path'], 'version': versions[Path(node['path']).stem]}
                              for node in closure if node['packable']]
        solution = evidence / 'jobs-model-pack.slnf'
        write_json(solution, {'solution': {'path': str(source / 'Elsa.Server.slnx'), 'projects': [node['path'] for node in closure]}})
        property_arg = '-p:CustomBeforeDirectoryBuildProps=' + str(props)
        run(['dotnet', 'restore', solution, '--locked-mode', property_arg])
        run(['dotnet', 'build', solution, '-c', 'Release', '--no-restore', property_arg])
        run(['dotnet', 'pack', solution, '-c', 'Release', '--no-build', '--no-restore', property_arg, '--output', feed])
        records = [BASE['package_metadata'](path) for path in sorted(feed.glob('*.nupkg'))]
        expected = {item['id']: item['version'] for item in receipt['closure']}
        if len(records) != len(expected) or {item['id']: item['version'] for item in records} != expected:
            raise ValueError('The model package archive set differs from the complete computed closure.')
        computed = {item['package_id']: item for item in computation['packages']}
        for record in records:
            if record['sourceCommit'] != args.source_ref or record['fingerprint'] != computed[record['id']]['fingerprint']:
                raise ValueError('A model archive lost its exact source/input fingerprint.')
        receipt['packages'], receipt['packState'] = records, 'PASSED'
        if args.stage in ('publish', 'execute'):
            publish_consumer(receipt, evidence, source, feed, run, environment)
        if args.stage == 'execute':
            controller = source / 'tools/jobs/ModelPackageProbe/run_models.py'
            witness = evidence / 'model-witness'
            receipt['controllerSha256'] = sha256(controller)
            receipt['consumerExecutionState'] = 'IN PROGRESS'
            try:
                run([sys.executable, controller, '--entry', receipt['consumer']['entryAssembly'], '--evidence-dir', witness],
                    Path(receipt['consumer']['deployment']))
            finally:
                proof = witness / 'witness-receipt.json'
                if proof.is_file():
                    result = json.loads(proof.read_text())
                    receipt.update(modelWitnessReceipt=str(proof), modelWitnessReceiptSha256=sha256(proof),
                        modelCases=result['cases'], remainingCases=result['remainingCases'], modelSliceState=result['state'])
            if not proof.is_file():
                raise ValueError('The model controller returned without its retained witness receipt.')
            if receipt['modelSliceState'] != 'PASSED' or not result['ownedProcessesCleaned']:
                raise ValueError('The model slice failed or retained an owned process.')
            receipt['consumerExecutionState'] = 'PASSED'
        if git('status', '--porcelain', cwd=source):
            raise ValueError('The immutable source changed during model verification.')
        receipt['sourceCleanAfterVerification'] = True
        code = 0
    except (OSError, ValueError, KeyError, TypeError, RuntimeError, subprocess.SubprocessError, ET.ParseError, zipfile.BadZipFile) as error:
        receipt['error'] = str(error) or type(error).__name__
        if receipt['consumerExecutionState'] == 'IN PROGRESS':
            receipt['consumerExecutionState'] = 'FAILED'
        print(receipt['error'], file=sys.stderr)
    finally:
        receipt['harnessUnchanged'] = sha256(Path(__file__)) == receipt['harnessSha256'] and sha256(HELPER) == receipt['helperSha256']
        if not receipt['harnessUnchanged']:
            code = 1
        receipt['verificationState'] = 'PASSED' if code == 0 else 'FAILED'
        write_json(evidence / 'receipt.json', receipt)
    return code


if __name__ == '__main__':
    sys.exit(main())

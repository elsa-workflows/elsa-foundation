#!/usr/bin/env python3
"""Pack an immutable Jobs closure and publish a clean PackageReference consumer.

No packages are published and publish-state is never modified. The pack stage proves
archive construction/provenance only. The publish stage compiles a committed real
PackageProbe. The execute stage runs its explicitly bounded standalone witness;
it does not claim complete Jobs, model or Studio acceptance.
"""

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[2]
MODULES = ('Elsa.Jobs.Core', 'Elsa.Jobs', 'Elsa.Jobs.Persistence.EntityFrameworkCore')
PROVIDERS = {'sqlite': 'Microsoft.EntityFrameworkCore.Sqlite',
             'postgresql': 'Npgsql.EntityFrameworkCore.PostgreSQL'}
FORBIDDEN_PACKAGES = ('elsa.workflows', 'elsa.activities', 'elsa.studio', 'elsa.expressions',
                      'elsa.workbench', 'elsa3', 'elsa.agent', 'elsa.etl', 'elsa.rules', 'bpmn')


def git(*arguments, cwd=ROOT):
    return subprocess.check_output(['git', *map(str, arguments)], cwd=cwd, text=True).strip()


def immutable_commit(value):
    if not re.fullmatch(r'[0-9a-f]{40}', value):
        raise argparse.ArgumentTypeError('Use a full lowercase commit SHA, not a branch or tag.')
    try:
        resolved = git('rev-parse', '--verify', value + '^{commit}')
    except subprocess.CalledProcessError as error:
        raise argparse.ArgumentTypeError('Commit is unavailable locally; fetch it first.') from error
    if resolved != value:
        raise argparse.ArgumentTypeError('The supplied object must itself be a commit.')
    return value


def sha256(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1_048_576), b''):
            digest.update(block)
    return digest.hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def require_standalone(package_ids):
    forbidden = sorted(package for package in package_ids if any(
        package.casefold() == prefix or package.casefold().startswith(prefix + '.')
        for prefix in FORBIDDEN_PACKAGES))
    if forbidden:
        raise ValueError('Standalone Jobs closure contains unrelated model/UI packages: ' + ', '.join(forbidden))


def closure_for(graph):
    if graph.get('schema_version') != 2:
        raise ValueError('Unknown dependency-map schema.')
    nodes = graph['nodes']
    by_path = {node['path']: node for node in nodes}
    by_id = {node['package_id'].casefold(): node for node in nodes if node['packable']}
    if len(by_path) != len(nodes) or len(by_id) != sum(bool(node['packable']) for node in nodes):
        raise ValueError('Dependency map has duplicate project paths/package IDs.')
    missing = [package for package in MODULES if package.casefold() not in by_id]
    if missing:
        raise ValueError('Immutable source lacks Jobs modules: ' + ', '.join(missing))
    seen, visiting, ordered = set(), set(), []

    def visit(path):
        if path in visiting:
            raise ValueError('Dependency-map cycle at ' + path)
        if path in seen:
            return
        visiting.add(path)
        node = by_path[path]
        for edge in node['edges']:
            if edge['type'] == 'internal':
                visit(edge['path'])
        visiting.remove(path)
        seen.add(path)
        ordered.append(node)

    for package in MODULES:
        visit(by_id[package.casefold()]['path'])
    require_standalone(node['package_id'] for node in ordered if node['packable'])
    return ordered


def package_metadata(path):
    with zipfile.ZipFile(path) as archive:
        names = [name for name in archive.namelist() if '/' not in name and name.endswith('.nuspec')]
        if len(names) != 1:
            raise ValueError('Expected one root nuspec: ' + path.name)
        root = ET.fromstring(archive.read(names[0]))
        metadata = next(node for node in root if node.tag.rsplit('}', 1)[-1] == 'metadata')
        fields = {node.tag.rsplit('}', 1)[-1]: node for node in metadata}
        return {'id': fields['id'].text, 'version': fields['version'].text,
                'sourceCommit': fields['repository'].attrib['commit'],
                'fingerprint': json.loads(archive.read('elsa-input-fingerprint.json'))['fingerprint'],
                'archiveSha512': base64.b64encode(hashlib.sha512(path.read_bytes()).digest()).decode(),
                'archiveSha256': sha256(path), 'path': str(path.resolve())}


def computed_versions(props):
    versions = {}
    for group in ET.parse(props).getroot().findall('PropertyGroup'):
        version = group.findtext('ElsaComputedPackageVersion')
        if version:
            match = re.fullmatch(r"'\$\(MSBuildProjectName\)' == '([^']+)'", group.attrib['Condition'])
            if match is None or match[1] in versions:
                raise ValueError('Unexpected/duplicate computed package property group.')
            versions[match[1]] = version
    return versions


def public_nuget_config(source, destination, feed, package_ids):
    config = ET.parse(source / 'NuGet.config')
    if config.find('packageSourceCredentials') is not None:
        raise ValueError('The public local-package witness does not consume NuGet credentials.')
    sources = config.find('packageSources')
    mappings = config.find('packageSourceMapping')
    if sources is None or mappings is None or sources.find('clear') is None:
        raise ValueError('Source NuGet.config must clear sources and define source mappings.')
    key = 'JobsLocalEvidence'
    if any(item.get('key') == key for item in sources.findall('add')):
        raise ValueError('Reserved local-package feed name already exists.')
    for item in sources.findall('add'):
        value = item.get('value', '')
        if re.search(r'://[^/]*@', value):
            raise ValueError('The local-package witness does not accept credential-bearing feed URLs.')
    ET.SubElement(sources, 'add', key=key, value=str(feed.resolve()))
    mapping = ET.SubElement(mappings, 'packageSource', key=key)
    # Exact mappings win over broad package patterns. Archive hashes below still verify
    # what restore actually selected, rather than relying on mapping intent alone.
    for package in sorted(package_ids):
        ET.SubElement(mapping, 'package', pattern=package)
    ET.indent(config)
    config.write(destination, encoding='utf-8', xml_declaration=True)


def clean_environment():
    environment = os.environ.copy()
    for name in list(environment):
        if name in ('GH_TOKEN', 'GITHUB_TOKEN', 'CustomBeforeDirectoryBuildProps', 'MSBuildExtensionsPath',
                    'MSBuildSDKsPath', 'ELSA_EF_CONNECTION', 'ELSA_EF_SCHEMA', 'DOTNET_STARTUP_HOOKS',
                    'DOTNET_ADDITIONAL_DEPS', 'DOTNET_SHARED_STORE', 'ASPNETCORE_HOSTINGSTARTUPASSEMBLIES') \
                or name.startswith('NuGetPackageSourceCredentials_'):
            environment.pop(name, None)
    return environment


def publish_consumer(receipt, evidence, source, feed, run, environment, provider):
    template = source / 'tools/jobs/PackageProbe'
    if not (template / 'Program.cs').is_file():
        raise ValueError('The immutable source has no real Jobs PackageProbe; consumer execution remains NOT RUN.')
    consumer, deployment = evidence / 'consumer', evidence / 'deployment'
    consumer.mkdir()
    for ancestor in consumer.parents:
        for name in ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props'):
            if (ancestor / name).exists():
                raise ValueError('Consumer would inherit ambient build configuration: ' + str(ancestor / name))
    templates = sorted(template.glob('*.cs'))
    for item in templates:
        shutil.copyfile(item, consumer / item.name)
    receipt['consumerSourceSha256'] = {str(item.relative_to(source)): sha256(item) for item in templates}
    shutil.copyfile(source / 'global.json', consumer / 'global.json')
    versions = {item['id']: item['version'] for item in receipt['closure']}
    central = {item.attrib['Include']: item.attrib['Version']
               for item in ET.parse(source / 'Directory.Packages.props').findall('.//PackageVersion')}
    references = {package: versions[package] for package in MODULES}
    references[PROVIDERS[provider]] = central[PROVIDERS[provider]]
    if provider == 'sqlite':
        # The repository's reviewed host engine envelope pins the native bundle
        # above EF's vulnerable minimum. An external host has no ambient central
        # transitive pin, so its own explicit PackageReference carries that choice.
        native_bundle = 'SQLitePCLRaw.bundle_e_sqlite3'
        references[native_bundle] = central[native_bundle]
    # An eventual real probe may need ordinary DI/hosting packages explicitly. Their
    # reviewed central pins, never ambient framework references, define that envelope.
    extra_file = template / 'package-references.json'
    if extra_file.exists():
        extra = json.loads(extra_file.read_text())
        if not isinstance(extra, list) or len(extra) != len(set(extra)) or any(
                item not in ('Microsoft.Extensions.DependencyInjection', 'Microsoft.Extensions.Hosting') for item in extra):
            raise ValueError('PackageProbe extra references must be unique DI/hosting package IDs.')
        references.update({package: central[package] for package in extra})
        receipt['consumerReferenceTemplateSha256'] = sha256(extra_file)
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    properties = ET.SubElement(project, 'PropertyGroup')
    for key, value in {'OutputType': 'Exe', 'TargetFramework': 'net10.0', 'ImplicitUsings': 'enable',
                       'Nullable': 'enable', 'RestorePackagesWithLockFile': 'true'}.items():
        ET.SubElement(properties, key).text = value
    items = ET.SubElement(project, 'ItemGroup')
    for package, version in sorted(references.items()):
        ET.SubElement(items, 'PackageReference', Include=package, Version='[' + version + ']')
    ET.indent(project)
    project_path = consumer / 'Jobs.PackageProbe.csproj'
    ET.ElementTree(project).write(project_path, encoding='utf-8', xml_declaration=True)
    config = consumer / 'NuGet.Config'
    public_nuget_config(source, config, feed, versions)
    env = environment.copy()
    env['NUGET_PACKAGES'] = str(evidence / 'consumer-packages')
    env['NUGET_HTTP_CACHE_PATH'] = str(evidence / 'consumer-http-cache')
    run(['dotnet', '--version'], cwd=consumer, env=env)
    run(['dotnet', 'restore', project_path, '--configfile', config], cwd=consumer, env=env)
    assets_path = consumer / 'obj/project.assets.json'
    assets = json.loads(assets_path.read_text())
    libraries = assets['libraries']
    if any(item.get('type') != 'package' for item in libraries.values()):
        raise ValueError('Consumer resolved a project or non-package library.')
    require_standalone(key.rsplit('/', 1)[0] for key in libraries)
    expected = {item['id'].casefold(): item for item in receipt['packages']}
    resolved = []
    for key, library in libraries.items():
        package, version = key.rsplit('/', 1)
        record = expected.get(package.casefold())
        if record is None:
            if package.casefold().startswith('elsa.') and not package.casefold().startswith('elsa.specifications.'):
                raise ValueError('Consumer resolved an Elsa package outside the packed closure: ' + key)
            continue
        if version != record['version']:
            raise ValueError('Consumer resolved a different packed package version: ' + key)
        archive = Path(env['NUGET_PACKAGES']) / library['path'] / (package.lower() + '.' + version.lower() + '.nupkg')
        digest = base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode()
        if digest != record['archiveSha512']:
            raise ValueError('Restored archive differs from the packed artifact: ' + key)
        resolved.append({'id': package, 'version': version, 'archiveSha512': digest})
    for package in MODULES:
        if package + '/' + versions[package] not in libraries:
            raise ValueError('Consumer did not resolve selected package root: ' + package)
    run(['dotnet', 'restore', project_path, '--locked-mode', '--configfile', config], cwd=consumer, env=env)
    run(['dotnet', 'publish', project_path, '-c', 'Release', '--no-restore', '--output', deployment],
        cwd=consumer, env=env)
    entry = deployment / 'Jobs.PackageProbe.dll'
    deps_path = entry.with_suffix('.deps.json')
    runtime_path = entry.with_suffix('.runtimeconfig.json')
    if not all(item.is_file() for item in (entry, deps_path, runtime_path)):
        raise ValueError('Publish did not produce the consumer DLL, deps and runtime configuration.')
    deps = json.loads(deps_path.read_text())
    require_standalone(key.rsplit('/', 1)[0] for key in deps['libraries'])
    if any(item.get('type') == 'project' for item in deps['libraries'].values()):
        # The application itself is a project in deps.json; referenced projects are not.
        project_names = [key.rsplit('/', 1)[0] for key, item in deps['libraries'].items()
                         if item.get('type') == 'project']
        if project_names != ['Jobs.PackageProbe']:
            raise ValueError('Published consumer has unexpected project dependencies.')
    target_name = deps['runtimeTarget']['name']
    target = deps['targets'][target_name]
    deployed_packages = []
    for package in MODULES:
        record = expected[package.casefold()]
        key = package + '/' + record['version']
        if deps['libraries'].get(key, {}).get('type') != 'package' or key not in target:
            raise ValueError('Published consumer lost its exact package root: ' + key)
        runtime_assets = target[key].get('runtime', {})
        dll_assets = [name for name in runtime_assets if name.endswith('.dll')]
        if not dll_assets:
            raise ValueError('Selected Jobs package has no published runtime assembly: ' + key)
        with zipfile.ZipFile(record['path']) as archive:
            for asset in dll_assets:
                deployed = deployment / Path(asset).name
                if not deployed.is_file() or hashlib.sha256(archive.read(asset)).hexdigest() != sha256(deployed):
                    raise ValueError('Deployed Jobs assembly differs from its packed asset: ' + key + '/' + asset)
                deployed_packages.append({'id': package, 'version': record['version'], 'asset': asset,
                                           'sha256': sha256(deployed)})
    runtime = json.loads(runtime_path.read_text())['runtimeOptions']
    frameworks = runtime.get('frameworks', [runtime['framework']] if 'framework' in runtime else [])
    if any(item['name'] != 'Microsoft.NETCore.App' for item in frameworks):
        raise ValueError('Standalone consumer unexpectedly requires an additional shared framework.')
    receipt['consumer'] = {'packageRoots': list(MODULES), 'provider': provider,
                           'references': references, 'restoredProjectLibraries': 0,
                           'resolvedExternalPackages': sorted(
                               ({'id': key.rsplit('/', 1)[0], 'version': key.rsplit('/', 1)[1],
                                 'sha512': library.get('sha512')}
                                for key, library in libraries.items()
                                if key.rsplit('/', 1)[0].casefold() not in expected),
                               key=lambda item: item['id']),
                           'restoreDiagnostics': [{key: item[key] for key in ('code', 'level', 'libraryId', 'message')
                                                   if key in item} for item in assets.get('logs', [])],
                           'resolvedPackedPackages': sorted(resolved, key=lambda item: item['id']),
                           'deployedJobsAssets': deployed_packages,
                           'assetsSha256': sha256(assets_path),
                           'lockSha256': sha256(consumer / 'packages.lock.json'),
                           'nugetConfigSha256': sha256(config),
                           'deployment': str(deployment), 'entryAssembly': str(entry),
                           'deploymentFilesSha256': {str(item.relative_to(deployment)): sha256(item)
                                                     for item in sorted(deployment.rglob('*')) if item.is_file()}}
    receipt['consumer']['providerRuntimeAssetsSha256'] = {
        name: digest for name, digest in receipt['consumer']['deploymentFilesSha256'].items()
        if any(part in name.casefold() for part in ('sqlite', 'npgsql'))}
    receipt['consumerPublishState'] = 'PASSED'


def execute_consumer(receipt, evidence, source, run, environment, provider, witness_group):
    controller = source / 'tools/jobs/PackageProbe/run_witness.py'
    if not controller.is_file():
        raise ValueError('The immutable source has no committed standalone witness controller.')
    witness_directory = evidence / 'standalone-witness'
    receipt['consumerControllerSha256'] = sha256(controller)
    receipt['consumerControllerFilesSha256'] = {
        str(path.relative_to(source)): sha256(path) for path in sorted(controller.parent.glob('*.py'))}
    receipt['witnessGroup'] = witness_group
    receipt['consumerExecutionState'] = 'IN PROGRESS'
    witness_path = witness_directory / 'witness-receipt.json'

    def capture():
        witness = json.loads(witness_path.read_text())
        receipt['standaloneWitnessReceipt'] = str(witness_path)
        receipt['standaloneWitnessReceiptSha256'] = sha256(witness_path)
        receipt['standaloneCases'] = witness['cases']
        receipt['executedProviderVersion'] = witness.get('providerVersion')
        return witness

    try:
        run([sys.executable, controller, '--entry', receipt['consumer']['entryAssembly'],
             '--evidence-dir', witness_directory, '--provider', provider, '--group', witness_group],
            cwd=Path(receipt['consumer']['deployment']), env=environment)
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError):
        receipt['consumerExecutionState'] = 'FAILED'
        receipt['standaloneSliceState'] = 'FAILED'
        receipt['standaloneAcceptanceState'] = 'INCOMPLETE'
        if witness_path.is_file():
            capture()
        raise
    witness = capture()
    if witness['state'] != 'PASSED' or not witness['ownedResourcesCleaned']:
        raise ValueError('Standalone witness failed or left owned resources uncleaned.')
    receipt['consumerExecutionState'] = 'PASSED'
    receipt['standaloneSliceState'] = 'PASSED'
    receipt['standaloneAcceptanceState'] = 'INCOMPLETE'
    receipt['proofScope'] = ('Immutable packages, clean external publish and focused independent-process standalone '
                             'slice. Full retry/schedule/security/model/UI qualification remains separate.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    if not __debug__:
        parser.error('Python optimization disables witness assertions. Run without -O/PYTHONOPTIMIZE.')
    parser.add_argument('--source-ref', required=True, type=immutable_commit)
    parser.add_argument('--publish-state-ref', required=True, type=immutable_commit)
    parser.add_argument('--evidence-dir', required=True, type=Path)
    parser.add_argument('--stage', choices=('pack', 'publish', 'execute'), default='pack')
    parser.add_argument('--provider', choices=tuple(PROVIDERS), default='sqlite')
    parser.add_argument('--witness-group', choices=('m1', 'schedules', 'coordination', 'attempts', 'chaos', 'all'), default='m1')
    parser.add_argument('--command-timeout-seconds', type=int, default=3600,
                        help='Includes build-slot queue time; no queue bypass is used.')
    args = parser.parse_args()
    if args.command_timeout_seconds <= 0:
        parser.error('Command timeout must be positive.')
    base = args.evidence_dir.expanduser().resolve()
    if base.is_relative_to(ROOT) or ROOT.is_relative_to(base):
        parser.error('Use a dedicated evidence directory outside the source checkout.')
    evidence = base / ('run-' + uuid.uuid4().hex)
    evidence.mkdir(parents=True)
    source, feed = evidence / 'source', evidence / 'feed'
    feed.mkdir()
    harness = Path(__file__).resolve()
    receipt = {'schemaVersion': 1, 'sourceCommit': args.source_ref,
               'publishStateCommit': args.publish_state_ref, 'stage': args.stage,
               'packState': 'NOT RUN', 'consumerPublishState': 'NOT RUN',
               'consumerExecutionState': 'NOT RUN', 'standaloneAcceptanceState': 'NOT RUN',
               'fullAcceptanceState': 'NOT RUN', 'commands': [], 'harnessSha256': sha256(harness),
               'proofScope': 'Package construction and optional clean consumer publish only; no job execution proof.'}
    environment = clean_environment()
    environment['NUGET_PACKAGES'] = str(evidence / 'source-packages')
    environment['NUGET_HTTP_CACHE_PATH'] = str(evidence / 'source-http-cache')
    print('Jobs package evidence: ' + str(evidence), flush=True)

    def run(command, cwd=source, env=environment):
        log = evidence / f'{len(receipt["commands"]) + 1:02d}-command.log'
        entry = {'command': list(map(str, command)), 'cwd': str(cwd), 'log': str(log)}
        receipt['commands'].append(entry)
        print(f'[{len(receipt["commands"])}] ' + ' '.join(map(str, command[:5])), flush=True)
        try:
            with log.open('w', encoding='utf-8') as output:
                # Each command owns a new process group. On timeout, clean its child
                # build/host processes too, without touching an ambient process.
                process = subprocess.Popen(list(map(str, command)), cwd=cwd, env=env,
                                           stdin=subprocess.DEVNULL, stdout=output,
                                           stderr=subprocess.STDOUT, start_new_session=True)
                try:
                    code = process.wait(timeout=args.command_timeout_seconds)
                except subprocess.TimeoutExpired:
                    try:
                        os.killpg(process.pid, signal.SIGTERM)
                    except ProcessLookupError:
                        pass
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        try:
                            os.killpg(process.pid, signal.SIGKILL)
                        except ProcessLookupError:
                            pass
                        process.wait(timeout=10)
                    raise
            entry['exitCode'] = code
            if code:
                raise RuntimeError('Command failed; inspect ' + str(log))
        except subprocess.TimeoutExpired:
            entry['timedOutSeconds'] = args.command_timeout_seconds
            raise
        finally:
            entry['logSha256'] = sha256(log)
            write_json(evidence / 'receipt.json', receipt)

    exit_code = 0
    try:
        run(['git', 'clone', '--shared', '--no-checkout', ROOT, source], cwd=evidence)
        run(['git', 'checkout', '--detach', args.source_ref])
        if git('rev-parse', 'HEAD', cwd=source) != args.source_ref or git('status', '--porcelain', cwd=source):
            raise ValueError('Source clone is not the clean immutable commit.')
        frozen_harness = source / harness.relative_to(ROOT)
        if not frozen_harness.is_file() or sha256(frozen_harness) != receipt['harnessSha256']:
            raise ValueError('Executed packaging driver must match its committed source input.')
        if args.stage in ('publish', 'execute') and not (source / 'tools/jobs/PackageProbe/Program.cs').is_file():
            raise ValueError('No real PackageProbe is committed at the source SHA; publish remains NOT RUN.')
        props = evidence / 'package-versions.props'
        run(['dotnet', 'run', '--project', 'tools/versioning/Elsa.Versioning.Calculator', '--',
             '--repo', source, '--commit', args.source_ref, '--record-ref', args.publish_state_ref,
             '--output', evidence / 'computation.json', '--pack-properties', props,
             '--branch', 'agent/codex/3586-background-jobs'])
        computation = json.loads((evidence / 'computation.json').read_text())
        if computation['commit'] != args.source_ref:
            raise ValueError('Version calculator returned another source commit.')
        receipt['lastPublishCommit'] = computation['last_publish_commit']
        run(['dotnet', 'run', '--project', 'tools/maps/Elsa.Maps.Generator', '--', 'dependency-map'])
        closure = closure_for(json.loads((source / 'docs/maps/dependency-map.json').read_text()))
        versions = computed_versions(props)
        packages = [node for node in closure if node['packable']]
        receipt['closure'] = [{'id': node['package_id'], 'path': node['path'],
                               'version': versions[Path(node['path']).stem]} for node in packages]
        filter_path = evidence / 'jobs-pack.slnf'
        write_json(filter_path, {'solution': {'path': str(source / 'Elsa.Server.slnx'),
                                            'projects': [node['path'] for node in closure]}})
        property_arg = '-p:CustomBeforeDirectoryBuildProps=' + str(props)
        run(['dotnet', 'restore', filter_path, '--locked-mode', property_arg])
        run(['dotnet', 'build', filter_path, '-c', 'Release', '--no-restore', property_arg])
        run(['dotnet', 'pack', filter_path, '-c', 'Release', '--no-build', '--no-restore',
             property_arg, '--output', feed])
        records = [package_metadata(path) for path in sorted(feed.glob('*.nupkg'))]
        expected = {item['id']: item['version'] for item in receipt['closure']}
        if len(records) != len(expected) or {item['id']: item['version'] for item in records} != expected:
            raise ValueError('Packed archives do not match the computed complete closure.')
        computed = {item['package_id']: item for item in computation['packages']}
        for record in records:
            if record['sourceCommit'] != args.source_ref or record['fingerprint'] != computed[record['id']]['fingerprint']:
                raise ValueError('Packed source commit/input fingerprint mismatch: ' + record['id'])
        receipt['packages'] = records
        receipt['packState'] = 'PASSED'
        if args.stage in ('publish', 'execute'):
            publish_consumer(receipt, evidence, source, feed, run, environment, args.provider)
        if args.stage == 'execute':
            execute_consumer(receipt, evidence, source, run, environment, args.provider, args.witness_group)
        if git('status', '--porcelain', cwd=source):
            raise ValueError('Immutable source changed during package verification.')
        receipt['sourceCleanAfterVerification'] = True
    except (OSError, ValueError, KeyError, TypeError, RuntimeError, subprocess.SubprocessError,
            ET.ParseError, zipfile.BadZipFile) as error:
        receipt['error'] = str(error) or type(error).__name__
        print(receipt['error'], file=sys.stderr)
        exit_code = 1
    finally:
        receipt['harnessUnchanged'] = harness.is_file() and sha256(harness) == receipt['harnessSha256']
        if not receipt['harnessUnchanged']:
            receipt['error'] = 'Packaging driver changed during execution.'
            exit_code = 1
        receipt['verificationState'] = 'PASSED' if exit_code == 0 else 'FAILED'
        write_json(evidence / 'receipt.json', receipt)
    return exit_code


if __name__ == '__main__':
    sys.exit(main())

#!/usr/bin/env python3
"""Fixed local demo preparation; operational process ownership belongs to the cockpit."""
import argparse, base64, hashlib, json, os, re, shutil, socket, subprocess, time
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
DEMO = ROOT / 'artifacts/demo/renewals'
SOURCE_HOST = ROOT / 'src/apps/Elsa.Foundation.Host/bin/Release/net10.0'
COMPOSITION = ROOT / 'tools/demo/renewals/Composition/Composition.csproj'
CLI = ROOT / 'src/essentials/Cli/bin/Release/net10.0/Elsa.Cli.dll'
STUDIO = Path(os.environ.get('DEMO_STUDIO_DIR', '/Users/sipke/Projects/Elsa/elsa-foundation-studio'))
DB = DEMO / 'renewals.db'
CONNECTION = 'Data Source=' + str(DB) + ';Pooling=False;Default Timeout=30'
HOSTS = {'a': 5311, 'b': 5312}
MODULES = [('Elsa.Samples.Nuplane.Renewals', ROOT / 'samples/Elsa.Samples.Nuplane.Renewals/Elsa.Samples.Nuplane.Renewals.csproj'), ('Elsa.Samples.Nuplane.Renewals.Activities', ROOT / 'samples/Elsa.Samples.Nuplane.Renewals.Activities/Elsa.Samples.Nuplane.Renewals.Activities.csproj')]


def run(args, **kwargs):
    print('$ ' + ' '.join(str(a) for a in args), flush=True)
    subprocess.run([str(a) for a in args], check=True, **{'cwd': ROOT, **kwargs})


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + '\n')


def signing_key():
    key = DEMO / 'signing-key.pk8'
    if not key.exists():
        pem = subprocess.check_output(['openssl', 'genpkey', '-algorithm', 'RSA', '-pkeyopt', 'rsa_keygen_bits:2048'], stderr=subprocess.DEVNULL)
        der = subprocess.check_output(['openssl', 'pkcs8', '-topk8', '-nocrypt', '-outform', 'DER'], input=pem)
        key.write_bytes(der)
        key.chmod(0o600)
    return base64.b64encode(key.read_bytes()).decode('ascii')


def require_ports_free(ports):
    for port in ports:
        try:
            with socket.socket() as listener:
                listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                listener.bind(('127.0.0.1', port))
                listener.listen(1)
        except OSError as error:
            raise RuntimeError(f'Demo port {port} is occupied; preparation/reset refuses to touch active state.') from error


def require_stopped():
    require_ports_free([*HOSTS.values(), 5313])


def bootstrap_host(name, initialize=False):
    """Acquire the platform before the domain, so their non-collectible generations have separate lifetimes."""
    require_ports_free([HOSTS[name]])
    host = DEMO / 'hosts' / name
    pid_file = host / 'cockpit.pid'
    if pid_file.exists():
        try:
            pid = int(pid_file.read_text().strip())
            if pid <= 0: raise ValueError('Invalid PID')
            os.kill(pid, 0)
        except ProcessLookupError:
            pass
        except ValueError as error:
            raise RuntimeError('Invalid ownership PID; refusing to rewrite host bootstrap state.') from error
        else:
            raise RuntimeError(f'Host {name} PID {pid} is still running; stop it through its owner before bootstrap.')
    ready = host / 'ready-shells.json'
    marker = host / 'bootstrap-pending'
    if initialize:
        write_json(ready, json.loads((host / 'shells.json').read_text()))
        ready.chmod(0o600)
    elif not marker.exists():
        raise RuntimeError('This host has no pending baseline bootstrap; refusing to rewrite an existing demonstration.')
    shells = json.loads(ready.read_text())
    features = shells['CShells']['Shells']['default']['Features']
    for feature in ['Renewals', 'RenewalsEntityFrameworkCore', 'RenewalsActivities', 'FoundationDemoDesignerActivities']:
        features.pop(feature, None)
    for filename in ['bootstrap-shells.json', 'shells.json']:
        destination = host / filename
        write_json(destination, shells)
        destination.chmod(0o600)
    for package in (host / 'feed').glob('*.nupkg'):
        if any(package.name.startswith(module + '.') for module, _ in MODULES):
            package.unlink()
    # The CLI worker that applied the baseline has exited; none of its assembly loads
    # belong to the host. Its restored store must not preload the domain at first boot.
    store = host / '.nuplane'
    if store.exists(): shutil.rmtree(store)
    marker.write_text('Platform first, then acquire renewal release 1.0.0 through Nuplane.\n')
    print(f'Host {name} staged for platform-first baseline acquisition; its database is preserved.', flush=True)


def pack_graph():
    """Use NuGet's restored project graph, not a guessed source reference walk."""
    assets = json.loads((COMPOSITION.parent / 'obj/project.assets.json').read_text())
    common, closure = DEMO / 'common', DEMO / 'closure'
    for directory in [common, closure]:
        if directory.exists(): shutil.rmtree(directory)
    common.mkdir(parents=True, exist_ok=True); closure.mkdir(parents=True, exist_ok=True)
    projects = []
    for lib in assets['libraries'].values():
        if lib['type'] == 'project':
            project = (COMPOSITION.parent / lib['msbuildProject']).resolve()
            # Shared contracts still need resolvable NuGet packages in a source/dev feed. Host sharing
            # determines assembly binding independently; a package copy never replaces that host assembly.
            projects.append(project)
        elif lib['type'] == 'package':
            sha = next((f for f in lib['files'] if f.endswith('.nupkg.sha512')), None)
            if not sha: continue
            relative = Path(lib['path']) / sha.removesuffix('.sha512')
            package = next((Path(folder) / relative for folder in assets['packageFolders'] if (Path(folder) / relative).is_file()), None)
            if package is None: raise RuntimeError('Missing restored package: ' + str(relative))
            shutil.copy2(package, closure / package.name)
    # One MSBuild invocation packs the actual restored closure, using the already built assemblies.
    project = ET.Element('Project'); items = ET.SubElement(project, 'ItemGroup')
    for p in projects: ET.SubElement(items, 'DemoProject', Include=str(p))
    target = ET.SubElement(project, 'Target', Name='PackDemoClosure')
    ET.SubElement(target, 'MSBuild', Projects='@(DemoProject)', Targets='Pack', BuildInParallel='false', Properties=f'Configuration=Release;NoBuild=true;PackageOutputPath={common};RestoreLockedMode=false')
    batch = DEMO / 'pack-closure.proj'; ET.ElementTree(project).write(batch, encoding='unicode')
    run(['dotnet', 'msbuild', batch, '-t:PackDemoClosure', '-nologo', '-v:minimal'])
    print(f'Prepared {len(projects)} source projects and {len(list(closure.glob("*.nupkg")))} external packages', flush=True)


def build_pack():
    require_stopped()
    DEMO.mkdir(parents=True, exist_ok=True)
    (DEMO / 'prepared.json').unlink(missing_ok=True)
    run(['dotnet', 'build', ROOT / 'src/apps/Elsa.Foundation.Host/Elsa.Foundation.Host.csproj', '-c', 'Release', '-nologo', '-v', 'minimal'])
    run(['dotnet', 'build', COMPOSITION, '-c', 'Release', '-nologo', '-v', 'minimal', '-m:1', '-p:RestoreDisableParallel=true'])
    run(['dotnet', 'build', ROOT / 'src/essentials/Cli/Elsa.Cli.csproj', '-c', 'Release', '-nologo', '-v', 'minimal'])
    pack_graph()
    # Identity is packed once as part of the restored composition graph above.
    pack_releases()
    run(['pnpm', 'install', '--frozen-lockfile'], cwd=STUDIO)
    run(['pnpm', '--workspace-concurrency=2', '-r', 'build'], cwd=STUDIO)
    run(['dotnet', 'build', STUDIO / 'src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj', '-c', 'Release', '-nologo', '-v', 'minimal'])
    manifests = {'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(), 'sourceDirty': bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()), 'preparedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), 'hostSha256': hashlib.sha256((SOURCE_HOST / 'Elsa.Foundation.Host.dll').read_bytes()).hexdigest(), 'sourcePackageCount': len(list((DEMO / 'common').glob('*.nupkg'))), 'externalPackageCount': len(list((DEMO / 'closure').glob('*.nupkg')))}
    write_json(DEMO / 'prepared.json', manifests)


def pack_releases():
    DEMO.mkdir(parents=True, exist_ok=True)
    batch = ET.Element('Project')
    target = ET.SubElement(batch, 'Target', Name='PackRenewalReleases')
    for release in [1, 2]:
        stage = DEMO / 'staging' / str(release)
        if stage.exists(): shutil.rmtree(stage)
        stage.mkdir(parents=True)
        for _, project in MODULES:
            properties = f'DemoVersion={release};Configuration=Release;PackageOutputPath={stage};BuildProjectReferences=false;RestoreLockedMode=false'
            # A fresh project evaluation must import the files Restore just generated. Distinct
            # global phase values prevent MSBuild from reusing its pre-restore project instance.
            ET.SubElement(target, 'MSBuild', Projects=str(project), Targets='Restore', BuildInParallel='false', Properties=properties + ';DemoPreparationPhase=Restore')
            ET.SubElement(target, 'MSBuild', Projects=str(project), Targets='Pack', BuildInParallel='false', Properties=properties + ';DemoPreparationPhase=Pack')
    path = DEMO / 'pack-renewals.proj'
    ET.ElementTree(batch).write(path, encoding='unicode')
    run(['dotnet', 'msbuild', path, '-t:PackRenewalReleases', '-nologo', '-v:minimal', '-m:1'])


def prepare_host(name):
    if not (SOURCE_HOST / 'Elsa.Foundation.Host.dll').is_file(): raise RuntimeError('Build Foundation.Host first.')
    host = DEMO / 'hosts' / name
    host.mkdir(parents=True, exist_ok=True)
    # Preflight only; callers must refuse this while any owned or unowned host uses this content root.
    for source in SOURCE_HOST.iterdir():
        target = host / source.name
        if source.is_dir(): shutil.copytree(source, target, dirs_exist_ok=True)
        else: shutil.copy2(source, target)
    feed = host / 'feed'; feed.mkdir(exist_ok=True)
    common = list((DEMO / 'common').glob('*.nupkg'))
    if not common: raise RuntimeError('The feature package closure has not been prepared.')
    for package in common: shutil.copy2(package, feed / package.name)
    publish(name, 1)
    shells = json.loads((ROOT / 'tools/demo/renewals/shells.template.json').read_text())
    (DEMO / 'locks').mkdir(exist_ok=True)
    shells['CShells']['Shells']['default']['Features']['FileSystemDistributedLocking']['LocksFolderPath'] = str(DEMO / 'locks')
    identity = shells['CShells']['Shells']['default']['Features']['FoundationDemoIdentity']
    identity['IdentityConnectionString'] = CONNECTION
    identity['OpenIddictConnectionString'] = 'Data Source=' + str(DEMO / 'tokens.db')
    identity['SigningKey'] = signing_key()
    write_json(host / 'shells.json', shells)
    (host / 'shells.json').chmod(0o600)
    # These demo packages use the exact source/dev versions in this host's deps.json.
    # Declare only the contracts this binary actually shares, so package acquisition
    # cannot introduce a second EfModuleAttribute identity into the CLI worker.
    settings = json.loads(re.sub(r'^\s*//.*$', '', (SOURCE_HOST / 'appsettings.json').read_text(), flags=re.MULTILINE))
    provided = settings['Nuplane']['HostProvidedPackages'][:]
    libraries = json.loads((SOURCE_HOST / 'Elsa.Foundation.Host.deps.json').read_text())['libraries']
    package_ids = {key.split('/')[0] for key in libraries}
    for shared in settings['Nuplane']['Loading']['SharedAssemblies']:
        shared_id = shared['Name']
        if shared_id not in package_ids or not (SOURCE_HOST / (shared_id + '.dll')).is_file():
            raise RuntimeError('Host cannot provide its declared shared contract: ' + shared_id)
        if shared_id not in provided: provided.append(shared_id)
    write_json(host / 'appsettings.Development.json', {
        'Nuplane': {'HostProvidedPackages': provided, 'Setup': {'Feeds': [{'Name': 'renewal-demo', 'DirectoryPath': str(feed), 'IncludePatterns': ['*'], 'Directory': {'Watch': True, 'DebounceWindow': '00:00:01'}}, {'Name': 'closure', 'DirectoryPath': str(DEMO / 'closure')}], 'PollInterval': '00:00:05'}, 'Capabilities': {'ef-provider': 'Sqlite'}},
        'Elsa': {'Shells': {'ReloadOnPackageChange': False}, 'ModuleManagement': {'Enabled': True}, 'DataProtection': {'ApplicationName': 'Toolbox.Renewals.Demo', 'EntityFrameworkCore': {'Enabled': True, 'Provider': 'Sqlite'}}, 'Cluster': {'Membership': {'HostId': 'toolbox-renewals-' + name, 'EntityFrameworkCore': {'Enabled': True, 'Provider': 'Sqlite'}, 'HeartbeatInterval': '00:00:02', 'ExpiryPeriod': '00:00:10', 'SkewAllowance': '00:00:02'}}, 'Persistence': {'EntityFramework': {'Migrate': {'Policy': 'Validate'}, 'Finalization': {'EvaluationInterval': '00:00:02', 'RefreshInterval': '00:00:02'}}}},
        # Keep the live story legible without hiding activation or migration errors.
        # Reconciliation state and CLI results are also captured by the cockpit.
        'Logging': {'LogLevel': {
            'Default': 'Warning',
            'Microsoft.Hosting.Lifetime': 'Information',
            'CShells.Lifecycle': 'Information',
            'Elsa.Foundation.Host': 'Information',
            'Elsa.Persistence.Schema': 'Information',
            'Elsa.Samples.Nuplane': 'Information',
            'Elsa.Activities.Design.Reconciliation.Clr.Services.ClrAssemblyScanner': 'Error'
        }}
    })
    print('Prepared Foundation.Host ' + name + ' on port ' + str(HOSTS[name]), flush=True)


def publish(name, release):
    stage = DEMO / 'staging' / str(release); feed = DEMO / 'hosts' / name / 'feed'
    feed.mkdir(parents=True, exist_ok=True)
    packages = [p for p in stage.glob('*.nupkg') if any(p.name.startswith(n + '.') for n, _ in MODULES)]
    if len(packages) != 2: raise RuntimeError('Both staged renewal packages are required; prepare packages first.')
    for p in packages:
        temp = feed / (p.name + '.tmp'); shutil.copy2(p, temp); temp.replace(feed / p.name)
        print('Published ' + p.name + ' to host ' + name, flush=True)


def persistence(name, operation, restore=False, selection='--all'):
    env = dict(os.environ, ELSA_EF_CONNECTION=CONNECTION, ConnectionStrings__Elsa=CONNECTION, Elsa__Cluster__Membership__EntityFrameworkCore__ConnectionString=CONNECTION)
    args = ['dotnet', CLI, 'persistence', operation, '--host', DEMO / 'hosts' / name, '--environment', 'Development', selection]
    if operation != 'list': args += ['--provider', 'Sqlite']
    if operation == 'status': args += ['--family', 'SamplesRenewals', '--skew-allowance', '00:00:02']
    if restore: args.append('--restore')
    run(args, env=env)


def reset_data():
    # Caller verifies no host owns these files. Rename rather than erase so recovery stays possible.
    require_stopped()
    if not DEMO.exists(): return
    archive = ROOT / 'artifacts/demo' / ('renewals-reset-' + str(time.time_ns()))
    archive.mkdir()
    for part in ['hosts', 'studio', 'locks', 'renewals.db', 'renewals.db-shm', 'renewals.db-wal', 'tokens.db', 'tokens.db-shm', 'tokens.db-wal']:
        source = DEMO / part
        if source.exists(): source.replace(archive / part)
    print('Previous isolated demo state archived to ' + str(archive), flush=True)


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('action', choices=['build-pack', 'prepare', 'bootstrap', 'publish', 'migrate', 'status', 'reset']); parser.add_argument('--host', choices=HOSTS, default='a'); parser.add_argument('--release', choices=[1, 2], type=int, default=2)
    args = parser.parse_args()
    if args.action == 'build-pack': build_pack()
    elif args.action == 'prepare':
        require_stopped()
        for name in HOSTS: prepare_host(name)
        # --all alone can legitimately discover only the host's storage modules.
        # Validate the enabled shell's declarations before applying anything.
        persistence('a', 'list', restore=True, selection='--from-host')
        persistence('a', 'apply')
        for name in HOSTS: bootstrap_host(name, initialize=True)
    elif args.action == 'bootstrap': bootstrap_host(args.host)
    elif args.action == 'publish': publish(args.host, args.release)
    elif args.action == 'migrate': persistence(args.host, 'apply')
    elif args.action == 'status': persistence(args.host, 'status')
    elif args.action == 'reset': reset_data()

if __name__ == '__main__': main()

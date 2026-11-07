"""Reuse frozen portable proof/fixture controllers; never synthesize engine results."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys

PROVIDER_PROJECT = ('Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/'
                    'Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj')
CRASH_CASES = ('crash-before-commit', 'crash-after-commit', 'crash-after-body')
COLD_CASES = ('sequence', 'suspension', 'late-output', 'waited-child', 'protected')
SQLITE_CASES = COLD_CASES + CRASH_CASES + ('compensation-control', 'compensation-kill')
FLAGS = ['-m:1', '-nr:false', '-p:UseSharedCompilation=false']


def dotnet():
    value = shutil.which('dotnet')
    if not value:
        raise ValueError('The configured .NET executable is unavailable')
    return str(Path(value).resolve())


def build_provider(runner):
    from runner import commit, git
    item = runner.state['variants']['snapshot']
    source = runner.refresh_source('snapshot')
    lock = str(Path(PROVIDER_PROJECT).parent / 'packages.lock.json')
    if not (source / lock).exists():
        runner.phase('snapshot', 'provider-bootstrap', [
            {'name': 'restore-create-provider-lock', 'argv': [dotnet(), 'restore', PROVIDER_PROJECT,
                '--use-lock-file', '--disable-parallel'] + FLAGS, 'timeoutSeconds': 540}], generated=[lock])
        commit(source, 'Commit provider qualification dependency lock before qualified builds')
        runner.refresh_source('snapshot')
    binary_dir = source / Path(PROVIDER_PROJECT).parent / 'bin/Release/net10.0'
    receipt = runner.phase('snapshot', 'provider-build-' + item['commit'][:12], [
        {'name': 'locked-provider-restore', 'argv': [dotnet(), 'restore', PROVIDER_PROJECT,
            '--locked-mode', '--disable-parallel'] + FLAGS, 'timeoutSeconds': 120},
        {'name': 'build-provider-controls', 'argv': [dotnet(), 'build', PROVIDER_PROJECT, '-c', 'Release',
            '--no-restore', '--no-incremental'] + FLAGS, 'timeoutSeconds': 420,
         'captureArtifactDirectories': [str(binary_dir)]}])
    item['providerBuild'] = {'status': 'PASSED', 'buildReceipt': receipt,
        'binary': str(binary_dir / (Path(PROVIDER_PROJECT).stem + '.dll')),
        'sourceCommit': item['commit'], 'sourceTree': item['tree']}
    runner.save()


def selected_binary(runner, variant, provider=False):
    item = runner.state['variants'][variant]
    source = runner.refresh_source(variant)
    selected = item.get('providerBuild', {}) if provider else item
    status = selected.get('status') if provider else selected.get('buildStatus')
    if status != 'PASSED' or not selected.get('buildReceipt') or not selected.get('binary'):
        raise ValueError('Successful current exact-source build is required: ' + variant)
    manifest = json.loads(Path(selected['buildReceipt']['manifestPath']).read_bytes())
    if (manifest['head'], manifest['tree']) != (item['commit'], item['tree']):
        raise ValueError('Built binaries precede a source change: ' + variant)
    return {'worktree': str(source), 'binary': selected['binary'], 'buildReceipt': selected['buildReceipt']}


def collect_private(runner, private, label):
    """No connection/env files or protected exceptions enter the uploaded artifact tree."""
    from runner import sha, write
    destination = runner.evidence / 'controller-phases' / label
    destination.mkdir(parents=True, exist_ok=False)
    inventory, excluded = [], []
    for path in sorted(private.rglob('*')):
        if path.is_symlink():
            raise ValueError('An owned output became a symlink')
        if not path.is_file():
            continue
        relative = path.relative_to(private)
        if (path.name in ('postgres.env', 'snapshot-admin.connection') or path.name.endswith('.failure.log')
                or path.suffix in ('.sqlite', '.db') or path.name.endswith(('-wal', '-shm'))):
            excluded.append(str(relative))
            continue
        content = path.read_bytes()
        # Fail closed rather than publish any unexpected raw provider connection details.
        if path.suffix in ('.log', '.trx', '.json') and re.search(rb'(?i)(?:password|pwd)\s*=\s*[^;\r\n\s][^;\r\n]*', content):
            excluded.append(str(relative))
            continue
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(content)
        inventory.append({'path': str(relative), 'bytes': len(content), 'sha256': sha(content)})
    write(destination / 'export.json', {'schema': 'runtime-prototype-private-export-v1',
        'originalRoot': str(private), 'files': inventory, 'excludedPrivateFiles': excluded,
        'referencePathsPreserved': True, 'secretFilesNeverExported': True})
    return str(destination)


def fixture_request(runner, label, output, variants):
    from runner import HERE
    pin = runner.state.get('postgres')
    if not pin:
        raise ValueError('Prepare the exact hosted image before PostgreSQL stages')
    return {'name': label, 'output': str(output), 'dotnetExecutable': dotnet(), 'runtimeExecutable': dotnet(),
        'dockerExecutable': str(Path(shutil.which('docker')).resolve()), 'dockerContext': pin['dockerContext'],
        'expectedImageId': pin['expectedImageId'], 'expectedDaemonId': pin['expectedDaemonId'],
        'cpus': pin['cpus'], 'memoryMiB': pin['memoryMiB'],
        'helpers': {name: str(HERE / 'helpers' / name) for name in ('owned_resources.py', 'process_scope.py')},
        'variants': variants}


def execute_packet(runner, private, script, generator_argv, label, postgres):
    from runner import command, sha
    private.parent.mkdir(parents=True, exist_ok=True)
    # SQLite generator creates private itself; PG request writer creates it first.
    log = runner.evidence / (label + '-freeze.log')
    row = {'status': 'PREPARING', 'provider': 'PostgreSql' if postgres else 'Sqlite'}
    runner.state.setdefault('controllers', {})[label] = row
    runner.save()
    try:
        command(generator_argv, log)
        packet = private / 'packet'
        manifest_path = packet / 'manifest.json'
        manifest_sha = sha(manifest_path.read_bytes())
        row.update(status='RUNNING', manifestPath=str(manifest_path), manifestSha256=manifest_sha)
        runner.save()
        code = command([sys.executable, str(packet / script), str(packet), '--run', '--manifest-sha256', manifest_sha],
                       runner.evidence / (label + '-controller.log'), check=False)
        phase_path = private / 'output/phase.json'
        actual = json.loads(phase_path.read_bytes()) if phase_path.is_file() else {}
        row['phasePath'] = str(phase_path)
        row['phaseSha256'] = sha(phase_path.read_bytes()) if phase_path.is_file() else None
        if (code != 0 or actual.get('status') != 'COMMANDS_PASSED' or actual.get('processQuiescent') is not True or
                (postgres and (actual.get('secretRemoved') is not True or actual.get('containerRemoved') is not True))):
            raise RuntimeError('Actual proof/measurement or owned closeout did not pass: ' + label)
        row['status'] = 'PASSED'
        # Descriptive observations await independent offline qualification; this is not a review stamp.
        return row
    except BaseException as error:
        row.update(status='FAILED', errorType=type(error).__name__)
        raise
    finally:
        try:
            row['exportPath'] = collect_private(runner, private, label) if private.exists() else None
        except BaseException as error:
            row.update(status='FAILED', exportFailureType=type(error).__name__)
            raise
        finally:
            runner.save()


def proof(runner, variant, provider, case, mode, leaves):
    from runner import command, write, HERE
    qualification = case == 'snapshot-provider-qualification'
    if qualification and (variant != 'snapshot' or provider != 'PostgreSql'):
        raise ValueError('Provider controls are PostgreSQL snapshot only')
    if case not in (COLD_CASES + CRASH_CASES + ('snapshot-provider-qualification',) if provider == 'PostgreSql' else SQLITE_CASES):
        raise ValueError('Case is outside the provider proof scope')
    selected = selected_binary(runner, variant, provider=qualification)
    label = '-'.join(('proof', provider.lower(), variant, mode.lower(), str(leaves), case))
    private = runner.root / 'private' / label
    if private.exists():
        raise ValueError('Proof phase is create-only')
    source = Path(selected['worktree'])
    scripts = source / 'tools/experiments/runtime-architecture'
    if provider == 'Sqlite':
        argv = [sys.executable, str(scripts / 'generate_sqlite_proof_packet.py'),
            '--worktree', str(source), '--probe-binary', selected['binary'],
            '--build-manifest', selected['buildReceipt']['manifestPath'], '--build-phase', selected['buildReceipt']['phasePath'],
            '--owned-root', str(private), '--runtime-host', dotnet(), '--build-tool', dotnet(),
            '--owned-resources', str(HERE / 'helpers/owned_resources.py'), '--process-scope', str(HERE / 'helpers/process_scope.py'),
            '--variant', variant, '--case', case, '--mode', mode, '--leaves', str(leaves)]
        return execute_packet(runner, private, 'launch_phase.py', argv, label, False)
    private.mkdir(parents=True, mode=0o700)
    request = fixture_request(runner, label, private / 'output', {variant: selected})
    request.update(variant=variant, case={'kind': case} if qualification else {'kind': case, 'mode': mode, 'leaves': leaves, 'cap': 50})
    write(private / 'request.json', request)
    return execute_packet(runner, private, 'postgres_phase.py',
        [sys.executable, str(scripts / 'freeze_postgres_proof_phase.py'), str(private / 'request.json'), str(private / 'packet')], label, True)


def require_measurement_gates(runner, mode):
    from runner import VARIANTS
    for variant in ('runview', 'snapshot'):
        item = runner.state['variants'][variant]
        record = item.get('focusedTests', {})
        if (record.get('status') != 'PASSED' or
                (record.get('sourceCommit'), record.get('sourceTree')) != (item['commit'], item['tree']) or
                hashlib.sha256(Path(record['receiptPath']).read_bytes()).hexdigest() != record.get('receiptSha256')):
            raise ValueError('Focused contract tests must pass before matched observations: ' + variant)
    expected = ['proof-postgresql-snapshot-balanced-10-snapshot-provider-qualification']
    for variant in VARIANTS:
        expected.extend('-'.join(('proof', 'postgresql', variant, mode.lower(), '100', case))
                        for case in CRASH_CASES + ('sequence', 'suspension'))
        expected.extend('-'.join(('proof', 'sqlite', variant, mode.lower(), '10', case))
                        for case in ('suspension', 'late-output', 'waited-child', 'protected', 'compensation-control', 'compensation-kill'))
    for label in expected:
        row = runner.state.get('controllers', {}).get(label, {})
        if row.get('status') != 'PASSED':
            raise ValueError('Required actual recovery/semantic gate has not passed: ' + label)
        path = Path(row['phasePath'])
        if hashlib.sha256(path.read_bytes()).hexdigest() != row['phaseSha256']:
            raise ValueError('An earlier qualification receipt changed')
        manifest = json.loads(Path(row['manifestPath']).read_bytes())
        if hashlib.sha256(Path(row['manifestPath']).read_bytes()).hexdigest() != row['manifestSha256']:
            raise ValueError('An earlier qualification manifest changed')
        if label.endswith('snapshot-provider-qualification'):
            sources = {key: value['source'] for key, value in manifest['variants'].items()}
        elif 'variants' in manifest:
            sources = {key: value['source'] for key, value in manifest['variants'].items()}
        else:
            sources = {manifest['proofPlan']['variant']: manifest}
        for variant, identity in sources.items():
            item = runner.state['variants'][variant]
            if (identity['head'], identity['tree']) != (item['commit'], item['tree']):
                raise ValueError('Qualification was run before a source change: ' + label)


def measure(runner, profile, mode, leaves, action, samples, concurrency):
    from runner import VARIANTS, write
    if concurrency not in (1, 4) or ((profile == 'count' or action == 'resume') and concurrency != 1):
        raise ValueError('Count/cold resume require one caller; full timing permits one or four')
    variants = {variant: selected_binary(runner, variant) for variant in VARIANTS}
    require_measurement_gates(runner, mode)
    label = '-'.join(('matched', profile, mode.lower(), str(leaves), action, 'c' + str(concurrency)))
    private = runner.root / 'private' / label
    private.mkdir(parents=True, exist_ok=False, mode=0o700)
    request = fixture_request(runner, label, private / 'output', variants)
    request.update(profile=profile, case={'mode': mode, 'leaves': leaves, 'cap': 50, 'action': action,
        'samples': 1 if profile == 'count' or action == 'resume' else samples,
        'warmup': 0 if profile == 'count' or action == 'resume' else 5, 'concurrency': concurrency}, maxLoadAverage=2,
        timeouts={'control': 2, 'create': 10, 'start': 10, 'ready': 25, 'initialize': 10, 'prepare': 10, 'measure': 20})
    write(private / 'request.json', request)
    scripts = Path(variants['baseline']['worktree']) / 'tools/experiments/runtime-architecture'
    return execute_packet(runner, private, 'postgres_phase.py',
        [sys.executable, str(scripts / 'freeze_postgres_phase.py'), str(private / 'request.json'), str(private / 'packet')], label, True)

"""GitHub-hosted execution entrypoint. Local engine/container execution is refused."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import platform
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ARCHIVE_SHA = '67126d30b39d3e0ca6e5637ed1047153f3db24f593816903757df1bb26d305b7'
MANIFEST_SHA = 'fe68e5d4142564a7544b7fd1094c2f4a712f5c5834757c652d49587c59ff3f97'
PROBE = 'tools/experiments/runtime-architecture/RuntimeArchitectureProbe.csproj'
PROBE_LOCK = 'tools/experiments/runtime-architecture/packages.lock.json'
VARIANTS = ('baseline', 'runview', 'snapshot')
COMMON_REPAIR = '0001-common-harness-build-fixes.patch'
COMMON_REPAIR_SHA = '109dee78fb77b6c951a1f4751db9242a6e9d294c32bd4d1ff56531ea7803365b'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, indent=2) + '\n')
    temporary.replace(path)


def command(argv, log, cwd=None, check=True):
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open('wb') as stream:
        result = subprocess.run(argv, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT)
    if check and result.returncode:
        raise RuntimeError('Command failed; retained log: ' + str(log))
    return result.returncode


def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args], timeout=60).decode().strip()


def commit(root, message):
    git(root, 'add', '-A')
    git(root, '-c', 'user.name=Elsa Runtime Experiment', '-c', 'user.email=runtime-experiment@example.invalid',
        'commit', '-m', message)


def repair_common_harness(source, evidence, variant):
    """Apply the minimal hosted compiler repair after original input-tree verification."""
    patch = HERE / 'patches' / COMMON_REPAIR
    content = patch.read_bytes()
    if sha(content) != COMMON_REPAIR_SHA:
        raise ValueError('Reviewed common harness compiler repair differs')
    if git(source, 'status', '--porcelain', '--untracked-files=all'):
        raise ValueError('Compiler repair requires the clean verified original source')
    before_commit, before_tree = git(source, 'rev-parse', 'HEAD'), git(source, 'rev-parse', 'HEAD^{tree}')
    command(['git', '-C', str(source), 'apply', '--check', '--index', '--binary', str(patch)],
            evidence / ('repair-check-' + variant + '.log'))
    command(['git', '-C', str(source), 'apply', '--index', '--binary', str(patch)],
            evidence / ('repair-apply-' + variant + '.log'))
    expected = ['tools/experiments/runtime-architecture/Semantics.cs',
                'tools/experiments/runtime-architecture/VariantLoader.cs']
    if git(source, 'diff', '--cached', '--name-only').splitlines() != expected:
        raise ValueError('Common compiler repair changed unexpected source files')
    commit(source, 'Repair common probe namespace and transition enum compiler errors found by hosted build')
    target = evidence / 'source-repairs' / COMMON_REPAIR
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and target.read_bytes() != content:
        raise ValueError('An earlier exported compiler repair changed')
    if not target.exists():
        with target.open('xb') as stream:
            stream.write(content)
    return {'patchPath': str(target), 'patchSha256': sha(content),
        'beforeCommit': before_commit, 'beforeTree': before_tree,
        'afterCommit': git(source, 'rev-parse', 'HEAD'), 'afterTree': git(source, 'rev-parse', 'HEAD^{tree}'),
        'changedPaths': expected, 'applicationOnly': True}


class Runner:
    def __init__(self, root):
        if os.environ.get('GITHUB_ACTIONS') != 'true' or platform.system() != 'Linux':
            raise RuntimeError('This execution entrypoint requires an authorized Linux GitHub Actions job')
        temporary = Path(os.environ['RUNNER_TEMP']).resolve()
        self.root = root.resolve()
        if self.root == temporary or not self.root.is_relative_to(temporary):
            raise ValueError('State must be in a fresh owned subdirectory of RUNNER_TEMP')
        self.evidence = self.root / 'evidence'
        self.evidence.mkdir(parents=True, exist_ok=True)
        self.owner = {'runId': os.environ['GITHUB_RUN_ID'], 'attempt': os.environ['GITHUB_RUN_ATTEMPT'],
                      'repository': os.environ['GITHUB_REPOSITORY'], 'runnerRevision': os.environ['GITHUB_SHA']}
        self.state_path = self.root / 'state.json'
        if self.state_path.exists():
            self.state = json.loads(self.state_path.read_bytes())
            if self.state['owner'] != self.owner:
                raise ValueError('State belongs to another workflow run/attempt/revision')
        else:
            self.state = {'schema': 'runtime-prototype-github-state-v1', 'owner': self.owner,
                          'archiveSha256': ARCHIVE_SHA, 'variants': {}, 'stages': {}, 'qualifiedForBenchmarks': False}

    def save(self):
        try:
            self.export_evidence()
        except BaseException as error:
            self.state.setdefault('evidenceExportFailures', []).append({'type': type(error).__name__})
            raise
        finally:
            write(self.state_path, self.state)
            write(self.evidence / 'state.json', self.state)

    def export_evidence(self):
        from evidence_exports import source as export_source, build as export_build, immutable
        for variant, item in self.state['variants'].items():
            item['sourceExport'] = export_source(item, self.evidence, variant)
            # Preserve even an exploratory failed restore's generated lock, without qualifying it.
            lock = Path(item['worktree']) / PROBE_LOCK
            if lock.is_file():
                content = lock.read_bytes()
                target = self.evidence / 'diagnostic-locks' / variant / (sha(content) + '.json')
                immutable(target, content)
                item['probeLockSnapshot'] = {'path': str(target), 'sha256': sha(content), 'diagnosticSnapshotOnly': True}
            if item.get('buildStatus') == 'PASSED':
                item['binaryExport'] = export_build(item['buildReceipt'], self.evidence, variant, 'probe')
            if item.get('providerBuild', {}).get('status') == 'PASSED':
                record = item['providerBuild']
                record['binaryExport'] = export_build(record['buildReceipt'], self.evidence, variant, 'provider')
            if item.get('focusedTests', {}).get('receiptPath'):
                focused = json.loads(Path(item['focusedTests']['receiptPath']).read_bytes())
                exports = {}
                for group in focused['groups']:
                    if group.get('status') == 'PASSED' and 'phaseReceipt' in group:
                        exports[group['name']] = export_build(group['phaseReceipt'], self.evidence, variant, 'focused-' + group['name'])
                item['focusedBinaryExports'] = exports

    def refresh_source(self, variant):
        item = self.state['variants'][variant]
        source = Path(item['worktree'])
        if git(source, 'status', '--porcelain', '--untracked-files=all'):
            raise ValueError('Source must be clean before qualification: ' + variant)
        current = git(source, 'rev-parse', 'HEAD')
        if item['commit'] != current:
            self.invalidate_probe(variant)
        item.update(commit=current, tree=git(source, 'rev-parse', 'HEAD^{tree}'))
        return source

    def invalidate_probe(self, variant):
        item = self.state['variants'][variant]
        if 'buildReceipt' in item:
            item.setdefault('supersededBuildReceipts', []).append(item.pop('buildReceipt'))
        item.pop('binary', None)
        item.update(buildStatus='NOT_RUN', qualificationStatus='NOT_RUN')
        self.state['qualifiedForBenchmarks'] = False

    def sync_lock(self):
        baseline = self.refresh_source('baseline')
        lock = baseline / PROBE_LOCK
        git(baseline, 'ls-files', '--error-unmatch', PROBE_LOCK)
        content = lock.read_bytes()
        for variant in ('runview', 'snapshot'):
            source = self.refresh_source(variant)
            target = source / PROBE_LOCK
            if not target.is_file() or target.read_bytes() != content:
                target.write_bytes(content)
                commit(source, 'Use the common baseline probe dependency lock for matched execution')
                self.refresh_source(variant)
        self.state['commonProbeLockSha256'] = sha(content)
        self.save()

    def focused(self, variant):
        from focused_tests import run_focused_tests
        self.refresh_source(variant)
        run_focused_tests(self, variant)
        record = self.state['variants'][variant]['focusedTests']
        record['receiptSha256'] = sha(Path(record['receiptPath']).read_bytes())
        self.save()

    def materialize(self):
        if self.state['variants']:
            raise ValueError('Source materialization is create-only')
        archive = (HERE / 'source-transfer.json.gz').read_bytes()
        if sha(archive) != ARCHIVE_SHA:
            raise ValueError('Immutable reviewed source archive differs')
        payload = self.root / 'source-transfer.b64'
        payload.write_bytes(base64.b64encode(archive))
        transfer = self.root / 'transfer'
        command([sys.executable, str(HERE / 'unpack_transfer.py'), str(payload), str(transfer),
                 '--archive-sha256', ARCHIVE_SHA, '--manifest-sha256', MANIFEST_SHA], self.evidence / 'unpack.log')
        manifest = json.loads((transfer / 'manifest.json').read_bytes())
        write(self.evidence / 'source-manifest.json', manifest)
        repository = Path(git(HERE, 'rev-parse', '--show-toplevel'))
        base = manifest['base']['commit']
        git(repository, 'cat-file', '-e', base + '^{commit}')
        for name in VARIANTS:
            entry = manifest['variants'][name]
            source = self.root / 'sources' / name
            source.parent.mkdir(exist_ok=True)
            branch = 'codex/runtime-cloud-' + self.owner['runId'] + '-' + self.owner['attempt'] + '-' + name
            git(repository, 'worktree', 'add', '-b', branch, str(source), base)
            command(['git', '-C', str(source), 'apply', '--index', '--binary', str(transfer / entry['patch']['file'])],
                    self.evidence / ('apply-' + name + '.log'))
            if git(source, 'write-tree') != entry['resultTree']:
                raise ValueError('Applied source tree differs: ' + name)
            commit(source, 'Materialize reviewed runtime experiment source ' + name)
            initial = git(source, 'rev-parse', 'HEAD')
            common_repair = repair_common_harness(source, self.evidence, name)
            launcher = source / 'tools/experiments/runtime-architecture/launch_phase.py'
            content = launcher.read_text()
            old = "os.kill(expected['pid'], signal.SIGKILL)"
            new = "assert resources.signal_exact(expected, signal.SIGKILL) is True, 'Exact owned signal was not sent'"
            if content.count(old) != 1:
                raise ValueError('The reviewed watcher signal seam differs')
            before = sha(launcher.read_bytes())
            launcher.write_text(content.replace(old, new))
            from linux_source import adapt_linux_crash_identity
            identity_adaptation = adapt_linux_crash_identity(source)
            commit(source, 'Use reviewed Linux exact-process signal primitive in hosted watcher')
            self.state['variants'][name] = {'worktree': str(source), 'inputCommit': entry['originalCommit'],
                'verifiedInputTree': entry['resultTree'], 'materializedCommit': initial,
                'commonHarnessRepair': common_repair,
                'commit': git(source, 'rev-parse', 'HEAD'), 'tree': git(source, 'rev-parse', 'HEAD^{tree}'),
                'linuxAdaptation': {'path': str(launcher.relative_to(source)), 'beforeSha256': before, 'afterSha256': sha(launcher.read_bytes())},
                'linuxIdentityAdaptation': identity_adaptation,
                'buildStatus': 'NOT_RUN', 'qualificationStatus': 'NOT_RUN'}
            self.save()

    def phase(self, variant, label, commands, generated=()):
        source = Path(self.state['variants'][variant]['worktree'])
        root = self.evidence / 'phases' / (variant + '-' + label)
        root.mkdir(parents=True, exist_ok=False)
        helpers = HERE / 'helpers'
        for name in ('owned_resources.py', 'process_scope.py'):
            if not (helpers / name).is_file():
                raise ValueError('Reviewed Linux helper missing: ' + name)
        scripts = source / 'tools/experiments/runtime-architecture'
        request = {'name': variant + '-' + label, 'worktree': str(source), 'output': str(root / 'output'),
            'commands': commands, 'allowedGeneratedSourceFiles': list(generated),
            'helpers': {name: str(helpers / name) for name in ('owned_resources.py', 'process_scope.py')}}
        request['helpers']['launch_phase.py'] = str(scripts / 'launch_phase.py')
        write(root / 'request.json', request)
        packet = root / 'packet'
        command([sys.executable, str(scripts / 'freeze_phase.py'), str(root / 'request.json'), str(packet)], root / 'freeze.log')
        raw = (packet / 'manifest.json').read_bytes()
        code = command([sys.executable, str(packet / 'launch_phase.py'), str(packet), '--run', '--manifest-sha256', sha(raw)],
                       root / 'controller.log', check=False)
        receipt_path = root / 'output/phase.json'
        receipt = json.loads(receipt_path.read_bytes()) if receipt_path.exists() else {}
        if code != 0 or receipt.get('status') != 'COMMANDS_PASSED' or receipt.get('processQuiescent') is not True:
            raise RuntimeError('Bounded build phase failed; all logs retained at ' + str(root))
        return {'phasePath': str(receipt_path), 'manifestPath': str(packet / 'manifest.json'), 'manifestSha256': sha(raw)}

    def build(self, variant):
        item = self.state['variants'][variant]
        source = self.refresh_source(variant)
        if variant != 'baseline':
            self.sync_lock()
        binary_dir = source / 'tools/experiments/runtime-architecture/bin/Release/net10.0'
        flags = ['-m:1', '-nr:false', '-p:UseSharedCompilation=false']
        if not (source / PROBE_LOCK).exists():
            # Complete the actual baseline graph early. Generated lock + this exploratory receipt cannot qualify binaries.
            self.phase(variant, 'bootstrap', [
                {'name': 'restore-create-lock', 'argv': ['dotnet', 'restore', PROBE, '--use-lock-file', '--disable-parallel'] + flags, 'timeoutSeconds': 180},
                {'name': 'build-complete-probe', 'argv': ['dotnet', 'build', PROBE, '-c', 'Release', '--no-restore'] + flags,
                 'timeoutSeconds': 360, 'captureArtifactDirectories': [str(binary_dir)]}], generated=[PROBE_LOCK])
            commit(source, 'Commit generated runtime probe dependency lock before qualification')
        item.update(commit=git(source, 'rev-parse', 'HEAD'), tree=git(source, 'rev-parse', 'HEAD^{tree}'))
        self.invalidate_probe(variant)
        item['buildReceipt'] = self.phase(variant, 'qualified-build-' + item['commit'][:12], [
            {'name': 'locked-restore', 'argv': ['dotnet', 'restore', PROBE, '--locked-mode', '--disable-parallel'] + flags, 'timeoutSeconds': 120},
            {'name': 'build-complete-probe', 'argv': ['dotnet', 'build', PROBE, '-c', 'Release', '--no-restore', '--no-incremental'] + flags,
             'timeoutSeconds': 420, 'captureArtifactDirectories': [str(binary_dir)]}])
        item.update(buildStatus='PASSED', binary=str(binary_dir / 'RuntimeArchitectureProbe.dll'))
        self.save()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    sub = parser.add_subparsers(dest='stage', required=True)
    sub.add_parser('materialize')
    sub.add_parser('sync-lock')
    sub.add_parser('prepare-postgres')
    sub.add_parser('provider-build')
    sub.add_parser('provider-qualify')
    build = sub.add_parser('build')
    build.add_argument('--variant', choices=VARIANTS, required=True)
    focused = sub.add_parser('focused')
    focused.add_argument('--variant', choices=('runview', 'snapshot'), required=True)
    from stages import SQLITE_CASES
    proof = sub.add_parser('proof')
    proof.add_argument('--variant', choices=VARIANTS, required=True)
    proof.add_argument('--provider', choices=('Sqlite', 'PostgreSql'), required=True)
    proof.add_argument('--case', choices=SQLITE_CASES, required=True)
    proof.add_argument('--mode', choices=('Balanced', 'Durable'), required=True)
    proof.add_argument('--leaves', type=int, choices=(1, 10, 100), default=10)
    measure = sub.add_parser('measure')
    measure.add_argument('--profile', choices=('count', 'timing'), required=True)
    measure.add_argument('--mode', choices=('Balanced', 'Durable'), required=True)
    measure.add_argument('--leaves', type=int, choices=(1, 10, 100), required=True)
    measure.add_argument('--action', choices=('run', 'resume'), default='run')
    measure.add_argument('--samples', type=int, choices=range(1, 101), default=20)
    measure.add_argument('--concurrency', type=int, choices=(1, 4), default=1)
    args = parser.parse_args()
    runner = Runner(args.root)
    name = '-'.join(str(value) for value in (args.stage, getattr(args, 'variant', None),
        getattr(args, 'provider', None), getattr(args, 'case', None), getattr(args, 'profile', None),
        getattr(args, 'mode', None), getattr(args, 'leaves', None), getattr(args, 'action', None),
        ('c' + str(args.concurrency)) if hasattr(args, 'concurrency') else None) if value is not None)
    runner.state['stages'][name] = {'status': 'RUNNING'}
    runner.save()
    try:
        if args.stage == 'materialize':
            runner.materialize()
        elif args.stage == 'sync-lock':
            runner.sync_lock()
        elif args.stage == 'focused':
            runner.focused(args.variant)
        elif args.stage == 'build':
            runner.build(args.variant)
        elif args.stage == 'prepare-postgres':
            from postgres_source import prepare
            prepare(runner)
        elif args.stage == 'provider-build':
            from stages import build_provider
            build_provider(runner)
        elif args.stage == 'provider-qualify':
            from stages import proof
            proof(runner, 'snapshot', 'PostgreSql', 'snapshot-provider-qualification', 'Balanced', 10)
        elif args.stage == 'proof':
            from stages import proof
            proof(runner, args.variant, args.provider, args.case, args.mode, args.leaves)
        elif args.stage == 'measure':
            from stages import measure
            measure(runner, args.profile, args.mode, args.leaves, args.action, args.samples, args.concurrency)
        runner.state['stages'][name] = {'status': 'PASSED'}
        return 0
    except BaseException as error:
        runner.state['stages'][name] = {'status': 'FAILED', 'errorType': type(error).__name__, 'message': str(error)}
        (runner.evidence / (name + '-failure.txt')).write_text(type(error).__name__ + ': ' + str(error) + '\n')
        print(type(error).__name__ + ': ' + str(error), file=sys.stderr)
        return 1
    finally:
        try:
            runner.save()
        except BaseException as error:
            runner.state['stages'][name].update(status='FAILED', evidenceExportFailureType=type(error).__name__)
            write(runner.state_path, runner.state)
            write(runner.evidence / 'state.json', runner.state)
            raise


if __name__ == '__main__':
    raise SystemExit(main())

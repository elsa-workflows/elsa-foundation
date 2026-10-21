#!/usr/bin/env python3
"""Pack an immutable Rules source closure and verify a clean PackageReference consumer.

Artifacts stay outside the repository. This command never publishes packages or modifies
publish-state. Direct-package proof is a checkpoint, not full native/process/upgrade acceptance.
"""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
RULES = ('Elsa.Rules.Core', 'Elsa.Rules.Persistence.Core', 'Elsa.Rules.Runtime',
         'Elsa.Rules.Persistence.EntityFrameworkCore')
NATIVE = ('Elsa.Rules.Workflows.Design', 'Elsa.Rules.Workflows.Runtime')


def git(*args, cwd=ROOT):
    return subprocess.check_output(['git', *args], cwd=cwd, text=True).strip()


def immutable_commit(value):
    if not re.fullmatch(r'[0-9a-f]{40}', value):
        raise argparse.ArgumentTypeError('Use a full immutable lowercase commit SHA, not a branch or tag.')
    try:
        actual = git('rev-parse', '--verify', value + '^{commit}')
    except subprocess.CalledProcessError as error:
        raise argparse.ArgumentTypeError('Commit is not available locally; fetch it first.') from error
    if actual != value:
        raise argparse.ArgumentTypeError('The supplied object must itself be a commit.')
    return value


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_json(path, data):
    path.write_text(json.dumps(data, indent=2) + '\n')


def closure_for(graph, roots):
    by_path = {n['path']: n for n in graph['nodes']}
    by_id = {n['package_id']: n for n in graph['nodes'] if n['packable']}
    missing = set(roots) - by_id.keys()
    if missing:
        raise ValueError('Missing Rules modules: ' + ', '.join(sorted(missing)))
    seen, ordered = set(), []

    def visit(path):
        if path in seen:
            return
        seen.add(path)
        node = by_path[path]
        for edge in node['edges']:
            if edge['type'] == 'internal':
                visit(edge['path'])
        ordered.append(node)
    for package in roots:
        visit(by_id[package]['path'])
    return ordered


def package_metadata(path):
    with zipfile.ZipFile(path) as archive:
        specs = [n for n in archive.namelist() if '/' not in n and n.endswith('.nuspec')]
        if len(specs) != 1:
            raise ValueError('Expected one nuspec: ' + path.name)
        root = ET.fromstring(archive.read(specs[0]))
        metadata = next(e for e in root if e.tag.rsplit('}', 1)[-1] == 'metadata')
        fields = {e.tag.rsplit('}', 1)[-1]: e for e in metadata}
        return {'id': fields['id'].text, 'version': fields['version'].text,
                'sourceCommit': fields['repository'].attrib['commit'],
                'fingerprint': json.loads(archive.read('elsa-input-fingerprint.json'))['fingerprint'],
                'archiveSha512': base64.b64encode(hashlib.sha512(path.read_bytes()).digest()).decode(),
                'path': str(path)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-ref', required=True, type=immutable_commit)
    parser.add_argument('--publish-state-ref', required=True, type=immutable_commit)
    parser.add_argument('--evidence-dir', required=True, type=Path)
    parser.add_argument('--packed-run', type=Path, help='Reuse a verified immutable pack receipt; consumer caches remain fresh.')
    parser.add_argument('--stage', choices=('pack', 'direct', 'process'), default='direct',
                        help='pack retains build artifacts only and does not qualify a consumer.')
    args = parser.parse_args()
    base = args.evidence_dir.expanduser().resolve()
    if base.is_relative_to(ROOT) or ROOT.is_relative_to(base):
        parser.error('Use a dedicated evidence directory outside the source checkout.')
    evidence = base / ('run-' + uuid.uuid4().hex)
    evidence.mkdir(parents=True)
    source, feed = evidence / 'source', evidence / 'feed'
    feed.mkdir()
    receipt = {'schemaVersion': 1, 'sourceCommit': args.source_ref,
               'publishStateCommit': args.publish_state_ref, 'stage': args.stage,
               'directProofPassed': False, 'processBoundaryProofPassed': False, 'fullAcceptancePassed': False, 'commands': [],
               'harness': {str(path.relative_to(ROOT)): sha256(path) for path in
                           [Path(__file__), Path(__file__).with_name('package_consumer.py'),
                            Path(__file__).with_name('package_process.py'),
                            *sorted(Path(__file__).with_name('PackageProbe').glob('*.cs'))]}}
    print('Rules package evidence: ' + str(evidence), flush=True)
    environment = os.environ.copy()
    # No ambient credentials or injected build settings are needed by the public local-package proof.
    for name in ('GH_TOKEN', 'GITHUB_TOKEN', 'CustomBeforeDirectoryBuildProps', 'MSBuildExtensionsPath',
                 'MSBuildSDKsPath', 'ELSA_EF_CONNECTION', 'ELSA_EF_SCHEMA'):
        environment.pop(name, None)

    def run(command, cwd=None, env=environment, expect_failure=False, timeout=None):
        cwd = source if cwd is None else cwd
        number = len(receipt['commands']) + 1
        log = evidence / f'{number:02d}-command.log'
        entry = {'command': list(map(str, command)), 'cwd': str(cwd), 'log': str(log)}
        receipt['commands'].append(entry)
        print(f'[{number}] ' + ' '.join(map(str, command[:5])), flush=True)
        try:
            with log.open('w') as output:
                completed = subprocess.run(list(map(str, command)), cwd=cwd, env=env,
                                           stdout=output, stderr=subprocess.STDOUT, timeout=timeout)
            entry['exitCode'] = completed.returncode
            entry['expectedFailure'] = expect_failure
        except subprocess.TimeoutExpired:
            entry['timedOutSeconds'] = timeout
            raise
        finally:
            connection = env.get('ELSA_RULES_PROCESS_CONNECTION')
            if connection:
                output_text = log.read_text().replace(connection, '[REDACTED CONNECTION]')
                for password in re.findall(r'(?i)(?:^|;)Password=([^;]+)', connection):
                    output_text = output_text.replace(password, '[REDACTED]')
                log.write_text(output_text)
            entry['logSha256'] = sha256(log)
            write_json(evidence / 'receipt.json', receipt)
        if (completed.returncode != 0) != expect_failure:
            raise RuntimeError(f'Command {number} returned {completed.returncode} (expected failure: {expect_failure}); see {log}.')

    try:
        if args.packed_run:
            packed = args.packed_run.expanduser().resolve()
            previous = json.loads((packed / 'receipt.json').read_text())
            if (previous.get('schemaVersion') != 1 or previous.get('harnessUnchanged') is not True
                    or previous.get('error') or not previous.get('packPassed') or previous['sourceCommit'] != args.source_ref
                    or previous['publishStateCommit'] != args.publish_state_ref):
                raise ValueError('The reused pack must match both immutable inputs and have completed packing.')
            source, feed = packed / 'source', packed / 'feed'
            if git('rev-parse', 'HEAD', cwd=source) != args.source_ref or git('status', '--porcelain', cwd=source):
                raise ValueError('Reused source checkout is no longer the clean immutable input.')
            actual = [package_metadata(p) for p in sorted(feed.glob('*.nupkg'))]
            if actual != previous['packages']:
                raise ValueError('Reused package archives or their provenance changed.')
            receipt['reusedPackReceipt'] = {'path': str(packed / 'receipt.json'),
                                           'sha256': sha256(packed / 'receipt.json')}
            for key in ('lastPublishCommit', 'modules', 'pendingNativeModules', 'closure', 'packages', 'packPassed'):
                receipt[key] = previous[key]
        else:
            # The clone owns its metadata but reads existing immutable objects. No shared-worktree checkout is modified.
            run(['git', 'clone', '--shared', '--no-checkout', ROOT, source], cwd=evidence)
            run(['git', 'checkout', '--detach', args.source_ref])
            if git('rev-parse', 'HEAD', cwd=source) != args.source_ref or git('status', '--porcelain', cwd=source):
                raise ValueError('The detached source checkout is not the clean immutable input.')
            props = evidence / 'package-versions.props'
            run(['dotnet', 'run', '--project', 'tools/versioning/Elsa.Versioning.Calculator', '--',
                 '--repo', source, '--commit', args.source_ref, '--record-ref', args.publish_state_ref,
                 '--output', evidence / 'computation.json', '--pack-properties', props,
                 '--branch', 'agent/codex/3181-durable-rules'])
            computation = json.loads((evidence / 'computation.json').read_text())
            if computation['commit'] != args.source_ref:
                raise ValueError('The calculator did not compute the requested source commit.')
            receipt['lastPublishCommit'] = computation['last_publish_commit']
            run(['dotnet', 'run', '--project', 'tools/maps/Elsa.Maps.Generator', '--', 'dependency-map'])
            graph = json.loads((source / 'docs/maps/dependency-map.json').read_text())
            if graph['schema_version'] != 2:
                raise ValueError('Unknown dependency graph schema.')
            available = {n['package_id'] for n in graph['nodes'] if n['packable']}
            roots = list(RULES) + [n for n in NATIVE if n in available]
            closure = closure_for(graph, roots)
            receipt['modules'] = roots
            receipt['pendingNativeModules'] = sorted(set(NATIVE) - available)
            packages = [n for n in closure if n['packable']]
            computed = {p['package_id']: p for p in computation['packages']}
            versions = {}
            for group in ET.parse(props).getroot().findall('PropertyGroup'):
                version = group.findtext('ElsaComputedPackageVersion')
                if version:
                    name = re.fullmatch(r"'\$\(MSBuildProjectName\)' == '([^']+)'", group.attrib['Condition'])[1]
                    versions[name] = version
            receipt['closure'] = [{'id': n['package_id'], 'path': n['path'],
                                   'version': versions[Path(n['path']).stem]} for n in packages]
            filter_path = evidence / 'rules-pack.slnf'
            write_json(filter_path, {'solution': {'path': str(source / 'Elsa.Server.slnx'),
                        'projects': [n['path'] for n in closure]}})
            property_arg = '-p:CustomBeforeDirectoryBuildProps=' + str(props)
            run(['dotnet', 'restore', filter_path, '--locked-mode', property_arg])
            run(['dotnet', 'build', filter_path, '-c', 'Release', '--no-restore', property_arg])
            run(['dotnet', 'pack', filter_path, '-c', 'Release', '--no-build', '--no-restore',
                 property_arg, '--output', feed])
            records = [package_metadata(p) for p in sorted(feed.glob('*.nupkg'))]
            expected = {n['package_id']: versions[Path(n['path']).stem] for n in packages}
            if len(records) != len(expected) or {p['id']: p['version'] for p in records} != expected:
                raise ValueError('Packed artifacts do not match the complete expected closure.')
            for record in records:
                if record['sourceCommit'] != args.source_ref or record['fingerprint'] != computed[record['id']]['fingerprint']:
                    raise ValueError('Package source/fingerprint mismatch: ' + record['id'])
            if git('status', '--porcelain', cwd=source):
                raise ValueError('The clean source checkout changed during packing.')
            receipt['packages'] = records
            receipt['packPassed'] = True
        if args.stage in ('direct', 'process'):
            from package_consumer import verify_consumer
            verify_consumer(receipt, evidence, source, feed, run, environment, args.stage == 'process')
        exit_code = 0
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError, AssertionError) as error:
        receipt['error'] = str(error) or type(error).__name__
        print(receipt['error'], file=sys.stderr)
        exit_code = 1
    finally:
        unchanged = all(sha256(ROOT / path) == digest for path, digest in receipt['harness'].items())
        receipt['harnessUnchanged'] = unchanged
        if not unchanged:
            receipt['directProofPassed'] = False
            receipt['processBoundaryProofPassed'] = False
            receipt['error'] = 'Harness changed during execution.'
            exit_code = 1
        write_json(evidence / 'receipt.json', receipt)
    return exit_code


if __name__ == '__main__':
    sys.exit(main())

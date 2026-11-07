"""Retain actual source, dependency locks and successful output bytes before host disposal."""
import hashlib
import json
from pathlib import Path
import subprocess

BASE = 'f4b6c6d6de445ecbd160da7b16317ce3f03f25ad'


def sha(content):
    return hashlib.sha256(content).hexdigest()


def immutable(path, content):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        if path.read_bytes() != content:
            raise ValueError('An immutable exported artifact changed: ' + str(path))
    else:
        with path.open('xb') as stream:
            stream.write(content)


def json_bytes(value):
    return (json.dumps(value, indent=2) + '\n').encode()


def verify_file(root, relative, expected):
    path = root / relative
    if path.resolve() != path or not path.is_relative_to(root):
        raise ValueError('Exported artifact escaped its owned directory')
    content = path.read_bytes()
    if (len(content), sha(content)) != (expected['bytes'], expected['sha256']):
        raise ValueError('An earlier exported artifact changed: ' + relative)


def source(item, evidence, variant):
    root = Path(item['worktree'])
    destination = evidence / 'sources' / variant / item['commit']
    manifest_path = destination / 'source.json'
    if manifest_path.is_file():
        record = json.loads(manifest_path.read_bytes())
        if (record['sourceCommit'], record['sourceTree']) != (item['commit'], item['tree']):
            raise ValueError('Earlier source export identity changed')
        for name in ('sourcePatch', 'sourceBundle'):
            verify_file(destination, record[name]['path'], record[name])
        for lock in record['committedLocks']:
            verify_file(destination, lock['exportPath'], lock)
        return {'manifestPath': str(manifest_path), 'manifestSha256': sha(manifest_path.read_bytes())}
    destination.mkdir(parents=True, exist_ok=True)
    def git(*args):
        return subprocess.check_output(['git', '-C', str(root), *args], timeout=60)
    if git('rev-parse', 'HEAD').decode().strip() != item['commit']:
        raise ValueError('Exported source must be the recorded current HEAD')
    if git('rev-parse', 'HEAD^{tree}').decode().strip() != item['tree']:
        raise ValueError('Exported source tree differs from its recorded identity')
    patch = git('diff', '--binary', '--full-index', BASE, item['commit'])
    patch_path = destination / 'source.patch'
    immutable(patch_path, patch)
    bundle = destination / 'source.bundle'
    if bundle.exists():
        raise ValueError('Interrupted source bundle export requires a fresh hosted attempt')
    subprocess.run(['git', '-C', str(root), 'bundle', 'create', str(bundle), 'HEAD', '^' + BASE],
                   check=True, capture_output=True, timeout=60)
    locks = []
    names = git('ls-tree', '-r', '--name-only', '-z', item['commit']).decode().split('\0')
    for relative in names:
        if relative and Path(relative).name == 'packages.lock.json':
            content = git('show', item['commit'] + ':' + relative)
            target = destination / 'locks' / relative
            immutable(target, content)
            locks.append({'repositoryPath': relative, 'exportPath': str(target.relative_to(destination)),
                          'bytes': len(content), 'sha256': sha(content)})
    record = {'schema': 'runtime-prototype-source-export-v1', 'variant': variant, 'baseCommit': BASE,
        'sourceCommit': item['commit'], 'sourceTree': item['tree'],
        'sourcePatch': {'path': 'source.patch', 'bytes': len(patch), 'sha256': sha(patch)},
        'sourceBundle': {'path': 'source.bundle', 'bytes': bundle.stat().st_size, 'sha256': sha(bundle.read_bytes()),
                         'prerequisite': BASE, 'includesActualCommitObjects': True},
        'committedLocks': locks}
    immutable(manifest_path, json_bytes(record))
    return {'manifestPath': str(manifest_path), 'manifestSha256': sha(manifest_path.read_bytes())}


def build(receipt, evidence, variant, label):
    phase_path, manifest_path = Path(receipt['phasePath']), Path(receipt['manifestPath'])
    phase_raw, manifest_raw = phase_path.read_bytes(), manifest_path.read_bytes()
    phase, manifest = json.loads(phase_raw), json.loads(manifest_raw)
    if (phase.get('status') != 'COMMANDS_PASSED' or phase.get('processQuiescent') is not True or
            phase.get('manifestSha256') != sha(manifest_raw)):
        raise ValueError('Only an actual passed quiescent build/test phase may export qualified output bytes')
    destination = evidence / 'binaries' / variant / (label + '-' + sha(phase_raw)[:16])
    export_path = destination / 'binary-export.json'
    if export_path.exists():
        record = json.loads(export_path.read_bytes())
        if (record['phaseSha256'], record['manifestSha256']) != (sha(phase_raw), sha(manifest_raw)):
            raise ValueError('Earlier exported binary provenance changed')
        for directory in record['directories']:
            for file in directory['originalClosure']['files']:
                verify_file(destination, directory['exportDirectory'] + '/' + file['relativePath'], file)
        return {'manifestPath': str(export_path), 'manifestSha256': sha(export_path.read_bytes())}
    directories = []
    for row in phase['commands']:
        for index, closure in enumerate(row.get('artifactDirectories', [])):
            original = Path(closure['path'])
            members = sorted(original.rglob('*'))
            if any(path.is_symlink() for path in members):
                raise ValueError('Exported binary closure contains a symlink')
            inventory = sorted(str(path.relative_to(original)) for path in members if path.is_file())
            if inventory != closure['inventory'] or inventory != [file['relativePath'] for file in closure['files']]:
                raise ValueError('Captured full binary closure changed before export')
            prefix = row['name'] + '-' + str(index)
            for file in closure['files']:
                path = original / file['relativePath']
                if path.resolve() != path or not path.is_relative_to(original) or str(path) != file['path']:
                    raise ValueError('Captured binary path escaped its original directory')
                content = path.read_bytes()
                if (len(content), sha(content)) != (file['bytes'], file['sha256']):
                    raise ValueError('Actual binary bytes changed before export')
                immutable(destination / prefix / file['relativePath'], content)
            directories.append({'originalClosure': closure, 'exportDirectory': prefix})
    if not directories:
        raise ValueError('Successful receipt contains no full output closure to export')
    record = {'schema': 'runtime-prototype-binary-export-v1', 'variant': variant, 'label': label,
        'sourceCommit': manifest['head'], 'sourceTree': manifest['tree'],
        'phasePath': str(phase_path), 'phaseSha256': sha(phase_raw),
        'manifestPath': str(manifest_path), 'manifestSha256': sha(manifest_raw), 'directories': directories}
    immutable(export_path, json_bytes(record))
    return {'manifestPath': str(export_path), 'manifestSha256': sha(export_path.read_bytes())}

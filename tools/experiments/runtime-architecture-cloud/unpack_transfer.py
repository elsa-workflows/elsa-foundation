"""Verify/reconstruct the reviewed source package only. Never apply patches or run code."""
import argparse
import base64
import gzip
import hashlib
import json
import os
from pathlib import Path, PurePosixPath


def sha(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()


def decode(item):
    if item['encoding'] == 'utf8':
        return item['data'].encode('utf8')
    if item['encoding'] == 'base64':
        return base64.b64decode(item['data'], validate=True)
    raise ValueError('Unknown chunk encoding')


def write(root, name, data):
    relative = PurePosixPath(name)
    if relative.is_absolute() or '..' in relative.parts or not relative.parts:
        raise ValueError('Invalid output path')
    path = root.joinpath(*relative.parts)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('xb') as stream:
        stream.write(data)
    os.chmod(path, 0o600)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('base64_file', type=Path)
    parser.add_argument('new_output', type=Path)
    parser.add_argument('--archive-sha256', required=True)
    parser.add_argument('--manifest-sha256', required=True)
    args = parser.parse_args()
    archive = base64.b64decode(args.base64_file.read_bytes(), validate=True)
    if sha(archive) != args.archive_sha256:
        raise ValueError('Archive SHA differs from the delivered review identity')
    bundle = json.loads(gzip.decompress(archive))
    if bundle['schema'] != 'elsa-runtime-prototype-source-transfer-bundle-v2':
        raise ValueError('Unknown source package')
    manifest = canonical(bundle['manifest'])
    if sha(manifest) != bundle['manifestSha256'] or sha(manifest) != args.manifest_sha256:
        raise ValueError('Manifest SHA differs')
    chunks = {}
    for digest, item in bundle['chunks'].items():
        chunks[digest] = decode(item)
        if sha(chunks[digest]) != digest:
            raise ValueError('Patch chunk SHA differs')
    output = args.new_output.resolve()
    output.mkdir(parents=True, exist_ok=False, mode=0o700)
    write(output, 'manifest.json', manifest)
    for name, item in bundle['manifest']['variants'].items():
        patch = b''.join(chunks[digest] for digest in item['patch']['chunks'])
        if len(patch) != item['patch']['bytes'] or sha(patch) != item['patch']['sha256']:
            raise ValueError('Reconstructed patch SHA differs: ' + name)
        write(output, item['patch']['file'], patch)
    for name, item in bundle['manifest']['referenceOnlyFiles'].items():
        data = decode(bundle['referenceOnlyFiles'][name])
        if len(data) != item['bytes'] or sha(data) != item['sha256'] or not item['referenceOnly'] or item['cloudExecutable']:
            raise ValueError('Invalid reference-only helper')
        write(output, name, data)
    print(json.dumps({'verified': True, 'reconstructed': str(output), 'sourceOnly': True, 'applied': False}))


if __name__ == '__main__':
    main()

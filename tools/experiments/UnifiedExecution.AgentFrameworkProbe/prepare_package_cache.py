#!/usr/bin/env python3
"""Expand the ignored local feed into a probe-local NuGet global-packages cache."""
import base64
import hashlib
import json
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parent
feed = root / 'feed'
cache = root / 'packages-global'
manifest = json.loads((root / 'feed-manifest.json').read_text(encoding='utf-8'))
for package in manifest['packages']:
    package_id = package['id']
    version = package['version']
    source = feed / f'{package_id}.{version}.nupkg'
    destination = cache / package_id.lower() / version.lower()
    destination.mkdir(parents=True, exist_ok=True)
    payload = source.read_bytes()
    content_hash = base64.b64encode(hashlib.sha512(payload).digest()).decode('ascii')
    if len(payload) != package['size'] or content_hash != package['sha512Base64']:
        raise ValueError(f"Manifest verification failed for {package_id} {version}: size={len(payload)}, SHA512={content_hash}")
    with zipfile.ZipFile(source) as archive:
        for member in archive.infolist():
            path = (destination / member.filename).resolve()
            if not path.is_relative_to(destination.resolve()):
                raise ValueError(f'Unsafe package path: {member.filename}')
            if member.is_dir():
                path.mkdir(parents=True, exist_ok=True)
            else:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(archive.read(member.filename))
    (destination / 'nupkg.sha512').write_text(content_hash, encoding='utf-8')
    (destination / '.nupkg.metadata').write_text(json.dumps({
        'version': 2,
        'contentHash': content_hash,
        'source': manifest['source']
    }, separators=(',', ':')), encoding='utf-8')
print(f'Prepared {len(manifest["packages"])} NuGet cache entries under {cache}')

import hashlib, io, json, pathlib, subprocess, sys, tarfile, urllib.request

T = pathlib.Path('/root/elsa-eff-i68'); HOSTS = {'w1': '10.238.2.5', 'w2': '10.238.2.6'}; RB = '/home/elsarunner/elsa-eff-i68'
SSH = ['ssh', '-i', str(T / 'ssh-key'), '-o', f'UserKnownHostsFile={T}/known_hosts', '-o', 'BatchMode=yes']
series = sys.argv[1:]; secrets = [(T / 'pg-password').read_text().strip().encode(), (T / 'transfer-sas').read_text().strip().encode()]
out = T / 'i68-evidence.tar.gz'; assert not out.exists()
manifest = []
with tarfile.open(out, 'w:gz') as bundle:
    def add(name, data):
        assert not any(s in data for s in secrets), 'protected value in ' + name
        info = tarfile.TarInfo(name); info.size = len(data); info.mtime = 0; bundle.addfile(info, io.BytesIO(data))
        manifest.append({'path': name, 'bytes': len(data), 'sha256': hashlib.sha256(data).hexdigest()})
    for s in series:
        for p in sorted((T / s).rglob('*')):
            if p.is_file(): add(f'runner/{s}/{p.relative_to(T / s)}', p.read_bytes())
        if (T / f'{s}.log').exists(): add(f'runner/{s}.log', (T / f'{s}.log').read_bytes())
    for name, ip in HOSTS.items():
        # Node logs of the progressing loop are trimmed to their first and last megabyte; env files and databases are excluded.
        script = (f"cd {RB} && for f in node-*.log; do [ -f \"$f\" ] && {{ head -c 1000000 \"$f\"; printf '\\n[trimmed]\\n'; tail -c 1000000 \"$f\"; }} > \"$f.trim\"; done; "
                  f"tar -czf - --exclude='pg.env' --exclude='node.env' --exclude='*.db' --exclude='node-*.log' $(ls -d cases control-* node-*.log.trim 2>/dev/null)")
        data = subprocess.run(SSH + ['elsarunner@' + ip, script], capture_output=True, timeout=600, check=True).stdout
        with tarfile.open(fileobj=io.BytesIO(data)) as worker:
            for member in worker.getmembers():
                if member.isfile(): add(f'{name}/{member.name}', worker.extractfile(member).read())
    add('manifest.json', json.dumps(manifest, indent=1).encode())
meta = {'blob': out.name, 'bytes': out.stat().st_size, 'sha256': hashlib.sha256(out.read_bytes()).hexdigest(), 'files': len(manifest), 'protectedValuesAbsent': True}
account = (T / 'transfer-account').read_text().strip(); token = (T / 'transfer-sas').read_text().strip()
try:
    request = urllib.request.Request(f'https://{account}.blob.core.windows.net/transfer/{out.name}?{token}', data=out.read_bytes(), method='PUT',
                                     headers={'x-ms-blob-type': 'BlockBlob', 'x-ms-version': '2023-11-03', 'If-None-Match': '*'})
    with urllib.request.urlopen(request, timeout=300) as response: assert response.status == 201
    meta['upload'] = 'passed'
except Exception as e:
    meta['upload'] = 'failed'; meta['errorType'] = type(e).__name__
print('UPLOADMETA ' + json.dumps(meta))

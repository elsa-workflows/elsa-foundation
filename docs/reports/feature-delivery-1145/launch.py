#!/usr/bin/env python3
"""Launch only task-owned containers; mount no restore cache or compiled fixture binaries."""
import argparse
import json
import pathlib
import subprocess
from prepare import IMAGE


def launch(root, attempt):
    state = root / f'state-{attempt:02d}'
    data = root / f'data-{attempt:02d}'
    if state.exists() or data.exists():
        raise RuntimeError('Each attempt must begin with fresh state and data')
    state.mkdir()
    data.mkdir()
    name = f'fr2-1145-host-{attempt:02d}'
    args = ['docker', 'run', '-d', '--name', name, '--label', 'codex.task=1145', '--cpus', '2',
            '--env-file', str(root / 'private.env'), '-p', '127.0.0.1::8443',
            '--health-cmd', 'curl --insecure --fail --silent https://localhost:8443/health/ready', '--health-start-period', '120s']
    for source, target, mode in [
        (root / 'config', '/proof/config', 'ro'), (root / 'catalog', '/proof/catalog', 'ro'),
        (root / 'packages', '/proof/packages', 'ro'), (state, '/app/.nuplane', 'rw'), (data, '/proof/data', 'rw'),
        (root / 'config/appsettings.json', '/app/appsettings.json', 'ro'),
        (root / 'config/shells.json', '/app/shells.json', 'ro'),
    ]:
        args.extend(['-v', f'{source}:{target}:{mode}'])
    args.append(IMAGE)
    container = subprocess.check_output(args, text=True).strip()
    port = subprocess.check_output(['docker', 'port', name, '8443/tcp'], text=True).strip().split(':')[-1]
    receipt = dict(name=name, id=container, image=IMAGE, url=f'https://localhost:{port}', state=str(state), data=str(data), emptyStateBeforeLaunch=True)
    (root / 'receipts' / f'attempt-{attempt:02d}-launch.json').write_text(json.dumps(receipt, indent=2) + '\n')
    (root / 'active-host.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps(receipt))


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('root', type=pathlib.Path)
    p.add_argument('attempt', type=int)
    a = p.parse_args()
    launch(a.root.resolve(), a.attempt)

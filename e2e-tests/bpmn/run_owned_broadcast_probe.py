#!/usr/bin/env python3
"""Own one probe host for a Unix stop/continue cut; keep stopped-child handling outside the .NET controller."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shlex
import signal
import subprocess
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assembly', type=Path, required=True)
    parser.add_argument('--content-root', type=Path, required=True)
    parser.add_argument('--base-url', required=True)
    parser.add_argument('--environment-file', type=Path, required=True)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--stop-file', type=Path, required=True)
    args = parser.parse_args()
    assembly = args.assembly.absolute(); root = args.content_root.absolute()
    environment = dict(os.environ); environment.update(json.loads(args.environment_file.read_text()))
    environment['ASPNETCORE_ENVIRONMENT'] = 'Development'; environment['ASPNETCORE_URLS'] = args.base_url
    stopping = False
    def stop(_signum, _frame):
        nonlocal stopping
        stopping = True
    signal.signal(signal.SIGTERM, stop)
    with (root / 'server-probe.out.log').open('w') as stdout, (root / 'server-probe.err.log').open('w') as stderr:
        child = subprocess.Popen(['dotnet', str(assembly), '--contentRoot', str(root)], cwd=root, env=environment, stdout=stdout, stderr=stderr)
        try:
            deadline = time.monotonic() + 10
            while True:
                if child.poll() is not None: raise RuntimeError('The owned probe exited during executable startup.')
                arguments = shlex.split(subprocess.check_output(['ps', '-ww', '-p', str(child.pid), '-o', 'command='], text=True))
                if str(assembly) in arguments and str(root) in arguments: break
                if time.monotonic() >= deadline: raise TimeoutError('Owned executable identity did not become observable.')
                time.sleep(0.025)
            start = subprocess.check_output(['ps', '-p', str(child.pid), '-o', 'lstart='], text=True).strip()
            epoch = int(datetime.strptime(start, '%a %b %d %H:%M:%S %Y').astimezone(timezone.utc).timestamp())
            temporary = args.manifest.with_suffix('.tmp')
            temporary.write_text(json.dumps({'controllerProcessId': os.getpid(), 'processId': child.pid, 'startEpochSeconds': epoch}, indent=2) + '\n')
            temporary.replace(args.manifest)
            while not stopping and not args.stop_file.exists():
                if child.poll() is not None: raise RuntimeError('The owned probe exited before the controller stopped it.')
                time.sleep(0.05)
        finally:
            if child.poll() is None: child.kill()  # Also stops a SIGSTOPed child; never looks up a listener or unrelated PID.
            child.wait(timeout=15)


if __name__ == '__main__':
    main()

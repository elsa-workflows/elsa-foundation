#!/usr/bin/env python3
"""Signal one explicitly owned probe process after birth/argument checks; modifies no application rows."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shlex
import signal
import sqlite3
import subprocess
import time


def process_identity(pid, assemblies, roots, expected_start):
    def start():
        text = subprocess.check_output(['ps', '-ww', '-p', str(pid), '-o', 'lstart='], text=True).strip()
        epoch = int(datetime.strptime(text, '%a %b %d %H:%M:%S %Y').astimezone(timezone.utc).timestamp())
        return text, epoch
    before = start()
    command = subprocess.check_output(['ps', '-ww', '-p', str(pid), '-o', 'command='], text=True)
    arguments = tuple(shlex.split(command))
    after = start()
    if before != after or before[1] != expected_start:
        raise ValueError('The owned process birth identity changed or differs from its registered start.')
    if not any(value in arguments for value in assemblies) or not any(value in arguments for value in roots):
        raise ValueError('The process does not have the exact owned assembly/content-root arguments.')
    return before[0], arguments


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pid', type=int, required=True)
    parser.add_argument('--expected-start', type=int, required=True)
    parser.add_argument('--assembly', type=Path, required=True)
    parser.add_argument('--content-root', type=Path, required=True)
    parser.add_argument('--database', type=Path, action='append', default=[])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--identity-marker', type=Path)
    parser.add_argument('--resume', action='store_true')
    args = parser.parse_args()
    if not hasattr(signal, 'SIGSTOP') or args.pid <= 1 or (not args.resume and not args.database):
        raise ValueError('This cut requires an owned Unix process and databases for pause.')
    assemblies = {str(args.assembly.absolute()), str(args.assembly.resolve(strict=True))}
    roots = {str(args.content_root.absolute()), str(args.content_root.resolve(strict=True))}
    def identity():
        return process_identity(args.pid, assemblies, roots, args.expected_start)
    expected = identity()
    if args.identity_marker: args.identity_marker.write_text(json.dumps({'processId': args.pid, 'startEpochSeconds': args.expected_start}))
    if args.resume:
        if identity() != expected: raise ValueError('The owned process changed before resume.')
        os.kill(args.pid, signal.SIGCONT)
        args.output.write_text(json.dumps({'processId': args.pid, 'startEpochSeconds': args.expected_start,
            'resumedAtUtc': datetime.now(timezone.utc).isoformat()}, indent=2) + '\n')
        return
    connections = []
    paused = False
    observation = None
    try:
        # Reserve both shared writers before suspension, then roll back without row/claim changes.
        for database in sorted({path.resolve(strict=True) for path in args.database}):
            connection = sqlite3.connect(database.as_uri() + '?mode=rw', uri=True, timeout=15)
            connections.append(connection); connection.execute('begin immediate')
        # Reservations may have waited. Recheck the parent's registered birth and exact arguments immediately before signal.
        if identity() != expected: raise ValueError('The owned process changed during writer reservation.')
        os.kill(args.pid, signal.SIGSTOP); paused = True
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            if identity() != expected: raise ValueError('The owned process changed after suspension.')
            state = subprocess.check_output(['ps', '-p', str(args.pid), '-o', 'state='], text=True).strip()
            if 'T' in state:
                observation = {'processId': args.pid, 'startEpochSeconds': args.expected_start, 'state': state,
                    'pausedAtUtc': datetime.now(timezone.utc).isoformat(), 'writerReservations': len(connections)}
                break
            time.sleep(0.025)
        if observation is None: raise TimeoutError('The owned process did not report suspended state.')
    except Exception:
        if paused:
            try:
                if identity() == expected: os.kill(args.pid, signal.SIGCONT)
            except (OSError, ValueError, subprocess.SubprocessError):
                pass  # Never signal a vanished/replaced process during cleanup.
        raise
    finally:
        for connection in reversed(connections):
            connection.rollback(); connection.close()
    observation['writerReservationsRolledBackWithoutDataChanges'] = True
    args.output.write_text(json.dumps(observation, indent=2) + '\n')


if __name__ == '__main__':
    main()

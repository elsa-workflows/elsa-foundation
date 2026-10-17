#!/usr/bin/env python3
"""Bounded real-silo bootstrap/recovery probe; not the durable provider witness."""
import json
import pathlib
import re
import socket
import subprocess
import tempfile
import time
import uuid

root = pathlib.Path(__file__).resolve().parent
binary = root / 'bin/Debug/net10.0/UnifiedExecution.ProviderProbe.dll'
if not binary.exists():
    raise SystemExit('Build the project before running this preflight.')
for port in (19111, 19112, 19211, 19212):
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', port))
output = pathlib.Path(tempfile.mkdtemp(prefix='elsa-orleans-preflight-'))
processes, streams = [], []
evidence = {'package': 'Microsoft.Orleans.Server 10.4.0', 'scope': 'loopback multiprocess activation only',
            'key': str(uuid.uuid4()), 'attempts': []}

def client(gateway=19211):
    started = time.monotonic()
    try:
        result = subprocess.run(['dotnet', str(binary), 'client', evidence['key'], str(gateway)], capture_output=True, text=True, timeout=20)
        content = result.stdout + result.stderr
        code = result.returncode
    except subprocess.TimeoutExpired as error:
        content = str(error.stdout) + str(error.stderr)
        code = 'timeout'
    log = f'client-{len(evidence["attempts"])}.log'
    (output / log).write_text(content)
    match = re.search(r'GRAIN_LOCATION .* pid=(\d+)', content)
    evidence['attempts'].append({'exit': code, 'log': log, 'seconds': time.monotonic() - started})
    return int(match.group(1)) if code == 0 and match else None

try:
    for port in (19111, 19112):
        log = output / f'silo-{port}.log'
        stream = log.open('w')
        streams.append(stream)
        process = subprocess.Popen(['dotnet', str(binary), 'silo', str(port)], stdout=stream, stderr=subprocess.STDOUT)
        processes.append(process)
        deadline = time.monotonic() + 30
        while f'SILO_READY {port}' not in log.read_text():
            if process.poll() is not None or time.monotonic() > deadline:
                raise RuntimeError(f'Silo {port} did not become ready; see {log}')
            time.sleep(.1)
    evidence['siloPids'] = [p.pid for p in processes]
    initial = client(19212)
    assert initial in evidence['siloPids'], 'Activation must run in an owned silo process'
    # Local development membership uses the first silo as seed. Test recovery by killing
    # an activation only when it is on the second silo; a seed failure is a separate test.
    if initial == processes[0].pid:
        raise RuntimeError('Activation selected seed silo; rerun with a new key to test non-seed death. No recovery claim.')
    next(p for p in processes if p.pid == initial).kill()
    next(p for p in processes if p.pid == initial).wait(timeout=10)
    evidence['killedActivationPid'] = initial
    deadline = time.monotonic() + 120
    recovered = None
    while recovered is None and time.monotonic() < deadline:
        recovered = client()
        if recovered is None:
            time.sleep(1)
    assert recovered == processes[0].pid, 'Same grain key must reactivate on the surviving silo'
    evidence['recoveredActivationPid'] = recovered
    evidence['passed'] = True
except BaseException as error:
    evidence['passed'] = False
    evidence['error'] = str(error)
    raise
finally:
    for process in processes:
        if process.poll() is None:
            process.terminate()
    for process in processes:
        try:
            process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
    for stream in streams:
        stream.close()
    (output / 'evidence.json').write_text(json.dumps(evidence, indent=2))
    print(output / 'evidence.json')

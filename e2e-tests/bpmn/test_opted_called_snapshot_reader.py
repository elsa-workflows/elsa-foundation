#!/usr/bin/env python3
"""Check actual opted-in host snapshots and explicitly private invalid fixture copies."""
import argparse
import base64
from contextlib import closing
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import sqlite3
import subprocess
import tempfile
from test_called_snapshot_reader import create_orphan, digest, freeze_private_copy


def remove_capability(database, parent_id):
    decode = lambda value: base64.b64decode(value).decode('utf-16-le')
    with closing(sqlite3.connect(database)) as connection:
        matches = [(identity, json.loads(content)) for identity, content in connection.execute('SELECT Id, ContentJson FROM elsa_runtime_workflow_dispatch')
                   if decode(json.loads(content)['parentWorkflowExecutionId']) == parent_id]
        if len(matches) != 1:
            raise RuntimeError('Exactly one actual opted-in dispatch must identify the private control.')
        identity, wire = matches[0]
        required = {'runtime.childControlConsumerKey', 'runtime.childControlConsumerVersion'}
        if len([key for key in wire['metadata'] if decode(key) in required]) != 2:
            raise RuntimeError('The real dispatch did not originally declare both consumer fields.')
        wire['metadata'] = {key: value for key, value in wire['metadata'].items() if decode(key) not in required}
        connection.execute('UPDATE elsa_runtime_workflow_dispatch SET ContentJson=? WHERE Id=?', (json.dumps(wire, separators=(',', ':')), identity))
        connection.commit()
    return freeze_private_copy(database)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reader-assembly', type=Path, required=True)
    parser.add_argument('--database', type=Path, required=True)
    parser.add_argument('--sha256', required=True, help='Expected uncompressed real runtime snapshot hash.')
    parser.add_argument('--api-receipt', type=Path, required=True)
    parser.add_argument('--phase', choices=['coded', 'escalation-parked', 'escalation-recovered', 'escalation-finished'], required=True)
    parser.add_argument('--scope', default='default')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    original = args.database.read_bytes()
    raw = gzip.decompress(original) if args.database.suffix == '.gz' else original
    if hashlib.sha256(raw).hexdigest() != args.sha256:
        raise ValueError('The real snapshot differs from its expected hash.')
    api = json.loads(args.api_receipt.read_text())
    records = []
    with tempfile.TemporaryDirectory(prefix='elsa-opted-called-reader-') as temporary:
        root = Path(temporary); baseline = root / 'actual.sqlite'; baseline.write_bytes(raw)

        def run(name, database, receipt, expected, expected_hash=None):
            if any(Path(str(database) + suffix).exists() for suffix in ['-wal', '-shm', '-journal']):
                raise RuntimeError('A private fixture connection or sidecar remains live.')
            with closing(sqlite3.connect('file:' + str(database) + '?mode=ro&immutable=1', uri=True)) as connection:
                if connection.execute('PRAGMA integrity_check').fetchone() != ('ok',):
                    raise RuntimeError('Private fixture SQLite integrity failed.')
            before = digest(database)
            result = subprocess.run(['dotnet', str(args.reader_assembly.resolve()), '--inspect-opted-called', str(database), expected_hash or before,
                                     args.scope, str(receipt.resolve()), args.phase], capture_output=True, text=True, check=False)
            observed = json.loads(result.stdout)
            if result.returncode != expected or observed.get('providerChecked') != (expected == 0):
                raise RuntimeError(f'Control {name} returned unexpected status {result.returncode}.')
            if result.stderr or digest(database) != before:
                raise RuntimeError('The reader leaked diagnostics or changed a closed snapshot.')
            if expected == 0:
                if not observed.get('readOnly') or observed.get('snapshotSha256') != before or len(observed['calledWorkflows']) != len(api['cases']):
                    raise RuntimeError('The complete actual public cases were not checked.')
            elif observed.get('status') != 'ObservationUnavailable' or observed.get('failureType') != 'InvalidDataException':
                raise RuntimeError('The private invalid fixture missed the expected checked boundary.')
            records.append({'name': name, 'exitCode': result.returncode, 'databaseSha256': before,
                            'outputSha256': hashlib.sha256(result.stdout.encode()).hexdigest(), 'unchanged': True})

        run('actual-public-host-snapshot', baseline, args.api_receipt, 0)
        run('wrong-snapshot-hash', baseline, args.api_receipt, 2, '0' * 64)
        for field, value in [('artifactHash', 'sha256:foreign-pin'), ('artifactVersion', '99.0.0')]:
            foreign = json.loads(args.api_receipt.read_text()); foreign['cases'][0]['parent']['instance'][field] = value
            path = root / (field + '.json'); path.write_text(json.dumps(foreign))
            run('foreign-api-' + field, baseline, path, 2)
        orphan = root / 'orphan.sqlite'; shutil.copy2(baseline, orphan)
        run('owed-orphan-hidden-behind-canonical-source', create_orphan(orphan), args.api_receipt, 2)
        undeclared = root / 'undeclared.sqlite'; shutil.copy2(baseline, undeclared)
        run('actual-dispatch-loses-consumer-declaration', remove_capability(undeclared, api['cases'][0]['parentWorkflowExecutionId']), args.api_receipt, 2)
    if args.database.read_bytes() != original:
        raise RuntimeError('The original actual snapshot changed.')
    args.output.write_text(json.dumps({'passed': True, 'phase': args.phase, 'controls': records,
                                      'originalSnapshotUnchanged': True, 'scope': args.scope}, indent=2) + '\n')
    print('Actual public snapshot accepted; wrong hash, foreign complete pins, owed orphan and missing consumer declaration refused without writes or diagnostic leakage.')


if __name__ == '__main__':
    main()

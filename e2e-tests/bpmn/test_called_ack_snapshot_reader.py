#!/usr/bin/env python3
"""Exercise actual pre-ACK/recovered snapshots and closed private invalid copies."""
import argparse
import base64
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
from contextlib import closing
import sqlite3
from test_called_snapshot_reader import create_orphan, digest, freeze_private_copy
from test_opted_called_snapshot_reader import remove_capability


def remove_accepted_inbox(database, calling_id):
    with closing(sqlite3.connect(database)) as connection:
        matches = [(identity, json.loads(content)) for identity, content in connection.execute('SELECT Id, ContentJson FROM elsa_runtime_activity_execution_state')
                   if base64.b64decode(json.loads(content)['execution']['activityExecutionId']).decode('utf-16-le') == calling_id]
        if len(matches) != 1 or not matches[0][1].get('childControlInbox'):
            raise RuntimeError('Exactly one actually accepted calling inbox is required for the private control.')
        identity, wire = matches[0]; wire.pop('childControlInbox')
        connection.execute('UPDATE elsa_runtime_activity_execution_state SET ContentJson=? WHERE Id=?', (json.dumps(wire, separators=(',', ':')), identity))
        connection.commit()
    return freeze_private_copy(database)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--reader-assembly', type=Path, required=True)
    p.add_argument('--database', type=Path, required=True)
    p.add_argument('--sha256', required=True)
    p.add_argument('--api-receipt', type=Path, required=True)
    p.add_argument('--marker', type=Path, required=True)
    p.add_argument('--phase', choices=['held', 'recovered', 'finished'], required=True)
    p.add_argument('--scope', default='default')
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args()
    original = args.database.read_bytes(); raw = gzip.decompress(original) if args.database.suffix == '.gz' else original
    if hashlib.sha256(raw).hexdigest() != args.sha256:
        raise ValueError('The actual snapshot differs from its expected hash.')
    api = json.loads(args.api_receipt.read_text()); marker = json.loads(args.marker.read_text()); records = []
    with tempfile.TemporaryDirectory(prefix='elsa-called-ack-controls-') as temporary:
        root = Path(temporary); baseline = root / 'actual.sqlite'; baseline.write_bytes(raw)

        def run(name, database, receipt, mark, expected, expected_hash=None):
            if any(Path(str(database) + suffix).exists() for suffix in ['-wal', '-shm', '-journal']):
                raise RuntimeError('A private fixture connection or sidecar remains live.')
            with closing(sqlite3.connect('file:' + str(database) + '?mode=ro&immutable=1', uri=True)) as c:
                if c.execute('pragma integrity_check').fetchone() != ('ok',):
                    raise RuntimeError('A private fixture lost SQLite integrity.')
            before = digest(database)
            result = subprocess.run(['dotnet', str(args.reader_assembly.resolve()), '--inspect-called-ack', str(database), expected_hash or before,
                                     args.scope, str(receipt.resolve()), str(mark.resolve()), args.phase], capture_output=True, text=True, check=False)
            value = json.loads(result.stdout)
            if result.returncode != expected or value.get('providerChecked') != (expected == 0):
                raise RuntimeError(f'Control {name} returned unexpected status {result.returncode}.')
            if result.stderr or digest(database) != before:
                raise RuntimeError('The reader leaked diagnostics or changed a closed fixture.')
            if expected == 0:
                if not value.get('readOnly') or value.get('snapshotSha256') != before or not value['calledWorkflow'].get('exactCommittedAcceptanceVerified'):
                    raise RuntimeError('Actual committed acceptance was not checked.')
            elif value.get('status') != 'ObservationUnavailable' or value.get('failureType') != 'InvalidDataException':
                raise RuntimeError('The invalid fixture did not meet its checked refusal boundary.')
            records.append({'name': name, 'exitCode': result.returncode, 'databaseSha256': before, 'outputSha256': hashlib.sha256(result.stdout.encode()).hexdigest(), 'unchanged': True})

        run('actual-parent-commit-source-state', baseline, args.api_receipt, args.marker, 0)
        run('wrong-snapshot-hash', baseline, args.api_receipt, args.marker, 2, '0' * 64)
        foreign = json.loads(args.api_receipt.read_text()); foreign['cases'][0]['parent']['instance']['artifactVersion'] = '99.0.0'
        receipt = root / 'foreign-pin.json'; receipt.write_text(json.dumps(foreign)); run('foreign-complete-api-pin', baseline, receipt, args.marker, 2)
        for field, value in [('sourceCommitId', 'foreign-source-commit'), ('fencingToken', 9223372036854775807), ('scope', 'foreign-scope')]:
            altered = dict(marker); altered[field] = value; path = root / (field + '.json'); path.write_text(json.dumps(altered))
            run('foreign-marker-' + field, baseline, args.api_receipt, path, 2)
        orphan = root / 'orphan.sqlite'; shutil.copy2(baseline, orphan)
        run('owed-physical-orphan-hidden-behind-canonical-source', create_orphan(orphan), args.api_receipt, args.marker, 2)
        undeclared = root / 'undeclared.sqlite'; shutil.copy2(baseline, undeclared)
        case = api['cases'][0]; call_id = next(a['activityExecutionId'] for a in case['parent']['activities'] if a['executableNodeId'] == 'node-call')
        run('actual-calling-dispatch-loses-declaration', remove_capability(undeclared, case['parentWorkflowExecutionId'], call_id), args.api_receipt, args.marker, 2)
        unaccepted = root / 'unaccepted.sqlite'; shutil.copy2(baseline, unaccepted)
        run('actual-caller-loses-committed-inbox', remove_accepted_inbox(unaccepted, call_id), args.api_receipt, args.marker, 2)
    if args.database.read_bytes() != original:
        raise RuntimeError('The original actual snapshot changed.')
    args.output.write_text(json.dumps({'passed': True, 'phase': args.phase, 'controls': records, 'originalSnapshotUnchanged': True}, indent=2) + '\n')
    print('Nine actual ACK-reader controls passed without writes or raw diagnostic leakage.')


if __name__ == '__main__':
    main()

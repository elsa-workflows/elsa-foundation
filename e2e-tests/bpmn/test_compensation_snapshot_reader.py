#!/usr/bin/env python3
"""Check actual ordinary-task compensation snapshots and closed private invalid copies."""
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


def change_state(database, node, change):
    decode = lambda value: base64.b64decode(value).decode('utf-16-le')
    with closing(sqlite3.connect(database)) as connection:
        rows = [(identity, json.loads(content)) for identity, content in connection.execute('SELECT Id, ContentJson FROM elsa_runtime_activity_execution_state')
                if decode(json.loads(content)['execution']['executableNodeId']) == node]
        if len(rows) != 1:
            raise RuntimeError('Exactly one actual execution is required for the private control.')
        identity, wire = rows[0]; change(wire)
        connection.execute('UPDATE elsa_runtime_activity_execution_state SET ContentJson=? WHERE Id=?', (json.dumps(wire, separators=(',', ':')), identity))
        connection.commit()
    return freeze_private_copy(database)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--reader-assembly', type=Path, required=True)
    p.add_argument('--database', type=Path, required=True)
    p.add_argument('--sha256', required=True)
    p.add_argument('--api-receipt', type=Path, required=True)
    p.add_argument('--stage', choices=['HandlerB', 'HandlerA', 'Settled', 'Finished'], required=True)
    p.add_argument('--scope', default='default')
    p.add_argument('--output', type=Path, required=True)
    args = p.parse_args(); original = args.database.read_bytes(); raw = gzip.decompress(original) if args.database.suffix == '.gz' else original
    if hashlib.sha256(raw).hexdigest() != args.sha256:
        raise ValueError('Actual compensation snapshot differs from its expected hash.')
    records = []
    with tempfile.TemporaryDirectory(prefix='elsa-compensation-reader-') as temporary:
        root = Path(temporary); baseline = root / 'actual.sqlite'; baseline.write_bytes(raw)

        def run(name, database, api, expected, expected_hash=None):
            if any(Path(str(database) + suffix).exists() for suffix in ['-wal', '-shm', '-journal']):
                raise RuntimeError('A private fixture connection or sidecar remains live.')
            with closing(sqlite3.connect(database.resolve().as_uri() + '?mode=ro&immutable=1', uri=True)) as c:
                if c.execute('pragma integrity_check').fetchone() != ('ok',):
                    raise RuntimeError('The private fixture lost SQLite integrity.')
            before = digest(database)
            result = subprocess.run(['dotnet', str(args.reader_assembly.resolve()), '--inspect-compensation', str(database), expected_hash or before,
                                     args.scope, str(api.resolve()), args.stage], capture_output=True, text=True, check=False)
            value = json.loads(result.stdout)
            if result.returncode != expected or value.get('providerChecked') != (expected == 0):
                raise RuntimeError(f'Control {name} returned unexpected status {result.returncode}.')
            if result.stderr or digest(database) != before:
                raise RuntimeError('The reader leaked diagnostics or changed a closed fixture.')
            if expected == 0:
                if not value.get('readOnly') or value.get('snapshotSha256') != before or not value['compensation'].get('exactHandlerAndReplayOrderVerified'):
                    raise RuntimeError('Actual compensation identity and replay order were not checked.')
            elif value.get('status') != 'ObservationUnavailable' or value.get('failureType') != 'InvalidDataException':
                raise RuntimeError('The invalid fixture missed its checked refusal boundary.')
            records.append({'name': name, 'exitCode': result.returncode, 'databaseSha256': before,
                            'outputSha256': hashlib.sha256(result.stdout.encode()).hexdigest(), 'unchanged': True})

        run('actual-compensation-stage', baseline, args.api_receipt, 0)
        run('wrong-snapshot-hash', baseline, args.api_receipt, 2, '0' * 64)
        api = json.loads(args.api_receipt.read_text()); api['workflow']['instance']['artifactVersion'] = '99.0.0'
        foreign = root / 'foreign-pin.json'; foreign.write_text(json.dumps(api)); run('foreign-complete-api-pin', baseline, foreign, 2)
        api = json.loads(args.api_receipt.read_text()); api['case']['handlerBActivityExecutionId'] = 'foreign-handler'
        foreign = root / 'foreign-handler.json'; foreign.write_text(json.dumps(api)); run('foreign-original-handler-identity', baseline, foreign, 2)
        altered = root / 'association.sqlite'; shutil.copy2(baseline, altered)
        encode = lambda value: base64.b64encode(value.encode('utf-16-le')).decode()
        run('actual-handler-loses-scheduling-association', change_state(altered, 'node-handler-b', lambda w: w['provenance']['metadata'].__setitem__(encode('bpmn.elementId'), encode('foreign-handler'))), args.api_receipt, 2)
        orphan = root / 'orphan.sqlite'; shutil.copy2(baseline, orphan)
        run('owed-physical-orphan-hidden-behind-canonical-source', create_orphan(orphan), args.api_receipt, 2)
        if args.stage != 'Finished':
            altered = root / 'semantic-token.sqlite'; shutil.copy2(baseline, altered)

            def change_token(wire):
                state = wire['privateState']['value']['inlineValue']; field = 'activeChildren' if wire['privateState']['stateVersion'] == 1 else 'activeWork'
                state[field][0]['tokenId'] = 'foreign-semantic-token'

            run('actual-handler-loses-semantic-token', change_state(altered, 'compensation-bpmn', change_token), args.api_receipt, 2)
        if args.stage == 'HandlerB':
            altered = root / 'wrong-order.sqlite'; shutil.copy2(baseline, altered)
            run('actual-replay-pending-order-reversed', change_state(altered, 'compensation-bpmn', lambda w: w['privateState']['value']['inlineValue']['compensationRuns'][0]['pendingCompensableIds'].reverse()), args.api_receipt, 2)
    if args.database.read_bytes() != original:
        raise RuntimeError('The original actual compensation snapshot changed.')
    args.output.write_text(json.dumps({'passed': True, 'stage': args.stage, 'controls': records, 'originalSnapshotUnchanged': True}, indent=2) + '\n')
    print(f'{len(records)} actual compensation-reader controls passed without writes or raw diagnostics.')


if __name__ == '__main__':
    main()

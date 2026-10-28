#!/usr/bin/env python3
"""Verify portable pre-writer inputs; this is not upgraded-host recovery proof."""
import argparse
from contextlib import closing
import gzip
import hashlib
import json
from pathlib import Path
import sqlite3
import subprocess
import tempfile

FROZEN = '41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92'


def verify(path: Path, reader: Path) -> dict:
    root = path.resolve().parent
    manifest = json.loads(path.read_text())
    if manifest['schemaVersion'] != 1 or manifest['kind'] != 'actual-pre-writer-baseline-matrix':
        raise ValueError('Unknown baseline matrix.')
    if manifest['frozenAuthoringFingerprint'] != FROZEN:
        raise ValueError('The frozen authoring oracle changed.')
    assets = {}
    for asset in manifest['assets']:
        name = asset['path']; file = (root / name).resolve(strict=True)
        if Path(name).is_absolute() or file == root or not file.is_relative_to(root) or name in assets:
            raise ValueError('Baseline asset path escapes or repeats.')
        if hashlib.sha256(file.read_bytes()).hexdigest() != asset['sha256']:
            raise ValueError('Baseline asset hash differs: ' + name)
        assets[name] = asset

    def document(name):
        if name not in assets:
            raise ValueError('Receipt is not a verified asset.')
        return json.loads((root / name).read_text())

    with tempfile.TemporaryDirectory(prefix='elsa-baseline-matrix-') as temporary:
        databases = {}
        for name, asset in assets.items():
            if asset['role'] != 'whole-sqlite-store':
                continue
            raw = (root / name).read_bytes()
            if asset.get('encoding') == 'gzip':
                raw = gzip.decompress(raw)
            if hashlib.sha256(raw).hexdigest() != asset['databaseSha256']:
                raise ValueError('Whole-store bytes differ.')
            database = Path(temporary) / (str(len(databases)) + '.sqlite')
            database.write_bytes(raw)
            with closing(sqlite3.connect(database.as_uri() + '?mode=ro&immutable=1', uri=True)) as connection:
                if connection.execute('pragma integrity_check').fetchall() != [('ok',)]:
                    raise ValueError('Whole-store integrity failed.')
                tables = {row[0].lower() for row in connection.execute("select name from sqlite_master where type='table'")}
                if any(name.startswith(('identity_', 'openiddict', 'elsa_secret')) for name in tables):
                    raise ValueError('A retained store includes authentication or secrets.')
            databases[name] = database
        observed = 0
        for case in manifest['checkedObservations']:
            database = databases[case['database']]; sha = assets[case['database']]['databaseSha256']
            mode = case['mode']; api = document(case['api'])
            if mode == 'broadcast':
                if api['passed'] is not True or api['stopped']['snapshotSha256'] != sha:
                    raise ValueError('Broadcast receipt does not identify the retained input.')
                args = [str(database), sha, 'default', api['cut']['sourceOutboxItemId'],
                        *[item['workflowExecutionId'] for item in api['stopped']['workflows']]]
            elif mode == 'called-ack':
                args = ['--inspect-called-ack', str(database), sha, 'default', str(root / case['api']),
                        str(root / case['marker']), 'held']
                document(case['marker'])
            elif mode in ('called-control', 'compensation'):
                flag = '--inspect-opted-called' if mode == 'called-control' else '--inspect-compensation'
                args = [flag, str(database), sha, 'default', str(root / case['api']), case['phase']]
            else:
                raise ValueError('Unknown checked observation kind.')
            result = subprocess.run(['dotnet', str(reader.resolve()), *args], capture_output=True, text=True, timeout=60)
            value = json.loads(result.stdout)
            if result.returncode != 0 or value.get('providerChecked') is not True or value.get('readOnly') is not True:
                raise ValueError('The actual provider refused a retained observation.')
            if hashlib.sha256(database.read_bytes()).hexdigest() != sha:
                raise ValueError('The checked reader changed a retained input.')
            observed += 1
        terminals = document(manifest['nativeTerminalReceipt'])
        if terminals['passed'] is not True or len(terminals['cases']) != 12 or {
                (case['parent'], case['persistenceMode'], case['outcome']) for case in terminals['cases']} != {
                (parent, mode, outcome) for parent in ('Sequence', 'Flowchart', 'CompensationScope')
                for mode in ('Balanced', 'Durable') for outcome in ('Fault', 'Cancel')}:
            raise ValueError('Native terminal matrix is incomplete.')
        studio = document(manifest['studioReceipt'])
        frozen = document(studio['frozenContractReceipt'])
        run = document(studio['runReceipt'])
        exported = document(studio['exportReceipt'])
        if (frozen['oracleSha256'] != FROZEN or frozen['matchesFrozenFingerprint'] is not True or
                frozen['closedSnapshotSha256'] != assets[studio['designStore']]['databaseSha256'] or
                run['instance']['status'] != 'Completed' or run['instance']['artifactId'] != studio['artifactId'] or
                exported['definitionId'] != studio['definitionId'] or not exported.get('layout')):
            raise ValueError('Actual Studio/frozen-authoring baseline is incomplete.')
        for mapping in manifest['writerMap']:
            if not mapping['writers'] or not mapping['captures'] or any(name not in assets for name in mapping['captures']):
                raise ValueError('A writer has no retained source-bound capture.')
    return {'assetsVerified': len(assets), 'wholeStoresVerified': len(databases), 'checkedProviderObservations': observed,
            'nativeTerminalCases': 12, 'upgradeRecoveryProved': False, 'directHostAcceptanceProved': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--reader-assembly', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(verify(args.manifest, args.reader_assembly), indent=2))

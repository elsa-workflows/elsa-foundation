#!/usr/bin/env python3
"""Retain an unmodified coherent whole SQLite store as a closed snapshot.

The lifecycle caller owns the source host and any crash cut. Identity/secrets stores are
refused, rather than deleting their rows from a positive execution fixture.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sqlite3
from contextlib import closing
from inspect_broadcast_recovery import copy_snapshot, retain_snapshot


def capture(database: Path, destination: Path) -> dict:
    if not database.is_file():
        raise ValueError('The actual source store is missing.')
    if destination.exists():
        raise FileExistsError('An earlier retained input must not be overwritten.')
    # Connection backup includes committed WAL data without editing the source.
    import tempfile
    with tempfile.TemporaryDirectory(prefix='elsa-closed-hosting-store-') as root:
        snapshot = Path(root) / 'whole.sqlite'
        with closing(sqlite3.connect(database.resolve().as_uri() + '?mode=ro', uri=True)) as source:
            source.execute('pragma query_only=ON')
            tables = {row[0].lower() for row in source.execute("select name from sqlite_master where type='table'")}
            if any(name.startswith(('identity_', 'openiddict', 'elsa_secret')) for name in tables):
                raise ValueError('Authentication or secrets tables must be isolated before authoring the fixture.')
            copy_snapshot(source, snapshot)
        result = retain_snapshot(snapshot, destination)
        result.update({'wholeStore': True, 'storedExecutionRowsEdited': False, 'identityTablesAbsent': True,
                       'snapshotSha256': hashlib.sha256(snapshot.read_bytes()).hexdigest()})
        return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--database', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(capture(args.database, args.output)))

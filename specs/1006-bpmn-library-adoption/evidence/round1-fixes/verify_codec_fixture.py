#!/usr/bin/env python3
"""Verify the embedded codec regression payload against its unchanged actual old-writer module backup."""
from pathlib import Path
from contextlib import closing
import base64
import gzip
import hashlib
import json
import sqlite3
import tempfile

repo = Path(__file__).resolve().parents[4]
fixture = repo / 'Essentials/tests/Activities/Bpmn/Tests/Fixtures/LegacyCompensationState.json'
view = json.loads(fixture.read_text())
source = (repo / view['sourceDatabase']).resolve()
if not source.is_relative_to(repo):
    raise ValueError('The declared source must remain in the repository.')
compressed = source.read_bytes()
raw = gzip.decompress(compressed)
if hashlib.sha256(compressed).hexdigest() != view['sourceCompressedSha256'] or hashlib.sha256(raw).hexdigest() != view['sourceDatabaseSha256']:
    raise ValueError('The original old-writer source differs from its declared hashes.')
with tempfile.TemporaryDirectory(prefix='elsa-codec-fixture-') as directory:
    database = Path(directory) / 'original.sqlite'
    database.write_bytes(raw)
    with closing(sqlite3.connect(database.as_uri() + '?mode=ro&immutable=1', uri=True)) as connection:
        if connection.execute('pragma integrity_check').fetchone() != ('ok',):
            raise ValueError('The source module backup failed its integrity check.')
        states = [json.loads(row[0]) for row in connection.execute('SELECT ContentJson FROM elsa_runtime_activity_execution_state')]
    decode = lambda value: base64.b64decode(value).decode('utf-16-le')
    matches = [state for state in states if decode(state['execution']['executableNodeId']) == 'compensation-bpmn']
    if len(matches) != 1 or matches[0]['privateState']['stateVersion'] != view['stateVersion'] or matches[0]['privateState']['value']['inlineValue'] != view['payload']:
        raise ValueError('The embedded regression payload is not the exact recorded old-writer payload.')
if source.read_bytes() != compressed:
    raise ValueError('The original module backup changed.')
print('Embedded codec fixture equals the actual old-writer payload; original compressed module unchanged.')

"""Package a real current-writer delayed-compensation capture; never alter archived originals.

Run the existing Native_cancellation_settles_before_an_obsolete_compensation_retry_becomes_due
test with ELSA_BPMN_DELAYED_COMPENSATION_EVIDENCE naming a new disposable directory first.
The output is a current-writer restart witness, not an authentic old-writer upgrade proof.
"""
import argparse
import gzip
import hashlib
import json
import re
from pathlib import Path
import sqlite3
import subprocess


def sha(data):
    return hashlib.sha256(data).hexdigest()


def verify_capture_binding(data, report):
    provenance = report.get('captureProvenance')
    if not isinstance(provenance, dict) or provenance.get('version') != 1 or provenance.get('sourceDirty') is not False:
        raise ValueError('Recapture with the capture-time source/binary/database receipt enabled')
    source = provenance.get('sourceSha', '')
    if not re.fullmatch('[0-9a-f]{40}', source):
        raise ValueError('Capture source must name a full commit')
    if provenance.get('databaseSha256') != sha(data) or provenance.get('databaseByteLength') != len(data):
        raise ValueError('Database differs from its capture-time receipt')
    names = {'Elsa.Activities.Bpmn.Tests.dll', 'Elsa.Activities.Bpmn.dll', 'Elsa.Workflows.Runtime.Core.dll',
             'Elsa.Workflows.Runtime.dll', 'Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll', 'Bpmn.Model.dll', 'Bpmn.Semantics.dll'}
    hashes, versions = provenance.get('binaryHashes', {}), provenance.get('binaryVersions', {})
    if set(hashes) != names or set(versions) != names or any(not re.fullmatch('[0-9a-f]{64}', value) for value in hashes.values()):
        raise ValueError('Capture binary inventory is incomplete')
    if not versions['Elsa.Activities.Bpmn.Tests.dll'].endswith('+' + source):
        raise ValueError('The executing capture binary does not identify its claimed source')
    return provenance


def record(repo, capture, output):
    repo, capture, output = repo.resolve(), capture.resolve(), output.resolve()
    if output.exists():
        raise ValueError('Use a new output directory; retained captures are never overwritten')
    if subprocess.check_output(['git', 'status', '--porcelain'], cwd=repo, text=True).strip():
        raise ValueError('The capture writer checkout must be clean')
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=repo, text=True).strip()
    database = capture / 'queued-result.sqlite'
    data = database.read_bytes()
    report_bytes = (capture / 'queued-result.json').read_bytes()
    report = json.loads(report_bytes)
    provenance = verify_capture_binding(data, report)
    subprocess.run(['git', 'cat-file', '-e', provenance['sourceSha'] + '^{commit}'], cwd=repo, check=True)
    with sqlite3.connect(f'file:{database.as_posix()}?mode=ro', uri=True) as connection:
        if connection.execute('PRAGMA integrity_check').fetchall() != [('ok',)]:
            raise ValueError('Captured database integrity check failed')
        if connection.execute('PRAGMA foreign_key_check').fetchall():
            raise ValueError('Captured database has a foreign-key violation')
        tables = connection.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").fetchall()
        counts = {name: connection.execute('SELECT count(*) FROM "' + name.replace('"', '""') + '"').fetchone()[0] for (name,) in tables}
    original = repo / 'specs/1017-direct-bpmn-hosting/evidence/reviews/round-10/corrections/delayed-retry-original/queued-result.sqlite.gz'
    original_bytes = original.read_bytes()
    original_database_sha = sha(gzip.decompress(original_bytes))
    if original_database_sha != 'dedc7041baf31ad6d0b3de251049de7d55ff421dbe7e65e3059b6e35f9bf9e63':
        raise ValueError('The archived historical input differs from its pinned original')
    compressed = gzip.compress(data, compresslevel=9, mtime=0)
    receipt = dict(spec=1207, kind='current-writer-retained-state', writerSourceSha=provenance['sourceSha'], packagingSourceSha=head,
                   captureTest='Elsa.Activities.Bpmn.Tests.BpmnDirectExecutionTests.Native_cancellation_settles_before_an_obsolete_compensation_retry_becomes_due',
                   capturedAt=report['capturedAt'], executionId=report['model'], phase='queued-result',
                   databaseSha256=sha(data), compressedSha256=sha(compressed), byteLength=len(data), compressedByteLength=len(compressed),
                   binaries=provenance['binaryHashes'], binaryVersions=provenance['binaryVersions'],
                   captureReportSha256=sha(report_bytes), rowCounts=counts,
                   historicalOriginal=dict(path=original.relative_to(repo).as_posix(), compressedSha256=sha(original_bytes), databaseSha256=original_database_sha,
                       proof='Historical writer/source-bound claims remain attached to the unchanged spec1017 archive, not this current recapture.'),
                   replayGoal='Restart with new admission disabled; settle the queued cancelled result while preserving native worker state, obsolete retry, material incarnation and execution authority.')
    output.mkdir(parents=True)
    (output / 'delayed-compensation.sqlite.gz').write_bytes(compressed)
    (output / 'delayed-compensation.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({key: receipt[key] for key in ['writerSourceSha', 'executionId', 'databaseSha256', 'compressedSha256']}))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--capture', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    record(args.repo, args.capture, args.output)

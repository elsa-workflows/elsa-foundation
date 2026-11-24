"""Package complete current-writer concurrent-fault stores; historical bug cuts remain historical."""
import argparse
import base64
from datetime import datetime, timezone
import gzip
import hashlib
import json
from pathlib import Path
import re
import sqlite3
import struct
import subprocess

WRITERS = {'Elsa.Activities.Bpmn.Tests.dll', 'Elsa.Activities.Bpmn.dll', 'Elsa.Workflows.Runtime.Core.dll',
           'Elsa.Workflows.Runtime.dll', 'Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll',
           'Elsa.Serialization.Core.dll', 'Elsa.Activities.Runtime.Core.dll', 'Bpmn.Model.dll', 'Bpmn.Semantics.dll'}
ORIGINALS = {
    'concurrent-unadmitted': ('concurrent-terminal.sqlite.gz', '5676f8bd43eefe98333e97a6c231bc7f28d777656de58c71941cfe457e582170', 'bd663a394a582994c07059aa2e9efdeb74984b4a140ec2051d5908e65a3be5fe'),
    'concurrent-admitted': ('concurrent-admitted-terminal.sqlite.gz', 'ae0027de22b16b04912106623724207d493879fc96f195e61e87a42af9a1d683', 'd148588618d8f7ac06bc980caf561b35945de511115d0f6dd1a9dfe6774e1bc0'),
}
STAGES = ['initial-requested', 'pending-settlement', 'settled-after-restart']


def sha(value):
    return hashlib.sha256(value).hexdigest()


def verify(data, report):
    receipt = report.get('captureProvenance', {})
    if receipt.get('version') != 1 or receipt.get('sourceDirty') is not False or re.fullmatch('[0-9a-f]{40}', receipt.get('sourceSha', '')) is None:
        raise ValueError('Recapture with the clean source/binary/database receipt enabled')
    if receipt.get('databaseSha256') != sha(data) or receipt.get('databaseByteLength') != len(data):
        raise ValueError('Database differs from its capture-time receipt')
    if set(receipt.get('binaryHashes', {})) != WRITERS or set(receipt.get('binaryVersions', {})) != WRITERS:
        raise ValueError('The complete native, canonical and schema hash writer inventory must be pinned')
    hashes, versions = receipt['binaryHashes'], receipt['binaryVersions']
    for name, digest in hashes.items():
        if re.fullmatch('[0-9a-f]{64}', digest) is None:
            raise ValueError('Invalid executing binary digest: ' + name)
        if name.startswith('Elsa.') and not versions[name].endswith('+' + receipt['sourceSha']):
            raise ValueError('Executing first-party writer does not identify the clean source: ' + name)
    return receipt


def instant(value):
    """Exact .NET ticks; Python datetime alone discards the seventh fractional digit."""
    match = re.fullmatch(r'(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})', value)
    if match is None:
        raise ValueError('Settlement timestamps require explicit-offset instants')
    date = datetime.fromisoformat(match[1] + match[3].replace('Z', '+00:00')).astimezone(timezone.utc)
    return ((date.toordinal() * 86400 + date.hour * 3600 + date.minute * 60 + date.second) * 10_000_000
            + int((match[2] or '').ljust(7, '0')))


def verify_history(scenario, reports, stores):
    initial, pending, settled = reports
    for field in ['executionId', 'scope', 'protocol', 'executable', 'materialIncarnationId', 'authority']:
        if not (initial['state'][field] == pending['state'][field] == settled['state'][field]):
            raise ValueError('A captured authority/identity changed across restart: ' + field)
    if [report['state']['lifecycle'] for report in reports] != [0, 2, 3] or [report['state']['revision'] for report in reports] != [1, 2, 3]:
        raise ValueError('The current concurrent-fault cuts are incomplete')
    first, debt, final = [report['state']['payload'] for report in reports]
    origins = [work['nativeWork'] for work in first['work']]
    if len(origins) != 2 or any(len({origin[field] for origin in origins}) != 2 for field in ['invocationId', 'attemptId', 'workerExecutionId', 'sourceOutboxItemId']):
        raise ValueError('Both distinct original physical starts must be retained')
    if initial['state']['terminalResult'] is not None or initial['native']:
        raise ValueError('The initial cut must precede native admission')
    if pending['state']['terminalResult'].get('status') != 'Faulted' or pending['state']['terminalResult'] != settled['state']['terminalResult'] or debt['state'] != final['state']:
        raise ValueError('Recovery changed or omitted the retained fault decision')
    if debt['work'] or final['work'] or len(debt.get('nativeResults', [])) != 1 or len(debt.get('cancellations', [])) != 1 or len(final.get('cancellations', [])) != 1:
        raise ValueError('Pending fault settlement must retain its exact cancellation debt')
    result, cancellation, completion = debt['nativeResults'][0], debt['cancellations'][0], final['cancellations'][0]
    if result['origin'] not in origins or cancellation['origin'] not in origins or result['origin'] == cancellation['origin']:
        raise ValueError('The fault and owed cancellation must bind the two original starts')
    if cancellation.get('settled') is not False or completion.get('settled') is not True or cancellation['origin'] != completion['origin'] or cancellation['sourceOutboxItemId'] != completion['sourceOutboxItemId']:
        raise ValueError('Recovery lost or rebound the original cancellation source')
    admitted = scenario == 'concurrent-admitted'
    if bool(completion.get('notAdmitted')) == admitted or len(pending['native']) != (2 if admitted else 1) or pending['native'] != settled['native']:
        raise ValueError('The admitted and unadmitted physical worker histories differ from their claimed cuts')
    expected_workers = {origin['workerExecutionId'] for origin in origins} if admitted else {result['origin']['workerExecutionId']}
    if {worker['workflowExecutionId'] for worker in pending['native']} != expected_workers or result['status'] != 4 or any(worker['status'] != 4 for worker in pending['native']):
        raise ValueError('The captured native fault must belong to the originally admitted workers')
    if len(final['nativeResults']) != (2 if admitted else 1) or any(item['origin'] not in origins for item in final['nativeResults']) or result not in final['nativeResults']:
        raise ValueError('The original fault result was lost or an unrelated worker result was accepted')
    for origin in origins:
        source = origin['sourceOutboxItemId']
        if stores[0]['outbox'][source]['status'] != 'Pending' or stores[2]['outbox'][source]['status'] != 'Delivered' or not stores[2]['outbox'][source].get('deliveredAt'):
            raise ValueError('Original starts lack their actual pending and delivered sources')
    source = cancellation['sourceOutboxItemId']
    if stores[1]['outbox'][source]['status'] == 'Delivered' or stores[1]['outbox'][source].get('deliveredAt') or stores[2]['outbox'][source]['status'] != 'Delivered' or not stores[2]['outbox'][source].get('deliveredAt'):
        raise ValueError('Cancellation was acknowledged before or without physical settlement')
    command = cancellation['origin']['resultInputId']
    if not admitted:
        frame = b''
        for part in [cancellation['origin']['sourceOutboxItemId'], source]:
            encoded = part.encode('utf-16-le')
            frame += struct.pack('<i', len(encoded) // 2) + encoded
        command = 'native-start-retired:' + sha(frame)
    if stores[2]['inputs'].get(command, {}).get('result', {}).get('status') != 'Accepted':
        raise ValueError('Physical cancellation lacks its original result or not-admitted retirement receipt')
    accepted = instant(stores[2]['inputs'][command]['acceptedAt'])
    cancel_ack = instant(stores[2]['outbox'][source]['deliveredAt'])
    if cancel_ack < accepted:
        raise ValueError('Cancellation ACK precedes its physical accepted receipt')
    if not admitted:
        retired_start_ack = instant(stores[2]['outbox'][cancellation['origin']['sourceOutboxItemId']]['deliveredAt'])
        if retired_start_ack < accepted or cancel_ack < retired_start_ack:
            raise ValueError('Not-admitted start/cancel ACK order precedes physical retirement')


def record(repo, capture, output):
    repo, capture, output = repo.resolve(), capture.resolve(), output.resolve()
    if output.exists():
        raise ValueError('Use a new output directory; captures are never overwritten')
    if subprocess.check_output(['git', 'status', '--porcelain'], cwd=repo, text=True).strip():
        raise ValueError('The packaging checkout must be clean')
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=repo, text=True).strip()
    records = []
    for scenario, (original_name, original_gz, original_raw) in ORIGINALS.items():
        original = repo / 'specs/1017-direct-bpmn-hosting/evidence/reviews/round-3/correctness/legacy-checkpoint' / original_name
        archived = original.read_bytes()
        if sha(archived) != original_gz or sha(gzip.decompress(archived)) != original_raw:
            raise ValueError('Historical original differs from its independently pinned archive: ' + scenario)
        reports, stores = [], []
        for stage in STAGES:
            database = capture / scenario / (stage + '.sqlite')
            data = database.read_bytes()
            report_bytes = database.with_suffix('.json').read_bytes()
            report = json.loads(report_bytes)
            provenance = verify(data, report)
            if report['scenario'] != scenario or report['stage'] != stage:
                raise ValueError('Capture phase identity mismatch')
            subprocess.run(['git', 'cat-file', '-e', provenance['sourceSha'] + '^{commit}'], cwd=repo, check=True)
            if any(Path(str(database) + suffix).exists() for suffix in ['-wal', '-shm', '-journal']):
                raise ValueError('Closed backup must not depend on unbound SQLite sidecars')
            with sqlite3.connect(database.as_uri() + '?mode=ro&immutable=1', uri=True) as connection:
                if connection.execute('PRAGMA integrity_check').fetchall() != [('ok',)] or connection.execute('PRAGMA foreign_key_check').fetchall():
                    raise ValueError('Captured complete store is corrupt')
                names = [row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")]
                counts = {name: connection.execute('SELECT count(*) FROM "' + name.replace('"', '""') + '"').fetchone()[0] for name in names}
                physical = {}
                for label, table, field in [('outbox', 'elsa_runtime_post_commit_outbox', 'outboxItemId'), ('inputs', 'elsa_runtime_model_input', 'inputId')]:
                    rows = [json.loads(row[0]) for row in connection.execute('SELECT ContentJson FROM ' + table)]
                    physical[label] = {base64.b64decode(row[field], validate=True).decode('utf-16-le'): row for row in rows}
                stores.append(physical)
            compressed = gzip.compress(data, compresslevel=9, mtime=0)
            receipt = dict(spec=1207, kind='current-writer-concurrent-fault', scenario=scenario, stage=stage,
                           writerSourceSha=provenance['sourceSha'], packagingSourceSha=head, captureReportSha256=sha(report_bytes),
                           packagerSha256=sha(Path(__file__).read_bytes()), byteLength=len(data), databaseSha256=sha(data),
                           compressedSha256=sha(compressed), compressedByteLength=len(compressed), rowCounts=counts,
                           binaries=provenance['binaryHashes'], binaryVersions=provenance['binaryVersions'],
                           historicalOriginal=dict(path=original.relative_to(repo).as_posix(), compressedSha256=original_gz, databaseSha256=original_raw),
                           proof='Current writer and cold restart; not a historical late-start upgrade witness. Original fixed-bug state is retained separately with precise refusal.')
            records.append((scenario, stage, compressed, report_bytes, receipt))
            reports.append(report)
        initial = reports[0]
        verify_history(scenario, reports, stores)
        if any(report['captureProvenance'] != initial['captureProvenance'] and
               (report['captureProvenance']['sourceSha'], report['captureProvenance']['binaryHashes']) !=
               (initial['captureProvenance']['sourceSha'], initial['captureProvenance']['binaryHashes']) for report in reports):
            raise ValueError('Capture stages used different writer binaries')
    output.mkdir(parents=True)
    for scenario, stage, compressed, report, receipt in records:
        target = output / scenario
        target.mkdir(exist_ok=True)
        (target / (stage + '.sqlite.gz')).write_bytes(compressed)
        (target / (stage + '.capture.json')).write_bytes(report)
        (target / (stage + '.json')).write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(stores=len(records), packagingSourceSha=head)))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--capture', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    record(args.repo, args.capture, args.output)

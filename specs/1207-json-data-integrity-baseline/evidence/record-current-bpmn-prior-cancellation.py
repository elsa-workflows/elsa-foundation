"""Bind complete current SDK/store prior-cancellation cuts; never rewrite the historical witness."""
import argparse
import base64
import gzip
import importlib.util
import json
from pathlib import Path
import sqlite3
import subprocess

_spec = importlib.util.spec_from_file_location('fault_capture', Path(__file__).with_name('record-current-bpmn-fault-fixtures.py'))
fault = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(fault)
sha = fault.sha
STAGES = ['pending-outer-result', 'deferred-original-result', 'settled-after-restart']
WRITERS = fault.WRITERS | {'Elsa.Activities.Bpmn.Core.dll', 'Elsa.Activities.Scheduling.dll',
                         'Elsa.Workflows.Runtime.Scheduling.dll', 'Elsa.Activities.Testing.dll', 'Elsa.Serialization.SystemText.dll'}
HISTORICAL = 'specs/1017-direct-bpmn-hosting/evidence/reviews/round-9/correctness/prior-cancellation/deliver-worker-result.sqlite.gz'
HISTORICAL_GZIP = '87a7a76fdc45f7cc7c2c0a4f1ac94361c8108a33c1cc1633cafcc49f7487773c'
HISTORICAL_RAW = 'f19abce8444b42284483f511018039a48e39dd4d9cd6a53ab348a55191f4ecb4'


def encoded(value):
    if isinstance(value, str):
        return base64.b64encode(value.encode('utf-16-le')).decode('ascii')
    if isinstance(value, dict):
        return {key: encoded(item) for key, item in value.items()}
    return value


def decoded(value):
    return base64.b64decode(value, validate=True).decode('utf-16-le')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def inspect(database, report):
    require(not any(Path(str(database) + suffix).exists() for suffix in ['-wal', '-shm', '-journal']), 'Unbound SQLite sidecars')
    with sqlite3.connect(database.as_uri() + '?mode=ro&immutable=1', uri=True) as connection:
        connection.row_factory = sqlite3.Row
        require([tuple(row) for row in connection.execute('PRAGMA integrity_check')] == [('ok',)], 'Corrupt SQLite capture')
        require(not connection.execute('PRAGMA foreign_key_check').fetchall(), 'Foreign-key violation')
        tables = [row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")]
        counts = {name: connection.execute('SELECT count(*) FROM "' + name.replace('"', '""') + '"').fetchone()[0] for name in tables}
        stores = {}
        for label, table, key in [('models', 'elsa_runtime_model_execution', 'executionId'),
                                  ('inputs', 'elsa_runtime_model_input', 'inputId'),
                                  ('outbox', 'elsa_runtime_post_commit_outbox', 'outboxItemId'),
                                  ('native', 'elsa_runtime_workflow_execution_state', 'workflowExecutionId')]:
            rows = [json.loads(row[0]) for row in connection.execute('SELECT ContentJson FROM ' + table)]
            require(len({row[key] for row in rows}) == len(rows), 'Duplicate physical capture identity: ' + label)
            stores[label] = {decoded(row[key]): row for row in rows}
        outer = report['observations']['outer']
        stores['queue'] = [dict(row) for row in connection.execute('SELECT * FROM elsa_runtime_scheduler_work_item')
                           if json.loads(row['ContentJson'])['workflowExecutionId'] == encoded(outer)]
    for state in [report['state'], report['observations']['innerState']]:
        physical = stores['models'][state['executionId']]
        for field in ['scope', 'executionId', 'tenantId', 'protocol', 'executable', 'materialIncarnationId', 'authority']:
            require(physical[field] == encoded(state[field]), 'Physical model identity differs from report: ' + field)
        for field in ['revision', 'createdAt', 'updatedAt', 'payload', 'terminalResult']:
            require(physical[field] == state[field], 'Physical model state differs from report: ' + field)
        require(physical['lifecycle'] == {0: 'Running', 2: 'TerminalPendingSettlement', 3: 'TerminalSettled'}[state['lifecycle']], 'Physical lifecycle differs')
    require(set(stores['native']) == {state['workflowExecutionId'] for state in report['native']}, 'Physical native workers differ')
    for state in report['native']:
        physical = stores['native'][state['workflowExecutionId']]
        for field in ['pinnedExecutable', 'parentWorkflowExecutionId', 'tenantId', 'partition', 'authority']:
            require(physical[field] == encoded(state[field]), 'Physical native identity differs: ' + field)
    return stores, counts


def verify_history(reports, stores):
    first, deferred, final = reports
    original = first['observations']
    for report in reports:
        require(report['scenario'] == 'prior-scope-cancellation', 'Wrong capture scenario')
        for field in ['outer', 'middle', 'inner', 'source']:
            require(report['observations'][field] == original[field], 'Rebound cancellation identity: ' + field)
        require(report['executionId'] == original['outer'] == report['state']['executionId'], 'Wrong outer execution')
        require({state['workflowExecutionId'] for state in report['native']} == {original['middle'], first['observations']['innerState']['payload']['cancellations'][0]['origin']['workerExecutionId']}, 'Original native workers changed')
        for field in ['scope', 'protocol', 'executable', 'materialIncarnationId', 'authority']:
            require(report['state'][field] == first['state'][field], 'Outer authority or pin changed: ' + field)
            require(report['observations']['innerState'][field] == original['innerState'][field], 'Inner authority or pin changed: ' + field)
    require(first['state'] == deferred['state'] and first['state']['lifecycle'] == 0, 'Deferral changed the outer checkpoint')
    require([r['observations']['innerState']['lifecycle'] for r in reports] == [2, 2, 3], 'Earlier descendant cancellation was not pending then settled')
    require(final['state']['lifecycle'] == 3 and final['state']['terminalResult'] == {'status': 'Cancelled'}, 'Outer did not settle as cancelled')
    require(len(first['modelQueue']) == 1 and first['modelQueue'] == deferred['modelQueue'] and not final['modelQueue'], 'The exact original result must remain queued until settlement')
    command = first['modelQueue'][0]
    origin = first['state']['payload']['work'][0]['nativeWork']
    require(command['commandId'] == origin['resultInputId'] and command['payload']['payload']['origin'] == origin, 'Queued result does not belong to original physical start')
    require(command['payload']['executable'] == first['state']['executable'] and command['payload']['materialIncarnationId'] == first['state']['materialIncarnationId'], 'Queued result pin differs')
    for index in [0, 1]:
        require(command['commandId'] not in stores[index]['inputs'], 'Original result was accepted before earlier cancellation settled')
        require(len(stores[index]['queue']) == 1, 'Physical original queue item is missing or duplicated')
        physical = json.loads(stores[index]['queue'][0]['ContentJson'])
        for field in ['workItemId', 'workflowExecutionId', 'commandId', 'envelopeId', 'idempotencyKey']:
            require(physical[field] == encoded(command[field]), 'Physical queued identity differs: ' + field)
        require(physical['payload'] == command['payload'], 'Physical queued payload differs')
        source = stores[index]['outbox'][original['source']]
        require(source['status'] == 'FailedRetryable' and source['deliveredAt'] is None, 'Earlier cancellation was acknowledged before settlement')
    pending_claim, claim = [store['queue'][0] for store in stores[:2]]
    require(pending_claim['ClaimToken'] == 0 and pending_claim['ClaimOwnerId'] is None, 'Initial original result must be unclaimed')
    require(claim['Id'] == pending_claim['Id'] and claim['ClaimToken'] == 1 and claim['ClaimOwnerId'] is not None and
            claim['VisibleAfterUtcTicks'] - claim['ClaimedAtUtcTicks'] == 60 * 10_000_000, 'Deferral must retain its actual one-minute positive claim')
    require(not stores[2]['queue'], 'Final original result remains queued')
    accepted = stores[2]['inputs'][command['commandId']]
    require(accepted['result']['status'] == 'Accepted' and accepted['executionId'] == encoded(original['outer']), 'Original result lacks its accepted physical receipt')
    # .NET ticks start at ordinal day one; the helper uses ordinal day one at 86400 seconds.
    require(fault.instant(accepted['acceptedAt']) - 86400 * 10_000_000 >= claim['VisibleAfterUtcTicks'], 'Recovery bypassed the original positive claim expiry')
    cancellation = stores[2]['outbox'][original['source']]
    require(cancellation['status'] == 'Delivered' and cancellation['deliveredAt'], 'Original cancellation source was not delivered')
    require(fault.instant(cancellation['deliveredAt']) <= fault.instant(accepted['acceptedAt']), 'Outer result was accepted before original cancellation ACK')
    debt = original['innerState']['payload']['cancellations']
    completed = final['observations']['innerState']['payload']['cancellations']
    require(len(debt) == len(completed) == 1 and debt[0]['settled'] is False and completed[0] == dict(debt[0], settled=True), 'Earlier physical descendant debt changed or did not settle')
    require(final['state']['payload']['nativeResults'][0]['origin'] == origin, 'Settlement rebound the original result origin')


def record(repo, capture, output):
    repo, capture, output = repo.resolve(), capture.resolve(), output.resolve()
    require(not output.exists(), 'Use a new output directory; captures are never overwritten')
    require(not subprocess.check_output(['git', 'status', '--porcelain'], cwd=repo, text=True).strip(), 'Packaging checkout must be clean')
    head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=repo, text=True).strip()
    historical = (repo / HISTORICAL).read_bytes()
    require(sha(historical) == HISTORICAL_GZIP and sha(gzip.decompress(historical)) == HISTORICAL_RAW, 'Historical original changed')
    recipes = {}
    for file in [Path(__file__).resolve(), Path(fault.__file__).resolve()]:
        relative = file.relative_to(repo).as_posix()
        committed = subprocess.check_output(['git', 'show', head + ':' + relative], cwd=repo)
        require(committed.replace(b'\r\n', b'\n') == file.read_bytes().replace(b'\r\n', b'\n'), 'Commit exact packager source before recording')
        recipes[relative] = sha(committed)
    records, reports, stores = [], [], []
    for stage in STAGES:
        database = capture / (stage + '.sqlite')
        data, report_bytes = database.read_bytes(), database.with_suffix('.json').read_bytes()
        report = json.loads(report_bytes)
        provenance = fault.verify(data, report, WRITERS)
        require(report['stage'] == stage, 'Wrong capture stage')
        subprocess.run(['git', 'cat-file', '-e', provenance['sourceSha'] + '^{commit}'], cwd=repo, check=True)
        physical, counts = inspect(database, report)
        reports.append(report)
        stores.append(physical)
        compressed = gzip.compress(data, compresslevel=9, mtime=0)
        receipt = dict(spec=1207, kind='current-writer-prior-scope-cancellation', stage=stage, writerSourceSha=provenance['sourceSha'],
                       packagingSourceSha=head, verifierSourceHashes=recipes, captureReportSha256=sha(report_bytes),
                       databaseSha256=sha(data), byteLength=len(data), compressedSha256=sha(compressed), compressedByteLength=len(compressed),
                       rowCounts=counts, binaries=provenance['binaryHashes'], binaryVersions=provenance['binaryVersions'],
                       historicalOriginal=dict(path=HISTORICAL, compressedSha256=HISTORICAL_GZIP, databaseSha256=HISTORICAL_RAW),
                       proof='Current SDK/store cold restart with actual Delay descendant and real claim expiry; not historical ETL Workbench process-loss coverage.')
        records.append((stage, compressed, report_bytes, receipt))
    require(all((report['captureProvenance']['sourceSha'], report['captureProvenance']['binaryHashes']) ==
                (reports[0]['captureProvenance']['sourceSha'], reports[0]['captureProvenance']['binaryHashes']) for report in reports), 'Writer binaries changed between cuts')
    verify_history(reports, stores)
    output.mkdir(parents=True)
    for stage, compressed, report, receipt in records:
        (output / (stage + '.sqlite.gz')).write_bytes(compressed)
        (output / (stage + '.capture.json')).write_bytes(report)
        (output / (stage + '.json')).write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(stores=len(records), packagingSourceSha=head)))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--capture', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    record(args.repo, args.capture, args.output)

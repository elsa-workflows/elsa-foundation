"""Read-only proof of real Event-worker cancellation and its sibling settlement."""
import argparse
from contextlib import closing
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile

spec = importlib.util.spec_from_file_location('signal_proof', Path(__file__).with_name('verify_signal_recovery.py'))
v = importlib.util.module_from_spec(spec); spec.loader.exec_module(v); require = v.require


def verify(path):
    manifest = json.loads(path.read_text()); root = path.parent
    require(manifest['schemaVersion'] == 1 and manifest['kind'] == 'actual-event-worker-sibling-cancellation', 'The sibling cancellation receipt is unsupported.')
    for asset in manifest['assets']:
        name = Path(asset['path']); require(not name.is_absolute() and '..' not in name.parts and v.digest(root / name) == asset['sha256'], 'An actual sibling cancellation asset changed.')
    require({a['path'] for a in manifest['assets']} == {str(p.relative_to(root)) for p in root.rglob('*') if p.is_file() and p.name not in ('manifest.json', 'README.md')},
        'An actual sibling cancellation asset is missing or unreported.')
    baseline = Path(__file__).resolve().parents[2] / 'specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json'
    require(v.digest(baseline) == manifest['baselineManifestSha256'] and all(v.digest(baseline.parent / a['path']) == a['sha256'] for a in json.loads(baseline.read_text())['assets']), 'The frozen baseline changed.')
    read = lambda phase, name: json.loads((root / phase / name).read_text())
    process = read('fixed', 'process.json'); before = read('fixed', 'before.json'); after = read('fixed', 'after.json')
    require(process['sourceRevision'] == manifest['fixedSource'] and process['restoredFromActualSource'] == manifest['inputSource'] and not process['stateSeeded'] and
        len(process['processes']) == len(process['exits']) == 3 and len({p['processId'] for p in process['processes']}) == 3 and
        len({p['controllerProcessId'] for p in process['processes']}) == 1 and all(code == 137 for code in process['exits']) and
        all(item['productVersion'].endswith('+' + manifest['fixedSource']) if item['file'].startswith('Elsa.') else item['productVersion'].startswith('0.5.0+') for item in process['binaries']),
        'The actual sibling journey lost its source or process-loss identities.')
    require(before['status'] == 'Running' and before['revision'] == 1 and after['model']['status'] == 'TerminalSettled' and after['model']['revision'] == 3 and
        after['model']['result'] == {'status': 'Cancelled'} and all(item['instance']['status'] == 'Cancelled' for item in after['native']) and
        after['model'] == read('fixed', 'retained.json') and len(process['workers']) == 2 and process['cancelledWorker'] in process['workers'],
        'The model or an actual sibling did not retain its complete cancellation.')
    original = root.parent / 'model-broadcast/empty-cohort'
    require(len(process['inputs']) == 2 and all(v.digest(original / item['file']) == item['sha256'] for item in process['inputs']), 'The actual captured waiting inputs changed.')
    red = read('red', 'after.json'); failure = read('red', 'failure.json')
    require(failure['sourceRevision'] == manifest['redSource'] and failure['qualifiesJourney'] is False and red['model']['revision'] == 2 and
        red['model']['status'] == 'TerminalSettled' and sorted(x['instance']['status'] for x in red['native']) == ['Cancelled', 'Running'],
        'The original confirmed orphan was hidden or relabelled as passing.')
    require([s['file'] for s in process['stores']] == [stage + '-' + store + '.sqlite.gz' for stage in ('waiting', 'finished', 'retained') for store in ('runtime', 'design')],
        'A coupled sibling cancellation store is missing.')
    final_model = None; originals = None
    for entry in process['stores']:
        name = entry['file']; require(v.digest(root / 'fixed' / name) == entry['gzipSha256'], 'A compressed sibling store changed.')
        raw = gzip.decompress((root / 'fixed' / name).read_bytes()); require(hashlib.sha256(raw).hexdigest() == entry['databaseSha256'], 'A whole sibling store changed.')
        with tempfile.TemporaryDirectory(prefix='elsa-sibling-proof-') as temporary:
            database = Path(temporary) / 'snapshot.sqlite'; database.write_bytes(raw)
            with closing(sqlite3.connect(database.as_uri() + '?mode=ro&immutable=1', uri=True)) as connection:
                require(connection.execute('PRAGMA integrity_check').fetchall() == [('ok',)], 'A sibling cancellation store is corrupt.')
                if not name.endswith('-runtime.sqlite.gz'): continue
                row, = connection.execute('SELECT Revision,Lifecycle,ContentJson FROM elsa_runtime_model_execution').fetchall(); model = json.loads(row[2]); waiting = name.startswith('waiting-')
                require(row[:2] == ((1, 0) if waiting else (3, 3)) and model['executionId'] == v.key(process['modelExecutionId']), 'The physical sibling model lost its exact lifecycle.')
                if waiting:
                    origins = [w['nativeWork'] for w in model['payload']['work']]; originals = origins
                    require(len(origins) == 2 and model['payload']['formatVersion'] == 2, 'The original actual wait was not preserved.')
                else:
                    if final_model is None: final_model = model
                    else: require(model == final_model, 'The physical cancellation changed after another cold process.')
                    cancellation, = model['payload']['cancellations']; results = model['payload']['nativeResults']; origins = [r['origin'] for r in results]
                    require(model['payload']['formatVersion'] == 4 and not model['payload']['work'] and model['terminalResult'] == {'status': 'Cancelled'} and
                        len(results) == 2 and all(r['status'] == 5 and r['completion'] is None and r['faultCode'] is None for r in results) and cancellation['settled'] and
                        cancellation['origin']['workerExecutionId'] != process['cancelledWorker'] and all(origin in originals for origin in origins),
                        'The physical sibling cancellation obligation or original native truth changed.')
                    receipts = [json.loads(r[0]) for r in connection.execute('SELECT ContentJson FROM elsa_runtime_model_input')]
                    consumed = [r for r in receipts if r['inputKind'] == v.key('native-result')]
                    require(len(consumed) == 2 and {r['acceptedRevision'] for r in consumed} == {2, 3} and
                        next(r for r in consumed if r['acceptedRevision'] == 2)['result']['lifecycle'] == 'TerminalPendingSettlement' and
                        next(r for r in consumed if r['acceptedRevision'] == 3)['result']['lifecycle'] == 'TerminalSettled', 'The model skipped durable pending sibling settlement.')
                    sibling = next(r for r in results if r['origin'] == cancellation['origin']); settled = next(r for r in consumed if r['inputId'] == v.key(sibling['origin']['resultInputId']))
                    row, = connection.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (v.key(cancellation['sourceOutboxItemId']),)).fetchall(); source = json.loads(row[1])
                    finished = name.startswith('finished-')
                    require(row[0] == (3 if finished else 2) and source['intent']['payload']['origin'] == cancellation['origin'] and
                        (source['deliveredAt'] is None if finished else v.instant(source['deliveredAt']) >= v.instant(settled['acceptedAt'])),
                        'Sibling cancellation lost its pre-ACK cut or exact consumption before final acknowledgment.')
                    for result in results:
                        row, = connection.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (v.key(result['sourceOutboxItemId']),)).fetchall(); source = json.loads(row[1])
                        receipt = next(r for r in consumed if r['inputId'] == v.key(result['origin']['resultInputId']))
                        require(row[0] == (3 if finished else 2) and source['intent']['payload'] == result and
                            (source['deliveredAt'] is None if finished else v.instant(source['deliveredAt']) >= v.instant(receipt['acceptedAt'])),
                            'A cancelled native result lost its post-consumption/pre-ACK recovery proof.')
                for origin in origins:
                    row, = connection.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?', (v.key(origin['workerExecutionId']),)).fetchall()
                    worker = json.loads(row[1]); require(row[0] == (1 if waiting else 5) and worker['modelWorkOrigin'] == v.encoded(origin) and worker['pinnedExecutable'] == v.encoded(origin['workerExecutable']),
                        'An actual native worker lost its original model pin or cancellation.')
    return {'kind': manifest['kind'], 'sourceRevision': manifest['fixedSource'], 'wholeStores': 6, 'ownedHosts': 3, 'originalOrphanReproduced': True,
        'postConsumptionPreAckRecoveryProved': True, 'pendingModelSettlementCrashProved': False, 'finalUpgradeAndStudioProved': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('manifest', type=Path)
    print(json.dumps(verify(parser.parse_args().manifest), indent=2))

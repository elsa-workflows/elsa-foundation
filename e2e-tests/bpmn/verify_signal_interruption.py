"""Read-only proof of actual pending Event-sibling cancellation across process loss."""
import argparse
import base64
from contextlib import closing
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sqlite3
import tempfile

spec = importlib.util.spec_from_file_location('signal_proof', Path(__file__).with_name('verify_signal_recovery.py'))
v = importlib.util.module_from_spec(spec); spec.loader.exec_module(v); require = v.require
PARENTS = ('direct', 'root', 'sequence', 'flowchart', 'compensationscope')


def opaque(value):
    if isinstance(value, str):
        require(value.startswith('u16:'), 'The native invocation lost its lossless text encoding.')
        return base64.b64decode(value[4:], validate=True).decode('utf-16le')
    if isinstance(value, dict): return {k: opaque(x) for k, x in value.items()}
    if isinstance(value, list): return [opaque(x) for x in value]
    return value


def verify_case(root, parent, source):
    read = lambda name: json.loads((root / name).read_text())
    p = read('process.json'); embedded = parent != 'direct'
    require(p['kind'] == 'actual-pending-event-sibling-interruption' and p['sourceRevision'] == source and p['parent'].lower() == parent and
        p['stateSeeded'] is p['clockEdited'] is False and p['standardRecoveryHosts'] is True and
        [x['role'] for x in p['processes']] == ['probe', 'standard', 'standard'] and len({x['processId'] for x in p['processes']}) == 3 and
        len({x['controllerProcessId'] for x in p['processes']}) == 1 and len(p['exits']) == 3 and
        {x['processId'] for x in p['exits']} == {x['processId'] for x in p['processes']} and all(x['exitCode'] == -9 for x in p['exits']),
        'The pending sibling journey lost its source or owned process-loss identities.')
    for product in p['products'].values():
        require(product['sourceCommit'] == source and product['metadataOnly'] and len(product['actualProductVersions']) == 9 and
            len(product['releasedLibraryProducts']) == 3 and all(x['productVersion'].endswith('+' + source) and re.fullmatch('[0-9a-f]{64}', x['sha256']) for x in product['actualProductVersions']) and
            all(x['productVersion'].startswith('0.5.0+') and re.fullmatch('[0-9a-f]{64}', x['sha256']) for x in product['releasedLibraryProducts']),
            'The actual standard/probe products lost the exact source or released library.')
    require(set(p['products']) == {'standard', 'probe'} and p['products']['standard']['actualProductVersions'][0]['sha256'] != p['products']['probe']['actualProductVersions'][0]['sha256'] and
        p['products']['standard']['actualProductVersions'][1:] == p['products']['probe']['actualProductVersions'][1:] and
        all(x['failureType'] == 'SqliteException' and x['sourceStateEdited'] is False and x['attempt'] in (1, 2) for x in p['observerUnavailable']),
        'A recovery host used the pause product or hid a refused observation.')
    initial = read('initial.json')['model']; checked = {s: read(s + '-model.json') for s in ('pending', 'finished', 'retained')}
    pending = checked['pending']['model']; final = checked['finished']['model']; origins = [w['nativeWork'] for w in initial['payload']['work']]
    pin = {k: p['workerPublication'][k] for k in ('artifactId', 'artifactHash', 'artifactVersion', 'definitionId', 'definitionVersionId')}
    require(initial['executionId'] == p['modelExecutionId'] and initial['revision'] == 1 and initial['lifecycle'] == 0 and len(origins) == 2 and len(set(p['workers'])) == 2 and
        set(p['workers']) == {o['workerExecutionId'] for o in origins} and p['cancelledWorker'] in p['workers'] and initial['executable'] == p['publication']['executable'] and
        initial['materialIncarnationId'] == p['publication']['incarnationId'] and all(o['workerExecutable'] == pin and o['executionId'] == initial['executionId'] and
        o['executable'] == initial['executable'] and o['materialIncarnationId'] == initial['materialIncarnationId'] for o in origins),
        'The actual sibling waits lost their complete original model or worker pins.')
    require(pending['revision'] == 2 and pending['lifecycle'] == 2 and pending['terminalResult'] == {'status': 'Cancelled'} and not pending['payload']['work'] and
        pending['payload']['formatVersion'] == (6 if embedded else 4) and len(pending['payload']['nativeResults']) == 1 and
        pending['payload']['nativeResults'][0]['origin']['workerExecutionId'] == p['cancelledWorker'], 'The actual crash did not retain pending sibling settlement.')
    cancellation, = pending['payload']['cancellations']; sibling = cancellation['origin']
    require(cancellation['settled'] is False and sibling in origins and sibling['workerExecutionId'] != p['cancelledWorker'] and
        final['revision'] == 3 + int(embedded) and final['lifecycle'] == 3 and final['terminalResult'] == pending['terminalResult'] and
        final['executable'] == initial['executable'] and final['materialIncarnationId'] == initial['materialIncarnationId'] and
        final['payload']['state'] == pending['payload']['state'] == initial['payload']['state'] and not final['payload']['work'] and
        final['payload']['cancellations'] == [{**cancellation, 'settled': True}] and len(final['payload']['nativeResults']) == 2,
        'An original sibling obligation or interpreter history was replaced during recovery.')
    require(checked['finished'] == checked['retained'] and not checked['finished']['queuedModelInputs'] and all(x['status'] == 2 for x in checked['finished']['sources']),
        'The settled model changed after another cold host or left delivery work unacknowledged.')
    marker = read('pending-marker.json'); sources = {s: read(s + '-source.json')['source'] for s in checked}
    old = sources['pending']; new = sources['finished']
    require(marker['phase'] == 'before-native-cancel' and marker['providerEvent'] == 'ActualNativeCancellationSourceBeforeDispatcher' and
        marker['sourceOutboxItemId'] == cancellation['sourceOutboxItemId'] == old['outboxItemId'] == new['outboxItemId'] and
        marker['workflowExecutionId'] == p['modelExecutionId'] and marker['payload'] == old['intent']['payload'] == new['intent']['payload'] == cancellation and
        old['intent']['kind'] == 'Elsa.Bpmn.CancelNativeWork.v1' and old['status'] == 1 and old['deliveredAt'] is None and
        old['deliveryFencingToken'] == marker['fencingToken'] and old['deliveringOwnerId'] == marker['ownerId'] and old['deliveryVisibleAfter'] == marker['visibleAfter'] and
        v.instant(old['deliveryStartedAt']) <= v.instant(p['pendingCapturedAtUtc']) < v.instant(marker['visibleAfter']) and
        new['status'] == 2 and new['deliveryFencingToken'] > marker['fencingToken'] and v.instant(new['deliveredAt']) > v.instant(marker['visibleAfter']) and
        sources['retained'] == new, 'The original fresh cancellation claim did not survive process loss and complete through a higher fence.')
    workers = {phase: read(phase + '-workers.json') for phase in checked}
    receipts = [x for x in checked['finished']['inputs'] if x['inputKind'] == 'native-result']
    require(len(receipts) == 2 and {x['acceptedRevision'] for x in receipts} == {2, 3} and len(checked['pending']['inputs']) == 2 and
        next(x for x in receipts if x['acceptedRevision'] == 2)['result']['lifecycle'] == 'TerminalPendingSettlement', 'Exact native consumption skipped pending model settlement.')
    for origin in origins:
        result, = [r for r in final['payload']['nativeResults'] if r['origin'] == origin]
        receipt, = [r for r in receipts if r['inputId'] == origin['resultInputId']]
        require(result['status'] == 5 and result['completion'] is result['faultCode'] is None and receipt['result']['status'] == 'Accepted' and
            receipt['invocationId'] == origin['invocationId'] and receipt['attemptId'] == origin['attemptId'], 'A cancelled Event result lost its original semantic address.')
        for phase, rows in workers.items():
            row, = [x for x in rows if x['worker']['workflowExecutionId'] == origin['workerExecutionId']]
            expected = 1 if phase == 'pending' and origin == sibling else 5
            require(row['readOnly'] and row['worker']['status'] == expected and row['worker']['modelWorkOrigin'] == origin and row['worker']['pinnedExecutable'] == pin and
                row['worker']['authority'] == initial['authority'], 'An actual Event worker lost its original pin or pending/Cancelled truth.')
            if phase == 'pending' and origin == sibling:
                require(row['resultSource'] is row['resultInput'] is None, 'The sibling was already cancelled before the claimed dispatch interruption.')
            elif phase != 'pending':
                require(row['resultSource']['status'] == 2 and row['resultSource']['intent']['payload'] == result and row['resultInput'] == receipt and
                    v.instant(row['resultSource']['deliveredAt']) >= v.instant(receipt['acceptedAt']), 'An original native result was acknowledged before exact consumption.')
    sibling_receipt, = [r for r in receipts if r['inputId'] == sibling['resultInputId']]
    require(v.instant(new['deliveredAt']) >= v.instant(sibling_receipt['acceptedAt']), 'The sibling cancellation was acknowledged before its exact result consumption.')
    parents = {}
    if embedded:
        parents = {phase: read(phase + '-parent.json') for phase in checked}; invocation = pending['payload']['nativeParent']['invocation']; origin = opaque(invocation)
        parent_pin = {k: p['parentPublication'][k] for k in pin}
        require(origin['nativeExecutionId'] == p['nativeParentExecutionId'] and origin['executionId'] == p['modelExecutionId'] and origin['nativeExecutable'] == parent_pin and
            final['payload']['nativeParent']['invocation'] == invocation and parents['finished'] == parents['retained'], 'The native parent lost its exact retained invocation.')
        for phase, item in parents.items():
            run, = [a for a in item['activities'] if a['execution']['executableNodeId'] == 'run-bpmn']
            terminal = phase != 'pending'
            require(item['readOnly'] and item['model'] == checked[phase]['model'] and item['nativeParent']['status'] == (5 if terminal else 1) and
                item['nativeParent']['pinnedExecutable'] == parent_pin and run['status'] == (6 if terminal else 3) and run['invocationId'] == origin['nativeInvocationId'] and
                not [a for a in item['activities'] if a['execution']['executableNodeId'] == 'after'], 'The native parent advanced a successor or lost its pending/cancelled RunBpmn.')
            if terminal:
                settlement, = [r for r in item['inputs'] if r['inputKind'] == 'native-parent-settled']
                returned, = [s for s in item['sources'] if s['intent']['kind'] == 'Elsa.Bpmn.ResumeNativeParent.v1']
                require(not item['bookmarks'] and not item['queuedModelInputs'] and run['completion'] is None and settlement['acceptedRevision'] == 4 and
                    settlement['invocationId'] == origin['nativeInvocationId'] and settlement['attemptId'] == origin['nativeAttemptId'] and
                    returned['intent']['payload'] == {'FormatVersion': 1, 'Invocation': invocation, 'Result': final['terminalResult']} and returned['status'] == 2 and
                    v.instant(returned['deliveredAt']) >= v.instant(settlement['acceptedAt']) and any(t['status'] == 1 and t['registrationId'] == origin['registrationId'] and
                    t['providerId'] == 'elsa.bpmn.native-parent.v1' and t['payloadType']['alias'] == 'Elsa.Activities.Bpmn.Models.RunBpmnCompletion' for t in run['triggerDeliveries']),
                    'The cancelled native parent lacks exact typed consumption and settlement before ACK.')
            else:
                bookmark, = item['bookmarks']
                require(bookmark['bookmarkId'] == origin['registrationId'] and bookmark['stimulusHash'] == p['modelExecutionId'], 'The pending parent lost its original real wait.')
    else: require(p['nativeParentExecutionId'] is p['parentPublication'] is None, 'The direct model fabricated a native parent.')
    require(set(p['snapshots']) == set(checked) and all(set(x) == {'runtime', 'design'} for x in p['snapshots'].values()), 'A whole coupled pending sibling store is missing.')
    materials = None
    for phase, pair in p['snapshots'].items():
        for store, entry in pair.items():
            path = root / entry['file']; raw = gzip.decompress(path.read_bytes())
            require(entry['file'] == phase + '-' + store + '.sqlite.gz' and v.digest(path) == entry['gzipSha256'] and
                hashlib.sha256(raw).hexdigest() == entry['databaseSha256'] == entry['observer']['snapshotSha256'] and entry['observer']['readOnly'], 'A whole pending sibling store or observer receipt changed.')
            with tempfile.TemporaryDirectory(prefix='elsa-pending-sibling-proof-') as temporary:
                db = Path(temporary) / 'store.sqlite'; db.write_bytes(raw)
                with closing(sqlite3.connect(db.as_uri() + '?mode=ro&immutable=1', uri=True)) as c:
                    require(c.execute('PRAGMA integrity_check').fetchall() == [('ok',)], 'A pending sibling whole store is corrupt.')
                    if store != 'runtime': continue
                    model = checked[phase]['model']; row, = c.execute('SELECT Revision,Lifecycle,ContentJson FROM elsa_runtime_model_execution').fetchall(); physical = json.loads(row[2])
                    require(row[:2] == (model['revision'], model['lifecycle']) and physical['payload'] == model['payload'] and physical['executionId'] == v.key(p['modelExecutionId']) and
                        physical['executable'] == v.encoded(initial['executable']) and physical['materialIncarnationId'] == v.key(initial['materialIncarnationId']), 'The physical model differs from the actual pending/settled observation.')
                    require(c.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state').fetchone()[0] == 2 + int(embedded) and
                        c.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?', (v.key(p['modelExecutionId']),)).fetchone()[0] == 0,
                        'A fabricated native model root or extra Event worker appeared.')
                    for o in origins:
                        row, = c.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?', (v.key(o['workerExecutionId']),)).fetchall(); worker = json.loads(row[1])
                        require(row[0] == (1 if phase == 'pending' and o == sibling else 5) and worker['modelWorkOrigin'] == v.encoded(o) and worker['pinnedExecutable'] == v.encoded(pin),
                            'The physical Event worker lost its original pin or actual cancellation.')
                    row, = c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (v.key(cancellation['sourceOutboxItemId']),)).fetchall(); delivery = json.loads(row[1])
                    require(row[0] == sources[phase]['status'] and delivery['intent']['payload'] == cancellation and
                        all(delivery[k] == sources[phase][k] for k in ('deliveryFencingToken', 'deliveredAt', 'deliveryVisibleAfter', 'deliveryStartedAt')) and
                        delivery['deliveringOwnerId'] == (v.key(sources[phase]['deliveringOwnerId']) if sources[phase]['deliveringOwnerId'] is not None else None),
                        'The physical original cancellation source differs from its recovery claim or ACK.')
                    actual_materials = dict(c.execute('SELECT ArtifactId,ContentJson FROM elsa_runtime_workflow_executable'))
                    if materials is None: materials = actual_materials
                    else: require(actual_materials == materials, 'An original published artifact was rewritten during recovery.')
                    material = json.loads(actual_materials[v.key(initial['executable']['artifactId'])])['material']
                    require(material['definition']['formatVersion'] == 5 and material['definition']['nativeEventBindings'] == [{'bindingId': binding, 'executable': pin} for binding in ('first-event', 'second-event')],
                        'The physical signal artifact lost its explicit Event-worker bindings.')
                    for result in model['payload']['nativeResults']:
                        receipt, = c.execute('SELECT ContentJson FROM elsa_runtime_model_input WHERE InputId=?', (v.key(result['origin']['resultInputId']),)).fetchall(); receipt = json.loads(receipt[0])
                        require(receipt['acceptedRevision'] in (2, 3) and receipt['inputKind'] == v.key('native-result') and receipt['result']['status'] == 'Accepted' and
                            receipt['executionId'] == v.key(p['modelExecutionId']) and receipt['invocationId'] == v.key(result['origin']['invocationId']) and
                            receipt['attemptId'] == v.key(result['origin']['attemptId']), 'A physical native result lost its exact model consumption.')
                        if phase != 'pending':
                            status, raw_source = c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (v.key(result['sourceOutboxItemId']),)).fetchone(); raw_source = json.loads(raw_source)
                            require(status == 2 and raw_source['intent']['payload'] == result and v.instant(raw_source['deliveredAt']) >= v.instant(receipt['acceptedAt']),
                                'A physical native result lacks exact consumption before acknowledgment.')
                    if embedded:
                        item = parents[phase]; row, = c.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?', (v.key(p['nativeParentExecutionId']),)).fetchall()
                        require(row[0] == item['nativeParent']['status'] and json.loads(row[1])['pinnedExecutable'] == v.encoded(parent_pin), 'The physical native parent lost its real pin or cancellation.')
                        for activity in item['activities']:
                            row, = c.execute('SELECT Status FROM elsa_runtime_activity_execution_state WHERE ActivityExecutionId=?', (v.key(activity['invocationId']),)).fetchall()
                            require(row[0] == ('Scheduled', 'Running', 'Waiting', 'Suspended', 'Completed', 'Faulted', 'Cancelled', 'Recovered', 'Superseded')[activity['status']], 'A physical native activity differs from its observed cancellation.')
    return {'parent': parent, 'wholeStores': 6, 'ownedHosts': 3, 'pendingModelSettlementCrashProved': True}


def verify(path):
    manifest = json.loads(path.read_text()); root = path.parent
    require(manifest['schemaVersion'] == 1 and manifest['kind'] == 'actual-pending-event-sibling-interruption-matrix' and re.fullmatch('[0-9a-f]{40}', manifest['sourceRevision']) and
        len(manifest['cases']) == 5 and {c['parent'] for c in manifest['cases']} == set(PARENTS), 'The pending sibling matrix lost its exact source or required parents.')
    for asset in manifest['assets']:
        name = Path(asset['path']); require(not name.is_absolute() and '..' not in name.parts and v.digest(root / name) == asset['sha256'], 'A pending sibling matrix asset changed.')
    require(len({a['path'] for a in manifest['assets']}) == len(manifest['assets']) and {a['path'] for a in manifest['assets']} ==
        {str(p.relative_to(root)) for p in root.rglob('*') if p.is_file() and p.name not in ('manifest.json', 'README.md')}, 'The pending sibling asset inventory is incomplete.')
    baseline = Path(__file__).resolve().parents[2] / 'specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json'
    require(v.digest(baseline) == manifest['baselineManifestSha256'] and len(json.loads(baseline.read_text())['assets']) == manifest['frozenBaselineAssetCount'] == 104 and
        all(v.digest(baseline.parent / a['path']) == a['sha256'] for a in json.loads(baseline.read_text())['assets']), 'The frozen authoring baseline changed.')
    build = json.loads((root / 'build/receipt.json').read_text())
    require(build['sourceRevision'] == manifest['sourceRevision'] and build['productionCodeSource'] == manifest['productionCodeSource'] and build['productionFilesUnchanged'] and
        {b['product'] for b in build['builds']} == {'standard', 'reader', 'probe'} and all(b['errors'] == 0 for b in build['builds']), 'An actual recovery product build failed or lost its source.')
    for diagnostic in manifest['diagnostics']:
        name = Path(diagnostic['directory']); require(not name.is_absolute() and '..' not in name.parts and name.parts[0] == 'diagnostics', 'A diagnostic escapes the pending cancellation bundle.')
        failure = json.loads((root / name / 'failure.json').read_text())
        require(diagnostic['qualifiesJourney'] is failure['qualifiesJourney'] is False and diagnostic['sourceRevision'] == failure['sourceRevision'],
            'A diagnostic cannot qualify the pending cancellation journey.')
    result = []
    for case in manifest['cases']:
        require(case['directory'] == case['parent'], 'A pending sibling parent directory changed.')
        result.append(verify_case(root / case['directory'], case['parent'], manifest['sourceRevision']))
    return {'kind': manifest['kind'], 'sourceRevision': manifest['sourceRevision'], 'cases': result, 'wholeStores': 30, 'ownedHosts': 15,
        'pendingModelSettlementCrashProved': True, 'finalUpgradeAndStudioProved': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('manifest', type=Path)
    print(json.dumps(verify(parser.parse_args().manifest), indent=2))

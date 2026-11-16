"""Read-only proof of actual publication retention and admission-off/runtime-only recovery."""
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
    return value


def verify_case(root, parent, source):
    read = lambda name: json.loads((root / name).read_text())
    p = read('process.json'); embedded = parent != 'direct'; products = p['products']; collection = read('collection.json')
    require(p['schemaVersion'] == 1 and p['kind'] == 'actual-model-retention-and-admission-rollback' and p['sourceRevision'] == source and p['parent'].lower() == parent and
        p['executionStateEdited'] is p['claimEdited'] is p['clockEdited'] is p['designDatabaseRecreated'] is False and
        [x['role'] for x in p['processes']] == ['probe', 'standard', 'standard'] and [x['admissionEnabled'] for x in p['processes']] == [True, False, False] and
        [x['runtimeOnly'] for x in p['processes']] == [False, True, True] and len({x['processId'] for x in p['processes']}) == 3 and
        len({x['controllerProcessId'] for x in p['processes']}) == 1 and all(Path(x['executable']).name == 'dotnet' for x in p['processes']) and
        len(p['exits']) == 3 and {x['processId'] for x in p['processes']} == {x['processId'] for x in p['exits']} and all(x['exitCode'] == -9 for x in p['exits']),
        'The actual rollback journey lost its source, admission/Design configuration or owned process losses.')
    require(set(products) == {'standard', 'probe'} and products['standard']['actualProductVersions'][0]['sha256'] != products['probe']['actualProductVersions'][0]['sha256'],
        'Recovery did not use a distinct standard product.')
    for product in products.values():
        require(product['sourceCommit'] == source and len(product['actualProductVersions']) == 9 and len(product['releasedLibraryProducts']) == 3 and
            all(x['productVersion'].endswith('+' + source) and re.fullmatch('[0-9a-f]{64}', x['sha256']) for x in product['actualProductVersions']) and
            all(x['productVersion'].startswith('0.5.0+') for x in product['releasedLibraryProducts']), 'An actual host product lost its exact source or released BPMN library.')
    require(all(x['failureType'] == 'SqliteException' and x['sourceStateEdited'] is False and x['attempt'] in (1, 2) for x in p['observerUnavailable']), 'A refused observation was hidden or replaced with state edits.')
    feature = read('runtime-only-features.json')
    require(feature['ActivitiesBpmnHosting']['AdmitNewExecutions'] is False and 'ActivitiesBpmnHostingApi' in feature and 'WorkflowsRuntimeApi' in feature and
        not any('Design' in name or 'Publishing' in name for name in feature), 'The recovery configuration still enables admission or Design/Publishing storage.')
    denied = read('new-admission-refused.json')
    require(p['newAdmissionHttpStatus'] == denied['status'] == 503, 'The rollback host accepted a new model execution.')
    checked = {phase: read(phase + '-model.json') for phase in ('pending', 'recovered', 'finished', 'retained')}
    initial = read('initial-model.json')['model']; pending = checked['pending']['model']; final = checked['finished']['model']
    require(initial == pending == checked['recovered']['model'] and pending['revision'] == 1 and pending['lifecycle'] == 0 and pending['terminalResult'] is None and
        pending['executionId'] == p['modelExecutionId'] and pending['executable'] == p['published']['executable'] and pending['materialIncarnationId'] == p['published']['incarnationId'] and
        pending['payload']['formatVersion'] == (5 if embedded else 2) and len(pending['payload']['work']) == 2 and checked['pending']['nativeWorkflowRows'] == int(embedded),
        'The original committed model decision changed or workers were admitted before the collection/crash cut.')
    origins = [w['nativeWork'] for w in pending['payload']['work']]; ids = p['workers']; pin = {k: p['workerPublication'][k] for k in ('artifactId', 'artifactHash', 'artifactVersion', 'definitionId', 'definitionVersionId')}
    require(len(set(ids)) == 2 and set(ids) == {o['workerExecutionId'] for o in origins} and all(o['executionId'] == p['modelExecutionId'] and
        o['executable'] == pending['executable'] and o['materialIncarnationId'] == pending['materialIncarnationId'] and o['workerExecutable'] == pin for o in origins),
        'The owed native work lost its original exact model, worker or material address.')
    require(collection['operation'] == '--retire-and-sweep' and collection['controllerProcessId'] == p['processes'][0]['controllerProcessId'] and collection['executable'].endswith('/dotnet') and
        collection['executionStateEdited'] is collection['claimEdited'] is collection['clockEdited'] is False and collection['supportsStimulusDeliveryRetention'] is True and
        collection['modelBefore'] == collection['modelAfter'] == pending and collection['orphanCollected'] and collection['orphanArtifactId'] == p['orphanPublication']['executable']['artifactId'] and
        collection['sweep']['deletedArtifactCount'] == 1 and collection['sweep']['didWork'] and len(collection['products']) == 4 and
        all(x['informationalVersion'].endswith('+' + source) and re.fullmatch('[0-9a-f]{64}', x['sha256']) for x in collection['products']),
        'The actual retirement/collector SDK lost its source, positive orphan control or unchanged execution boundary.')
    before_materials = {m['identity']['artifactId']: m for m in collection['materialsBefore']}; after_materials = {m['identity']['artifactId']: m for m in collection['materialsAfter']}
    require(before_materials == after_materials and len(before_materials) == 2 + int(embedded) and before_materials[pending['executable']['artifactId']]['incarnationId'] == pending['materialIncarnationId'] and
        before_materials[pin['artifactId']]['identity'] == pin and len(collection['referencesBefore']) == len(collection['referencesAfter']) == 3 + int(embedded),
        'Collection rewrote or removed an owed original material/incarnation.')
    for before, after in zip(collection['referencesBefore'], collection['referencesAfter']):
        require(before['deletedAt'] is None and before['expiresAt'] is None, 'An owned publication was not live before retirement.')
        if before['artifactId'] == collection['orphanArtifactId']: require(after is None, 'The unreferenced control source was not collected.')
        else: require(after is not None and after['artifactId'] == before['artifactId'] and after['sourceReferenceId'] == before['sourceReferenceId'] and
            after['deletedAt'] == collection['startedAtUtc'], 'An owed retired source lost its provenance or retirement.')
    sources = {phase: read(phase + '-source.json')['source'] for phase in checked}; marker = read('pending-marker.json'); old = sources['pending']; recovered = sources['recovered']
    require(marker['phase'] == 'before-native-start' and marker['providerEvent'] == 'ActualNativeStartSourceBeforeDispatcher' and marker['workflowExecutionId'] == p['modelExecutionId'] and
        marker['sourceOutboxItemId'] == old['outboxItemId'] == recovered['outboxItemId'] and marker['payload'] in origins and old['intent']['payload'] == recovered['intent']['payload'] == marker['payload'] and
        old['status'] == 1 and old['deliveryFencingToken'] == marker['fencingToken'] and old['deliveringOwnerId'] == marker['ownerId'] and
        v.instant(collection['completedAtUtc']) <= v.instant(p['pendingCapturedAtUtc']) < v.instant(marker['visibleAfter']) and old['deliveryVisibleAfter'] == marker['visibleAfter'] and
        recovered['status'] == 2 and recovered['deliveryFencingToken'] > marker['fencingToken'] and v.instant(recovered['deliveredAt']) > v.instant(marker['visibleAfter']) and
        recovered == sources['finished'] == sources['retained'], 'The original fresh native-start claim did not recover after retirement through a higher fence.')
    require(final['executable'] == pending['executable'] and final['materialIncarnationId'] == pending['materialIncarnationId'] and final['revision'] == 3 + int(embedded) and final['lifecycle'] == 3 and
        final['terminalResult'] == {'status': 'Completed', 'outcome': 'Done'} and not final['payload']['work'] and checked['finished'] == checked['retained'] and
        all(x['status'] == 2 for x in checked['finished']['sources']) and not checked['finished']['queuedModelInputs'], 'The accepted rollback model did not complete and retain exact pins/settlement.')
    native = {phase: read(phase + '-workers.json') for phase in ('recovered', 'finished', 'retained')}; results = final['payload']['nativeResults']
    require(native['finished'] == native['retained'] and len(results) == 2, 'Original Event results changed in another cold host.')
    for origin in origins:
        result, = [r for r in results if r['origin'] == origin]
        require(result['status'] == 3 and result['completion']['resultType']['alias'] == 'Elsa.Activities.Primitives.Activities.EventResult' and
            result['completion']['result'] == {'eventName': 'actual-model-rollback-' + p['parent'], 'payload': {'release': 'admission-and-design-off'}}, 'An original Event result lost its real runtime-only signal.')
        for phase, rows in native.items():
            row, = [w for w in rows if w['worker']['workflowExecutionId'] == origin['workerExecutionId']]
            require(row['readOnly'] and row['worker']['status'] == (1 if phase == 'recovered' else 3) and row['worker']['modelWorkOrigin'] == origin and row['worker']['pinnedExecutable'] == pin and
                row['worker']['authority'] == pending['authority'], 'An original native worker lost its exact pin or rollback outcome.')
            if phase == 'recovered': require(row['resultInput'] is row['resultSource'] is None, 'A worker consumed a signal before the real runtime-only publisher.')
            else:
                receipt = row['resultInput']; delivery = row['resultSource']
                require(receipt['inputId'] == origin['resultInputId'] and receipt['invocationId'] == origin['invocationId'] and receipt['attemptId'] == origin['attemptId'] and
                    receipt['acceptedRevision'] in (2, 3) and receipt['result']['status'] == 'Accepted' and delivery['status'] == 2 and delivery['outboxItemId'] == result['sourceOutboxItemId'] and
                    delivery['intent']['payload'] == result and v.instant(delivery['deliveredAt']) >= v.instant(receipt['acceptedAt']), 'A native rollback result lacks exact consumption before ACK.')
    parents = {}
    if embedded:
        parents = {phase: read(phase + '-parent.json') for phase in checked}; invocation = pending['payload']['nativeParent']['invocation']; origin = opaque(invocation)
        require(parents['finished'] == parents['retained'] and origin['nativeExecutionId'] == p['nativeParentExecutionId'] and origin['executionId'] == p['modelExecutionId'] and
            origin['model']['executable'] == pending['executable'] and origin['model']['materialIncarnationId'] == pending['materialIncarnationId'], 'The rollback native parent lost its original model invocation.')
        for phase, item in parents.items():
            terminal = phase in ('finished', 'retained'); run, = [a for a in item['activities'] if a['execution']['executableNodeId'] == 'run-bpmn']
            require(item['model'] == checked[phase]['model'] and item['nativeParent']['pinnedExecutable'] == origin['nativeExecutable'] and item['nativeParent']['status'] == (3 if terminal else 1) and
                run['status'] == (4 if terminal else 3) and run['invocationId'] == origin['nativeInvocationId'], 'The native parent lost its actual wait/typed rollback completion.')
            if terminal:
                settlement, = [r for r in item['inputs'] if r['inputKind'] == 'native-parent-settled']; returned, = [s for s in item['sources'] if s['intent']['kind'] == 'Elsa.Bpmn.ResumeNativeParent.v1']
                require(not item['bookmarks'] and settlement['acceptedRevision'] == 4 and settlement['invocationId'] == origin['nativeInvocationId'] and returned['status'] == 2 and
                    v.instant(returned['deliveredAt']) >= v.instant(settlement['acceptedAt']) and run['completion']['result']['inlineValue'] == {'executionId': p['modelExecutionId'], 'result': final['terminalResult']},
                    'The rollback parent lost exact typed result consumption and settlement before ACK.')
            if parent != 'root': require(len([a for a in item['activities'] if a['execution']['executableNodeId'] == 'after' and a['status'] == 4]) == int(terminal), 'A native successor did not continue exactly once after rollback.')
    else: require(p['nativeParentExecutionId'] is p['parentPublication'] is None, 'The direct rollback model fabricated a native parent.')
    require(set(p['snapshots']) == set(checked) and set(p['snapshots']['pending']) == {'runtime', 'design'} and
        all(set(pair) == {'runtime'} for phase, pair in p['snapshots'].items() if phase != 'pending'), 'The actual store set lost its coupled input or fabricated later Design availability.')
    material_rows = None
    for phase, pair in p['snapshots'].items():
        for store, entry in pair.items():
            path = root / entry['file']; raw = gzip.decompress(path.read_bytes())
            require(entry['file'] == phase + '-' + store + '.sqlite.gz' and v.digest(path) == entry['gzipSha256'] and hashlib.sha256(raw).hexdigest() == entry['databaseSha256'] == entry['observer']['snapshotSha256'] and entry['observer']['readOnly'], 'An actual rollback whole store or observer receipt changed.')
            with tempfile.TemporaryDirectory(prefix='elsa-model-rollback-proof-') as t:
                db = Path(t) / 'store.sqlite'; db.write_bytes(raw)
                with closing(sqlite3.connect(db.as_uri() + '?mode=ro&immutable=1', uri=True)) as c:
                    require(c.execute('PRAGMA integrity_check').fetchall() == [('ok',)], 'A whole rollback store is corrupt.')
                    if store != 'runtime': continue
                    row, = c.execute('SELECT Revision,Lifecycle,ContentJson FROM elsa_runtime_model_execution').fetchall(); physical = json.loads(row[2]); model = checked[phase]['model']
                    require(row[:2] == (model['revision'], model['lifecycle']) and physical['payload'] == model['payload'] and physical['executable'] == v.encoded(model['executable']) and
                        physical['materialIncarnationId'] == v.key(model['materialIncarnationId']), 'The physical accepted model changed or a new rollback execution was admitted.')
                    actual_materials = {row[0]: row[1:] for row in c.execute('SELECT ArtifactId,IsModelMaterial,IncarnationId,ContentJson FROM elsa_runtime_workflow_executable')}
                    require(v.key(collection['orphanArtifactId']) not in actual_materials and len(actual_materials) == 4 + int(embedded) and
                        all(v.key(identity) in actual_materials for identity in before_materials), 'The actual collector kept its orphan or removed an owed pinned artifact.')
                    if material_rows is None: material_rows = actual_materials
                    else: require(actual_materials == material_rows, 'A retained model/worker/parent material or incarnation changed after rollback.')
                    for before, after in zip(collection['referencesBefore'], collection['referencesAfter']):
                        rows = c.execute('SELECT IsRetired,ContentJson FROM elsa_runtime_workflow_executable_source_reference WHERE SourceReferenceId=?', (v.key(before['sourceReferenceId']),)).fetchall()
                        if after is None: require(not rows, 'A collected orphan publication reappeared.')
                        else: require(len(rows) == 1 and rows[0][0] == 1 and json.loads(rows[0][1])['reference']['deletedAt'] == collection['startedAtUtc'], 'The physical retired publication became live or lost its retirement.')
                    native_rows = c.execute('SELECT WorkflowExecutionId,Status,ContentJson FROM elsa_runtime_workflow_execution_state').fetchall()
                    require(len(native_rows) == (int(embedded) if phase == 'pending' else 2 + int(embedded) + int(phase in ('finished', 'retained'))) and
                        all(identity != v.key(p['modelExecutionId']) for identity, _, _ in native_rows), 'A native worker was admitted before the crash or a fake native model root appeared.')
                    for work in origins:
                        rows = [r for r in native_rows if r[0] == v.key(work['workerExecutionId'])]
                        if phase == 'pending': require(not rows, 'An owed Event worker was already admitted before the interrupted start.')
                        else: require(len(rows) == 1 and rows[0][1] == (1 if phase == 'recovered' else 3) and json.loads(rows[0][2])['modelWorkOrigin'] == v.encoded(work), 'A physical rollback worker lost its original origin or result.')
                    if phase != 'pending': require(c.execute('SELECT COUNT(*) FROM elsa_runtime_scheduler_work_item').fetchone()[0] == 0, 'A refused new model left queued work after rollback.')
                    status, raw_source = c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (v.key(marker['sourceOutboxItemId']),)).fetchone(); raw_source = json.loads(raw_source)
                    require(status == sources[phase]['status'] and raw_source['intent']['payload'] == marker['payload'] and raw_source['deliveryFencingToken'] == sources[phase]['deliveryFencingToken'] and
                        raw_source['deliveryVisibleAfter'] == sources[phase]['deliveryVisibleAfter'], 'The physical original native-start source lost its fresh/recovered claim.')
                    if phase in ('finished', 'retained'):
                        broadcast = [json.loads(raw) for raw, in c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox WHERE WorkflowExecutionId=?', (v.key(p['publisherExecutionId']),))]
                        broadcast, = [s for s in broadcast if s['intent']['kind'] == v.key('Elsa.Activities.Primitives.PublishStimulus')]
                        cohort = broadcast['recipientSnapshot']
                        require(broadcast['status'] == 'Delivered' and not cohort['starts'] and len(cohort['resumes']) == 2 and
                            {r['workflowExecutionId'] for r in cohort['resumes']} == {v.key(i) for i in ids}, 'The actual runtime-only signal lost its two original physical recipients.')
                        for result in results:
                            status, raw_source = c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (v.key(result['sourceOutboxItemId']),)).fetchone(); raw_source = json.loads(raw_source)
                            receipt, = c.execute('SELECT ContentJson FROM elsa_runtime_model_input WHERE InputId=?', (v.key(result['origin']['resultInputId']),)).fetchone(); receipt = json.loads(receipt)
                            require(status == 2 and raw_source['intent']['payload'] == result and receipt['acceptedRevision'] in (2, 3) and
                                receipt['inputKind'] == v.key('native-result') and receipt['executionId'] == v.key(p['modelExecutionId']) and
                                receipt['invocationId'] == v.key(result['origin']['invocationId']) and receipt['attemptId'] == v.key(result['origin']['attemptId']) and
                                v.instant(raw_source['deliveredAt']) >= v.instant(receipt['acceptedAt']),
                                'A physical rollback result lacks exact consumption before ACK.')
                    if embedded:
                        native, = [r for r in native_rows if r[0] == v.key(p['nativeParentExecutionId'])]
                        require(native[1] == parents[phase]['nativeParent']['status'], 'The physical native parent lost its actual rollback outcome.')
    return {'parent': parent, 'wholeStores': 5, 'ownedHosts': 3, 'orphanCollected': True, 'acceptedWorkRecoveredWithAdmissionAndDesignOff': True}


def verify(path):
    manifest = json.loads(path.read_text()); root = path.parent
    require(manifest['schemaVersion'] == 1 and manifest['kind'] == 'actual-model-retention-and-rollback-matrix' and re.fullmatch('[0-9a-f]{40}', manifest['sourceRevision']) and
        len(manifest['cases']) == 5 and {x['parent'] for x in manifest['cases']} == set(PARENTS), 'The actual rollback matrix lost its exact source or required native parents.')
    for asset in manifest['assets']:
        name = Path(asset['path']); require(not name.is_absolute() and '..' not in name.parts and v.digest(root / name) == asset['sha256'], 'An actual rollback asset changed.')
    require(len({x['path'] for x in manifest['assets']}) == len(manifest['assets']) and {x['path'] for x in manifest['assets']} ==
        {str(p.relative_to(root)) for p in root.rglob('*') if p.is_file() and p.name not in ('manifest.json', 'README.md')}, 'The actual rollback inventory is incomplete.')
    baseline = Path(__file__).resolve().parents[2] / 'specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json'
    require(v.digest(baseline) == manifest['baselineManifestSha256'] and len(json.loads(baseline.read_text())['assets']) == manifest['frozenBaselineAssetCount'] == 104 and
        all(v.digest(baseline.parent / a['path']) == a['sha256'] for a in json.loads(baseline.read_text())['assets']), 'The frozen authoring baseline changed.')
    build = json.loads((root / 'build/receipt.json').read_text())
    require(build['sourceRevision'] == manifest['sourceRevision'] and build['productionCodeSource'] == manifest['productionCodeSource'] and build['productionFilesUnchanged'] and
        len(build['builds']) == 4 and {b['product'] for b in build['builds']} == {'standard', 'probe', 'reader', 'sdk'} and all(b['errors'] == 0 for b in build['builds']),
        'An actual rollback product build failed or lost its source.')
    cases = [verify_case(root / item['parent'], item['parent'], manifest['sourceRevision']) for item in manifest['cases']]
    return {'kind': manifest['kind'], 'sourceRevision': manifest['sourceRevision'], 'cases': cases, 'wholeStores': 25, 'ownedHosts': 15,
        'retiredPublicationRecoveryProved': True, 'admissionOffAndRuntimeOnlyRecoveryProved': True, 'nonSqlitePhysicalProviderQualificationProved': False, 'finalUpgradeAndStudioProved': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('manifest', type=Path)
    print(json.dumps(verify(parser.parse_args().manifest), indent=2))

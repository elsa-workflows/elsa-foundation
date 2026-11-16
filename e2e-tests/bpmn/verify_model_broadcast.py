"""Read-only verification of actual direct-model broadcast selection and recovery cuts."""
import argparse
from contextlib import closing
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sqlite3
import tempfile

SPEC = importlib.util.spec_from_file_location('signal_proof', Path(__file__).with_name('verify_signal_recovery.py'))
v = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(v)
require = v.require
CUTS = ('before-selection', 'after-resume', 'empty-cohort')


def verify_case(root, cut, source):
    read = lambda name: json.loads((root / name).read_text())
    p = read('process.json'); rearm = cut == 'after-resume'; before_selection = cut == 'before-selection'; empty = cut == 'empty-cohort'
    expected_roles = ['probe', 'assist-standard', 'standard', 'standard']
    require(p['schemaVersion'] == 1 and p['kind'] == 'actual-model-broadcast-recovery' and p['sourceRevision'] == source and p['cut'] == cut and
        p['stateSeeded'] is p['clockEdited'] is False and p['standardRecoveryHosts'] and p['separateStandardConsumerBeforeCrash'] and
        [x['role'] for x in p['processes']] == expected_roles and len({x['processId'] for x in p['processes']}) == 4 and
        len({x['controllerProcessId'] for x in p['processes']}) == 1 and len(p['exits']) == 4 and all(x['exitCode'] == -9 for x in p['exits']),
        'The actual broadcast lost its source, controlled consumer or process-loss identities.')
    require(set(p['products']) == {'standard', 'probe'} and all(product['sourceCommit'] == source and product['metadataOnly'] and
        len(product['actualProductVersions']) == 12 and all(item['productVersion'].endswith('+' + source) and re.fullmatch('[0-9a-f]{64}', item['sha256'])
        for item in product['actualProductVersions']) for product in p['products'].values()), 'The actual broadcast products differ from their committed source.')
    libraries = read('host-library-products.json')
    require(libraries['sourceCommit'] == source and libraries['metadataOnly'] and len(libraries['releasedLibraryProducts']) == 3 and
        all(item['productVersion'].startswith('0.5.0+') and re.fullmatch('[0-9a-f]{64}', item['sha256']) for item in libraries['releasedLibraryProducts']),
        'The actual host lost its released BPMN0.5.0 library products.')
    marker = read('cut-marker.json'); cut_source = read('cut-source.json'); before = read('before-source.json'); final = read('finished-source.json'); retained_source = read('retained-source.json')
    first = before['source']; last = final['source']; late = read('late-before-crash.json'); finished = read('finished-models.json')
    require(all(item['providerChecked'] and item['readOnly'] for item in (cut_source, before, final, retained_source)) and
        marker['phase'] == ('after-cohort' if empty else cut) and marker['workflowExecutionId'] == p['publisherExecutionId'] == first['workflowExecutionId'] and
        first['outboxItemId'] == marker['sourceOutboxItemId'] == last['outboxItemId'] and first['status'] == 'Delivering' and first['deliveryFencingToken'] == marker['fencingToken'] == 1 and
        first['deliveringOwnerId'] == marker['ownerId'] and v.instant(p['pendingCapturedAtUtc']) < v.instant(first['deliveryVisibleAfter']) and
        last['status'] == 'Delivered' and last['deliveryFencingToken'] > first['deliveryFencingToken'] and v.instant(last['deliveredAt']) >= v.instant(first['deliveryVisibleAfter']) and
        retained_source['source'] == last and cut_source['source']['snapshot'] == first['snapshot'], 'The original source lost its fresh pre-ACK claim or real cold retry.')
    require(finished == read('retained-models.json') and set(finished) == set(p['modelExecutionIds']) and len(finished) == (1 if empty else 2),
        'The actual model decision changed after another cold process.')
    initial = None if empty else read('initial.json'); original_ids = set() if empty else {w['nativeWork']['workerExecutionId'] for w in initial['model']['payload']['work']}
    late_id = late['model']['executionId']; late_ids = {w['nativeWork']['workerExecutionId'] for w in late['model']['payload']['work']}
    require(late['model']['revision'] == 1 and len(late_ids) == 2 and not original_ids.intersection(late_ids), 'The later listener is not a distinct actual model wait.')
    cohort = last['snapshot']; require(not cohort['starts'], 'An unqualified start trigger entered the model catch cohort.')
    selected = {r['workflowExecutionId'] for r in cohort['resumes']}
    require(selected == (original_ids | late_ids if before_selection else set() if empty else original_ids) and
        len(cohort['resumes']) == (4 if before_selection else 0 if empty else 2), 'The first delivery selected the wrong actual model recipients.')
    if before_selection:
        require(first['snapshot'] is None and all(value['model']['lifecycle'] == 3 and value['model']['revision'] == 3 and not value['model']['payload']['work'] and
            len(value['model']['payload']['nativeResults']) == 2 and value['model']['terminalResult'] == {'status': 'Completed', 'outcome': 'Done'} for value in finished.values()),
            'Delivery-time first selection did not complete both original and later model catches.')
        for recipient in cohort['resumes']:
            if recipient['workflowExecutionId'] in late_ids:
                require(v.instant(first['recordedAt']) < v.instant(recipient['createdAt']) <= v.instant(cohort['selectedAt']), 'A later native listener was not created between publisher commit and first selection.')
    else:
        require(first['snapshot'] == cohort and finished[late_id]['model'] == late['model'], 'A saved source cohort changed or consumed a later listener.')
    rearmed = None
    if rearm:
        rearmed = read('rearmed-before-crash.json'); model_id = initial['model']['executionId']; original = {w['nativeWork']['workerExecutionId']: w for w in initial['model']['payload']['work']}
        require(rearmed['model']['revision'] == 2 and len(rearmed['model']['payload']['nativeResults']) == 1 and
            rearmed['model']['payload']['nativeResults'][0]['origin'] == original[marker['acceptedExecution']]['nativeWork'] and
            marker['acceptedBookmark'] == next(r['bookmarkId'] for r in cohort['resumes'] if r['workflowExecutionId'] == marker['acceptedExecution']),
            'The pre-ACK rearm did not consume the exact original callback.')
        new, = [w for w in rearmed['model']['payload']['work'] if w['nativeWork']['workerExecutionId'] not in original_ids]
        require(new['nativeWork']['workerExecutionId'] in read('rearmed-native-waits.json') and new in finished[model_id]['model']['payload']['work'] and
            finished[model_id]['model']['revision'] == 3 and len(finished[model_id]['model']['payload']['nativeResults']) == 2 and
            len(finished[model_id]['model']['payload']['work']) == 2 and all(w['nativeWork']['workerExecutionId'] not in original_ids for w in finished[model_id]['model']['payload']['work']),
            'The original retry consumed a rearmed listener or lost an original result.')
    elif empty:
        require(not first['snapshot']['resumes'] and v.instant(cohort['selectedAt']) < v.instant(late['model']['createdAt']), 'The empty first selection did not precede the later model.')
    require(set(p['snapshots']) == {'pending', 'finished', 'retained'}, 'The model broadcast lacks a required complete crash/retention cut.')
    pins = {k: p['workerPublication'][k] for k in ('artifactId', 'artifactHash', 'artifactVersion', 'definitionId', 'definitionVersionId')}
    previous_materials = None
    for phase in ('pending', 'finished', 'retained'):
        expected = ({late_id: late} if empty else {initial['model']['executionId']: rearmed if rearm else initial, late_id: late}) if phase == 'pending' else finished
        require(set(p['snapshots'][phase]) == {'runtime', 'design'}, 'A coupled model broadcast store is missing.')
        for store, entry in p['snapshots'][phase].items():
            require(entry['file'] == phase + '-' + store + '.sqlite.gz' and v.digest(root / entry['file']) == entry['gzipSha256'], 'A captured broadcast store changed or escaped its bundle.')
            raw = gzip.decompress((root / entry['file']).read_bytes())
            require(hashlib.sha256(raw).hexdigest() == entry['databaseSha256'] == entry['observer']['snapshotSha256'] and entry['observer']['readOnly'], 'The observer receipt differs from its whole broadcast store.')
            with tempfile.TemporaryDirectory(prefix='elsa-broadcast-proof-') as temporary:
                path = Path(temporary) / 'snapshot.sqlite'; path.write_bytes(raw)
                with closing(sqlite3.connect(path.as_uri() + '?mode=ro&immutable=1', uri=True)) as connection:
                    require(connection.execute('PRAGMA integrity_check').fetchall() == [('ok',)], 'A whole broadcast store is corrupt.')
                    if store != 'runtime': continue
                    physical(connection, phase, expected, pins, first if phase == 'pending' else last, cohort, rearm)
                    materials = dict(connection.execute('SELECT ArtifactId,ContentJson FROM elsa_runtime_workflow_executable'))
                    if previous_materials is None: previous_materials = materials
                    else: require(all(materials.get(k) == value for k, value in previous_materials.items()), 'An original broadcast material or incarnation changed.')
    return {'cut': cut, 'sourceRevision': source, 'wholeStores': 6, 'ownedHosts': 4}


def physical(connection, phase, expected, pins, source, cohort, rearm):
    require(connection.execute('SELECT COUNT(*) FROM elsa_runtime_model_execution').fetchone()[0] == len(expected), 'The physical model set changed.')
    all_origins = {}
    for model_id, view in expected.items():
        model = view['model']; row, = connection.execute('SELECT Revision,Lifecycle,ContentJson FROM elsa_runtime_model_execution WHERE ExecutionId=?', (v.key(model_id),)).fetchall()
        body = json.loads(row[2])
        require(row[:2] == (model['revision'], model['lifecycle']) and body['payload'] == model['payload'] and body['executable'] == v.encoded(model['executable']) and
            body['materialIncarnationId'] == v.key(model['materialIncarnationId']), 'The physical model differs from its accepted broadcast decision.')
        require(connection.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?', (v.key(model_id),)).fetchone()[0] == 0,
            'A fabricated native model root appeared.')
        live = {w['nativeWork']['workerExecutionId']: w['nativeWork'] for w in model['payload']['work']}
        results = {r['origin']['workerExecutionId']: r for r in model['payload'].get('nativeResults', [])}
        all_origins.update(live); all_origins.update({identity: result['origin'] for identity, result in results.items()})
        for identity, origin in {**live, **{identity: result['origin'] for identity, result in results.items()}}.items():
            row, = connection.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?', (v.key(identity),)).fetchall(); worker = json.loads(row[1])
            require(row[0] == (3 if identity in results else 1) and worker['modelWorkOrigin'] == v.encoded(origin) and worker['pinnedExecutable'] == v.encoded(pins) and
                origin['executionId'] == model_id and origin['executable'] == model['executable'] and origin['materialIncarnationId'] == model['materialIncarnationId'],
                'A physical Event worker lost its exact original model address or pin.')
            bookmarks = list(connection.execute('SELECT BookmarkId,ContentJson FROM elsa_runtime_bookmark_state WHERE WorkflowExecutionId=?', (identity,)))
            require(len(bookmarks) == (0 if identity in results else 1), 'A current model work item has no exact native Event registration.')
            if identity in results:
                result = results[identity]; completion = result['completion']; require(result['status'] == 3 and result['faultCode'] is None and
                    completion['outcomeKey'] == 'Done' and completion['resultType']['alias'] == 'Elsa.Activities.Primitives.Activities.EventResult' and not completion['isRedacted'] and
                    completion['result'] == {'eventName': source['intentPayload']['eventName'], 'payload': None},
                    'An actual Event result was relabelled.')
                receipt, = connection.execute('SELECT ContentJson FROM elsa_runtime_model_input WHERE ExecutionId=? AND InputId=?', (v.key(model_id), v.key(origin['resultInputId']))).fetchall()
                accepted = json.loads(receipt[0]); delivery, = connection.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (v.key(result['sourceOutboxItemId']),)).fetchall()
                dispatched = json.loads(delivery[1]); require(accepted['result']['status'] == 'Accepted' and accepted['invocationId'] == v.key(origin['invocationId']) and
                    accepted['attemptId'] == v.key(origin['attemptId']) and dispatched['intent']['payload'] == result and
                    (delivery[0] != 2 or v.instant(dispatched['deliveredAt']) >= v.instant(accepted['acceptedAt'])) and (phase == 'pending' or delivery[0] == 2),
                    'A native result was acknowledged without exact model consumption.')
    require(connection.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state').fetchone()[0] == len(all_origins) + 1,
        'An unreported native worker or model carrier appeared.')
    row, = connection.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (v.key(source['outboxItemId']),)).fetchall(); body = json.loads(row[1])
    require(row[0] == (1 if phase == 'pending' else 2) and body['deliveryFencingToken'] == source['deliveryFencingToken'], 'The physical publisher lost its exact claim/ACK cut.')
    stored = body.get('recipientSnapshot')
    if source['snapshot'] is None: require(stored is None, 'First selection was installed before its delayed cut.')
    else:
        require(stored['sourceOutboxItemId'] == v.key(cohort['sourceOutboxItemId']) and stored['selectedAt'] == cohort['selectedAt'] and not stored['starts'] and
            len(stored['resumes']) == len(cohort['resumes']) and {(r['workflowExecutionId'], r['bookmarkId']) for r in stored['resumes']} ==
                {(v.key(r['workflowExecutionId']), v.key(r['bookmarkId'])) for r in cohort['resumes']}, 'The physical durable source cohort changed.')
        for recipient in cohort['resumes']:
            saved, = [r for r in stored['resumes'] if r['workflowExecutionId'] == v.key(recipient['workflowExecutionId'])]
            require(saved['registrationCapture']['generation'] == recipient['registrationCapture']['generation'] and
                saved['registrationCapture']['executable'] == v.encoded(recipient['registrationCapture']['executable']), 'The original native registration generation or pin changed.')


def verify(path):
    manifest = json.loads(path.read_text()); root = path.parent
    require(manifest['schemaVersion'] == 1 and manifest['kind'] == 'actual-model-broadcast-recovery-matrix' and len(manifest['cases']) == 3 and
        {case['cut'] for case in manifest['cases']} == set(CUTS), 'The actual broadcast matrix lost a required cut.')
    baseline = Path(__file__).resolve().parents[2] / 'specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json'
    frozen = json.loads(baseline.read_text())
    require(manifest['productionSourceFilesIdenticalBetweenExecutingCommits'] and v.digest(baseline) == manifest['baselineManifestSha256'] and
        len(frozen['assets']) == manifest['frozenBaselineAssetCount'] == 104 and all(v.digest(baseline.parent / a['path']) == a['sha256'] for a in frozen['assets']),
        'The frozen baseline or declared production source continuity changed.')
    for diagnostic in manifest['diagnostics']:
        require(diagnostic['qualifiesJourney'] is False, 'A failed prototype cannot qualify broadcast recovery.')
        for asset in diagnostic['assets']:
            name = Path(asset['path']); require(not name.is_absolute() and '..' not in name.parts and name.parts[0] == 'diagnostics' and v.digest(root / name) == asset['sha256'],
                'An actual broadcast diagnostic is missing or changed.')
    ci_root = root / 'main-ci'; ci = json.loads((ci_root / 'receipt.json').read_text())
    require(ci['runId'] == 40978933651 and ci['head'] == 'b9cf52c181b79e3e79eef233f7d61cb0843f4da7' and ci['conclusion'] == 'success' and
        ci['efContainerMatrix'] == 'skipped' and len(ci['outcomeArtifacts']) == 9, 'The base CI receipt lost its exact main run or skipped provider boundary.')
    quarantined = []
    for asset in ci['outcomeArtifacts']:
        name = Path(asset['file']); require(len(name.parts) == 1 and v.digest(ci_root / name) == asset['sha256'], 'A main CI outcome artifact changed.')
        outcome = json.loads((ci_root / name).read_text())
        require(outcome['headSha'] == ci['head'] and not outcome['failedTwice'] and not outcome['passedOnRetry'] and not outcome['aborted'], 'The base CI receipt hid a failed, retried or aborted test.')
        quarantined.extend(outcome['quarantinedFailed'])
    require(quarantined == ci['quarantinedFailures'] == [{'test': 'Elsa.Workbench.Tests.WorkbenchCompensationSampleTests.The_sample_workflows_end_as_the_readme_says', 'issue': 3261}],
        'The existing configured quarantine was hidden or relabelled as a passing test.')
    results = []
    for case in manifest['cases']:
        require(case['directory'] == case['cut'] and re.fullmatch('[0-9a-f]{40}', case['sourceRevision']), 'The actual broadcast case source/path is invalid.')
        directory = root / case['directory']
        require({asset['path'] for asset in case['assets']} == {p.name for p in directory.iterdir() if p.is_file()} and len({asset['path'] for asset in case['assets']}) == len(case['assets']), 'A raw broadcast asset is missing or duplicated.')
        for asset in case['assets']:
            name = Path(asset['path']); require(not name.is_absolute() and len(name.parts) == 1 and name.name not in ('.', '..') and v.digest(directory / name) == asset['sha256'], 'A raw broadcast asset changed or escaped its bundle.')
        results.append(verify_case(directory, case['cut'], case['sourceRevision']))
    return {'kind': manifest['kind'], 'cases': results, 'wholeStores': 18, 'ownedHosts': 12, 'finalUpgradeAndStudioProved': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('manifest', type=Path)
    print(json.dumps(verify(parser.parse_args().manifest), indent=2))

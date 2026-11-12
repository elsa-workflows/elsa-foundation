#!/usr/bin/env python3
"""Read-only checks of actual model-owned signal journeys and their whole captured stores."""
import argparse
import base64
from contextlib import closing
import gzip
import hashlib
import json
from pathlib import Path
import re
import sqlite3
import tempfile

PARENTS = ('direct', 'root', 'sequence', 'flowchart', 'compensationscope')


def require(condition, message):
    if not condition: raise ValueError(message)


def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def key(value): return base64.b64encode(value.encode('utf-16le')).decode()
def encoded(value):
    if isinstance(value, str): return key(value)
    if isinstance(value, list): return [encoded(item) for item in value]
    if isinstance(value, dict): return {name: encoded(item) for name, item in value.items()}
    return value


def instant(value):
    match = re.fullmatch(r'(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)', value)
    require(match is not None, 'A signal receipt has no UTC timestamp.')
    return match[1], (match[2] or '').ljust(7, '0')


def verify_case(root, parent, source):
    read = lambda name: json.loads((root / name).read_text())
    process = read('process.json'); embedded = parent != 'direct'
    require(process['schemaVersion'] == 1 and process['kind'] == 'actual-model-signal-recovery' and process['sourceRevision'] == source and
        process['parent'].lower() == parent and process['stateSeeded'] is False and len(process['processes']) == len(process['exits']) == 3 and
        len({p['processId'] for p in process['processes']}) == 3 and len({p['controllerProcessId'] for p in process['processes']}) == 1 and
        all(code not in (None, 0) for code in process['exits']) and all(instant(p['startedAtUtc']) for p in process['processes']),
        'The signal journey lost its exact source, parent or actual process-loss identities.')
    require(len(process['binaries']) == 11 and all(re.fullmatch('[0-9a-f]{64}', item['sha256']) and
        (item['productVersion'].endswith('+' + source) if item['file'].startswith('Elsa.') else item['productVersion'].startswith('0.5.0+'))
        for item in process['binaries']), 'The signal products differ from the executing source or released library.')
    before = read('waiting-checked.json'); final = read('finished-checked.json')
    require(before['readOnly'] and final['readOnly'] and before['model'] == read('recovered-checked.json')['model'] and final == read('retained-checked.json'),
        'An original signal wait or terminal result changed across a real restart.')
    first, last = before['model'], final['model']; work = first['payload']['work']
    origins = [item['nativeWork'] for item in work]; ids = process['workers']
    require(first['executionId'] == process['modelExecutionId'] and first['revision'] == 1 and first['lifecycle'] == 0 and
        first['payload']['formatVersion'] == (5 if embedded else 2) and len(work) == 2 and len(set(ids)) == 2 and
        set(ids) == {origin['workerExecutionId'] for origin in origins} and first['executable'] == process['published']['executable'] and
        first['materialIncarnationId'] == process['published']['incarnationId'] and last['executable'] == first['executable'] and
        last['materialIncarnationId'] == first['materialIncarnationId'] and last['revision'] == 3 + int(embedded) and last['lifecycle'] == 3 and
        last['terminalResult'] == {'status': 'Completed', 'outcome': 'Done'} and not last['payload']['work'] and not final['queuedModelInputs'] and
        all(item['status'] == 2 for item in final['sources']), 'The actual two catches lost their pinned model or final semantic result.')
    results = last['payload']['nativeResults']; receipts = [r for r in final['inputs'] if r['inputKind'] == 'native-result']
    require(len(results) == len(receipts) == 2 and {r['acceptedRevision'] for r in receipts} == {2, 3}, 'The model lost an exact signal consumption receipt.')
    pin = {name: process['worker'][name] for name in ('artifactId', 'artifactHash', 'artifactVersion', 'definitionId', 'definitionVersionId')}
    waiting = read('waiting-workers.json'); finished = read('finished-workers.json')
    for origin in origins:
        worker_id = origin['workerExecutionId']; old, = [w for w in waiting if w['worker']['workflowExecutionId'] == worker_id]
        new, = [w for w in finished if w['worker']['workflowExecutionId'] == worker_id]
        result, = [r for r in results if r['origin'] == origin]; receipt = new['resultInput']; delivery = new['resultSource']
        require(origin['executable'] == first['executable'] and origin['materialIncarnationId'] == first['materialIncarnationId'] and
            origin['executionId'] == first['executionId'] and origin['workerExecutable'] == pin and
            old['worker']['modelWorkOrigin'] == new['worker']['modelWorkOrigin'] == origin and old['worker']['pinnedExecutable'] == new['worker']['pinnedExecutable'] == pin and
            old['worker']['status'] == 1 and new['worker']['status'] == 3 and old['worker']['parentWorkflowExecutionId'] is None and
            old['worker']['authority'] == new['worker']['authority'] == first['authority'] and old['resultInput'] is old['resultSource'] is None,
            'A real Event worker lost its original exact model invocation, pin or authority.')
        completion = result['completion']
        require(result['status'] == 3 and result['faultCode'] is None and completion['outcomeKey'] == 'Done' and not completion['isRedacted'] and
            completion['resultType']['alias'] == 'Elsa.Activities.Primitives.Activities.EventResult' and
            completion['result'] == {'eventName': 'actual-model-release', 'payload': {'release': 'after-restart'}} and
            receipt['inputId'] == origin['resultInputId'] and receipt['invocationId'] == origin['invocationId'] and receipt['attemptId'] == origin['attemptId'] and
            receipt in receipts and receipt['result']['status'] == 'Accepted' and delivery['status'] == 2 and
            delivery['outboxItemId'] == result['sourceOutboxItemId'] and delivery['intent']['payload'] == result and
            instant(delivery['deliveredAt']) >= instant(receipt['acceptedAt']), 'A signal result was relabelled or acknowledged before exact model consumption.')
    broadcast = read('broadcast-checked.json'); cohort = broadcast['source']['snapshot']
    require(broadcast['providerChecked'] and broadcast['readOnly'] and broadcast['source']['workflowExecutionId'] == process['publisherId'] and
        broadcast['source']['status'] == 'Delivered' and not cohort['starts'] and len(cohort['resumes']) == 2 and
        {r['workflowExecutionId'] for r in cohort['resumes']} == set(ids) and all(r['registrationCapture']['executable'] == pin for r in cohort['resumes']),
        'The real publisher did not retain exactly the two original native Event recipients.')
    parent_result = None
    if embedded:
        parent_result = read('finished-parent.json'); native = parent_result['nativeParent']
        runs = [a for a in parent_result['activities'] if a['execution']['executableNodeId'] == 'run-bpmn']; run, = runs
        settlement, = [r for r in parent_result['inputs'] if r['inputKind'] == 'native-parent-settled']
        returned, = [s for s in parent_result['sources'] if s['intent']['kind'] == 'Elsa.Bpmn.ResumeNativeParent.v1']
        require(parent_result['readOnly'] and parent_result['model'] == last and native['workflowExecutionId'] == process['nativeParentExecutionId'] and
            native['status'] == 3 and not parent_result['bookmarks'] and not parent_result['queuedModelInputs'] and
            run['status'] == 4 and run['completion']['outcomeKey'] == 'Done' and run['completion']['resultType']['alias'] == 'Elsa.Activities.Bpmn.Models.RunBpmnResult' and
            run['completion']['result']['inlineValue'] == {'executionId': process['modelExecutionId'], 'result': last['terminalResult']} and
            settlement['acceptedRevision'] == 4 and settlement['result']['status'] == 'Accepted' and returned['status'] == 2 and
            instant(returned['deliveredAt']) >= instant(settlement['acceptedAt']), 'The native parent lost its typed result or settlement before acknowledgment.')
        if parent != 'root':
            require(len([a for a in parent_result['activities'] if a['execution']['executableNodeId'] == 'after' and a['status'] == 4]) == 1,
                'The actual native successor did not continue exactly once.')
    expected_files = {phase + '-' + store + '.sqlite.gz' for phase in ('waiting', 'finished', 'retained') for store in ('runtime', 'design')}
    require(len(process['stores']) == 6 and {s['file'] for s in process['stores']} == expected_files, 'A coupled signal store is missing or duplicated.')
    immutable = None
    for entry in sorted(process['stores'], key=lambda item: (('waiting', 'finished', 'retained').index(item['file'].split('-')[0]), item['file'])):
        require(digest(root / entry['file']) == entry['gzipSha256'], 'A compressed signal store changed.')
        raw = gzip.decompress((root / entry['file']).read_bytes())
        require(hashlib.sha256(raw).hexdigest() == entry['databaseSha256'], 'A whole signal store changed.')
        phase = entry['file'].split('-')[0]; terminal = phase != 'waiting'; model = last if terminal else first
        with tempfile.TemporaryDirectory(prefix='elsa-signal-proof-') as temporary:
            path = Path(temporary) / 'snapshot.sqlite'; path.write_bytes(raw)
            with closing(sqlite3.connect(path.as_uri() + '?mode=ro&immutable=1', uri=True)) as connection:
                require(connection.execute('PRAGMA integrity_check').fetchall() == [('ok',)], 'A signal store is corrupt.')
                if not entry['file'].endswith('-runtime.sqlite.gz'): continue
                row, = connection.execute('SELECT Revision,Lifecycle,ContentJson FROM elsa_runtime_model_execution').fetchall(); physical = json.loads(row[2])
                require(row[:2] == (model['revision'], model['lifecycle']) and physical['payload'] == model['payload'] and physical['executable'] == encoded(model['executable']) and
                    physical['materialIncarnationId'] == key(model['materialIncarnationId']), 'The physical model differs from its checked signal state.')
                require(connection.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?', (key(model['executionId']),)).fetchone()[0] == 0 and
                    connection.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state').fetchone()[0] == (3 if terminal else 2) + int(embedded),
                    'A fabricated native model root or extra Event worker appeared.')
                for origin in origins:
                    row, = connection.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?', (key(origin['workerExecutionId']),)).fetchall()
                    native = json.loads(row[1]); require(row[0] == (3 if terminal else 1) and native['modelWorkOrigin'] == encoded(origin) and native['pinnedExecutable'] == encoded(pin),
                        'The physical Event worker lost its exact model origin or outcome.')
                    recipient, = [r for r in cohort['resumes'] if r['workflowExecutionId'] == origin['workerExecutionId']]
                    count = connection.execute('SELECT COUNT(*) FROM elsa_runtime_bookmark_state WHERE WorkflowExecutionId=? AND BookmarkId=?',
                        (origin['workerExecutionId'], recipient['bookmarkId'])).fetchone()[0]
                    require(count == (0 if terminal else 1), 'The source cohort differs from the original captured Event registration.')
                materials = dict(connection.execute('SELECT ArtifactId,ContentJson FROM elsa_runtime_workflow_executable'))
                if phase == 'waiting': immutable = materials
                elif immutable is not None: require(all(materials.get(k) == v for k, v in immutable.items()), 'A retained original artifact was rewritten.')
                model_material = json.loads(materials[key(model['executable']['artifactId'])])['material']
                require(model_material['definition']['formatVersion'] == 5 and not model_material['definition']['humanBindings'] and
                    model_material['definition']['nativeEventBindings'] == [{'bindingId': binding, 'executable': pin} for binding in ('first-event', 'second-event')] and
                    model_material['dependencies'] == [{'bindingId': key(binding), 'executable': encoded(pin)} for binding in ('first-event', 'second-event')],
                    'The signal material lost its complete explicit native Event pins.')
                if terminal:
                    for result in results:
                        row, = connection.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (key(result['sourceOutboxItemId']),)).fetchall()
                        delivery = json.loads(row[1]); origin = result['origin']
                        receipt, = connection.execute('SELECT ContentJson FROM elsa_runtime_model_input WHERE InputId=?', (key(origin['resultInputId']),)).fetchall()
                        accepted = json.loads(receipt[0])
                        require(row[0] == 2 and delivery['intent']['payload'] == result and accepted['inputKind'] == key('native-result') and
                            accepted['executionId'] == key(model['executionId']) and accepted['invocationId'] == key(origin['invocationId']) and accepted['attemptId'] == key(origin['attemptId']) and
                            accepted['result']['status'] == 'Accepted' and instant(delivery['deliveredAt']) >= instant(accepted['acceptedAt']),
                            'A physical signal result lacks its exact consumption before acknowledgment.')
                    row, = connection.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?', (key(broadcast['source']['outboxItemId']),)).fetchall()
                    physical_cohort = json.loads(row[1])['recipientSnapshot']
                    require(row[0] == 2 and physical_cohort['sourceOutboxItemId'] == key(cohort['sourceOutboxItemId']) and
                        len(physical_cohort['resumes']) == 2 and not physical_cohort['starts'] and
                        {r['workflowExecutionId'] for r in physical_cohort['resumes']} == {key(i) for i in ids}, 'The physical durable broadcast cohort differs.')
    return {'parent': parent, 'wholeStores': 6, 'ownedProcesses': 3, 'nativeEventWorkers': 2}


def verify(path):
    manifest = json.loads(path.read_text()); root = path.parent
    require(manifest['schemaVersion'] == 1 and manifest['kind'] == 'actual-model-signal-recovery-matrix' and
        re.fullmatch('[0-9a-f]{40}', manifest['sourceRevision']) and {c['parent'] for c in manifest['cases']} == set(PARENTS) and len(manifest['cases']) == 5,
        'The signal matrix lost an exact source or required parent.')
    baseline = Path(__file__).resolve().parents[2] / 'specs/1017-direct-bpmn-hosting/evidence/baseline/manifest.json'
    frozen = json.loads(baseline.read_text())
    require(digest(baseline) == manifest['baselineManifestSha256'] and len(frozen['assets']) == manifest['frozenBaselineAssetCount'] == 104 and
        all(digest(baseline.parent / asset['path']) == asset['sha256'] for asset in frozen['assets']), 'The frozen authoring baseline changed.')
    for diagnostic in manifest['diagnostics']:
        require(diagnostic['qualifiesJourney'] is False, 'A diagnostic cannot qualify the signal journey.')
        for asset in diagnostic['assets']:
            name = Path(asset['path'])
            require(not name.is_absolute() and '..' not in name.parts and name.parts[0] == 'diagnostics' and digest(root / name) == asset['sha256'],
                'A retained signal diagnostic is missing or changed.')
    cases = []
    for case in manifest['cases']:
        require(case['directory'] == case['parent'] and len({a['path'] for a in case['assets']}) == len(case['assets']), 'The signal evidence paths are invalid or duplicated.')
        directory = root / case['directory']
        for asset in case['assets']:
            name = Path(asset['path']); require(len(name.parts) == 1 and not name.is_absolute() and name.name not in ('.', '..'), 'A signal asset escapes its bundle.')
            require(digest(directory / name) == asset['sha256'], 'A signal asset changed.')
        require({a['path'] for a in case['assets']} == {p.name for p in directory.iterdir() if p.is_file()}, 'The signal asset inventory is incomplete.')
        cases.append(verify_case(directory, case['parent'], manifest['sourceRevision']))
    return {'kind': manifest['kind'], 'sourceRevision': manifest['sourceRevision'], 'cases': cases, 'wholeStores': 30, 'ownedProcesses': 15,
        'laterListenerAndRearmRetryProved': False, 'finalUpgradeAndStudioProved': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('manifest', type=Path)
    print(json.dumps(verify(parser.parse_args().manifest), indent=2))

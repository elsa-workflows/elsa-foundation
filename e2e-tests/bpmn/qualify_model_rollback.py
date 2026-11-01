#!/usr/bin/env python3
"""Retire actual publications, collect an orphan, and recover owed native work with admission/Design off."""
import argparse
import base64
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import gzip
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import uuid

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('model_broadcast', Path(__file__).with_name('qualify_model_broadcast.py'))
b = importlib.util.module_from_spec(spec); spec.loader.exec_module(b); q = b.q; wait = b.wait


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('workbench', 'probe', 'reader', 'sdk'): parser.add_argument('--' + name + '-assembly', required=True, type=Path)
    parser.add_argument('--parent', choices=('Direct', 'Root', 'Sequence', 'Flowchart', 'CompensationScope'), required=True)
    parser.add_argument('--output', required=True, type=Path); args = parser.parse_args()
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=REPO, text=True).strip()
    if subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=all'], cwd=REPO, text=True).strip(): raise ValueError('Commit the exact journey source before qualification.')
    assemblies = {'probe': args.probe_assembly.resolve(strict=True), 'standard': args.workbench_assembly.resolve(strict=True)}
    reader = args.reader_assembly.resolve(strict=True); sdk = args.sdk_assembly.resolve(strict=True)
    def read(*arguments):
        result = subprocess.run(['dotnet', str(reader), *map(str, arguments)], text=True, capture_output=True, timeout=30)
        value = json.loads(result.stdout)
        if result.returncode: raise b.ObservationUnavailable(str(arguments[0]), value.get('failureType', 'UnclassifiedObserverFailure'))
        return value
    products = {name: read('--inspect-host-products', assembly) for name, assembly in assemblies.items()}
    if any(p['sourceCommit'] != source for p in products.values()): raise ValueError('Both host products must match the exact committed source.')
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    root = Path(tempfile.mkdtemp(prefix='elsa-model-rollback-')); control = root / 'probe'; control.mkdir(); (root / 'packages').mkdir()
    for name in ('appsettings.json', 'appsettings.Development.json', 'shells.json'): shutil.copyfile(REPO / 'Essentials/apps/Elsa.Workbench' / name, root / name)
    shells = json.loads((root / 'shells.json').read_text()); features = shells['CShells']['Shells']['default']['Features']
    features.update({'ActivitiesBpmnHosting': {'AdmitNewExecutions': True}, 'ActivitiesBpmnHostingApi': {}, 'WorkflowsRuntimeResumption': {'SweepIntervalSeconds': .2}})
    for name in ('WorkflowsRuntimeDistributed', 'WorkflowsRuntimeDistributedEntityFrameworkCorePersistence', 'WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence'): features.pop(name, None)
    features['WorkflowsRuntimeEntityFrameworkCore']['ConnectionString'] = 'Data Source=elsa.db;Pooling=False'
    for name in ('ActivitiesDesignEntityFrameworkCore', 'WorkflowsDesignEntityFrameworkCore', 'WorkflowsPublishingEntityFrameworkCore'): features[name]['ConnectionString'] = 'Data Source=design.db;Pooling=False'
    processes = []; exits = []; children = []; logs = []; snapshots = {}; refusals = []; host = None; api = None; native_parent = None; model_id = None; stage = 'publish'; runtime_only = False
    executor = None; submitted = None

    def owned(child):
        record = next(p for p in processes if p['processId'] == child.pid)
        if int(q.process_field(child.pid, 'ppid')) != os.getpid() or q.process_field(child.pid, 'lstart') != record['started'] or q.process_field(child.pid, 'comm') != record['executable']:
            raise ValueError('The owned host identity changed.')
    def stop(child):
        if child is None or child.poll() is not None: return
        owned(child); child.kill(); child.wait(timeout=30); exits.append({'processId': child.pid, 'exitCode': child.returncode})
    def launch(probe):
        nonlocal host, api
        role = 'probe' if probe else 'standard'
        if probe: features['BpmnRecoveryProbe'] = {'ControlDirectory': str(control)}
        else: features.pop('BpmnRecoveryProbe', None)
        q.write_json(root / 'shells.json', shells)
        path = root / ('host-' + str(len(processes)) + '.log'); log = path.open('w'); logs.append(log)
        host = subprocess.Popen(['dotnet', str(assemblies[role]), '--contentRoot', str(root), '--urls', 'http://127.0.0.1:0'], cwd=root,
            env={**os.environ, 'ASPNETCORE_ENVIRONMENT': 'Development'}, stdout=log, stderr=subprocess.STDOUT)
        children.append(host); processes.append({'role': role, 'processId': host.pid, 'controllerProcessId': os.getpid(),
            'started': q.process_field(host.pid, 'lstart'), 'executable': q.process_field(host.pid, 'comm'), 'admissionEnabled': not runtime_only, 'runtimeOnly': runtime_only})
        def address():
            if host.poll() is not None: raise RuntimeError('The owned host exited; private logs: ' + str(root))
            matches = [line.split('Now listening on: ', 1)[1].strip() for line in path.read_text().splitlines() if 'Now listening on: ' in line]
            return matches[0] if matches else None
        url = wait(address, bool, 'The owned host did not listen.'); api = q.Api(url)
        def healthy():
            try:
                with urllib.request.urlopen(url + '/health/ready', timeout=3) as response: return response.status == 200
            except urllib.error.HTTPError as error:
                if error.code == 503: return False
                raise
        wait(healthy, bool, 'The owned host did not become ready.'); api.login('admin', 'Password123!')
        if runtime_only and (root / 'design.db').exists(): raise ValueError('A Design database was recreated after removing its composition.')
    def capture(label):
        for attempt in range(1, 4):
            owned(host); os.kill(host.pid, signal.SIGSTOP)
            try:
                wait(lambda: 'T' in q.process_field(host.pid, 'stat'), bool, 'The owned host did not freeze.', 5)
                rows = []
                for store, name in [('runtime', 'elsa.db')] + ([] if runtime_only else [('design', 'design.db')]):
                    raw = root / (label + '-' + str(attempt) + '-' + store + '.sqlite')
                    rows.append((store, raw, read('--backup-sqlite', root / name, raw)))
            except b.ObservationUnavailable as error:
                refusals.append({'label': label, 'attempt': attempt, 'operation': error.operation, 'failureType': error.failure_type, 'sourceStateEdited': False})
                q.write_json(output / 'observer-unavailable.json', refusals)
                if error.failure_type != 'SqliteException' or attempt == 3: raise
            else:
                snapshots[label] = {}
                for store, raw, receipt in rows:
                    path = output / (label + '-' + store + '.sqlite.gz'); path.write_bytes(gzip.compress(raw.read_bytes()))
                    snapshots[label][store] = {'file': path.name, 'databaseSha256': q.digest(raw), 'gzipSha256': q.digest(path), 'observer': receipt}
                return
            finally: owned(host); os.kill(host.pid, signal.SIGCONT)
            time.sleep(.1)
    def node(identity, kind, inputs=None, structure=None): return q.node(identity, api.activity(kind), inputs, structure)
    def publish(name, root_node):
        draft = api.request('/design/workflows/definitions/submit', {'name': name, 'state': {'variables': [], 'inputs': [], 'outputs': [], 'rootActivity': root_node}})
        return api.request('/publishing/workflows/' + draft['version']['id'] + '/publish', {})
    def simple(process_id): return {'formatVersion': 1, 'humanBindings': ['gate'], 'process': {'processId': process_id,
        'elements': [{'elementId': 'start', 'elementType': 'startEvent'}, {'elementId': 'gate', 'elementType': 'userTask', 'bindingRef': 'gate'}, {'elementId': 'end', 'elementType': 'endEvent'}],
        'sequenceFlows': [{'flowId': 'first', 'sourceRef': 'start', 'targetRef': 'gate'}, {'flowId': 'last', 'sourceRef': 'gate', 'targetRef': 'end'}]}}
    def model(): return read('--read-bpmn-direct', root / 'elsa.db', model_id)
    def workers(): return [read('--read-bpmn-native', root / 'elsa.db', model_id, identity) for identity in worker_ids]
    def parent(): return read('--read-bpmn-embedding', root / 'elsa.db', model_id, native_parent)
    def wait_workers():
        result = []
        for identity in worker_ids:
            try: view = api.view(identity)
            except RuntimeError as error:
                if str(error).endswith('returned HTTP 404.'): return None
                raise
            if view['incidents']: raise ValueError('The actual Event worker faulted during runtime-only recovery.')
            if not view['activities']: return None
            detail = api.request('/runtime/workflows/instances/' + identity + '/activity-executions/' + view['activities'][0]['activityExecutionId'])
            if len(detail['bookmarks']) != 1: return None
            result.append({'view': view, 'detail': detail})
        return result
    def observations(phase):
        q.write_json(output / (phase + '-model.json'), model()); q.write_json(output / (phase + '-workers.json'), workers())
        if native_parent: q.write_json(output / (phase + '-parent.json'), parent())
        q.write_json(output / (phase + '-source.json'), read('--read-post-commit-source', root / 'elsa.db', marker['sourceOutboxItemId']))

    try:
        launch(True); event = 'actual-model-rollback-' + args.parent
        worker = publish('Actual rollback Event worker', node('event', 'Elsa.Activities.Primitives.Activities.Event', [q.literal('EventName', event), q.literal('CanStartWorkflow', False)]))
        pin = {k: worker[k] for k in ('artifactId', 'artifactHash', 'artifactVersion', 'definitionId', 'definitionVersionId')}
        publisher = publish('Actual runtime-only signal publisher', node('publish', 'Elsa.Activities.Primitives.Activities.PublishEvent', [q.literal('EventName', event), q.literal('Payload', {'release': 'admission-and-design-off'})]))
        definition = {'formatVersion': 5, 'humanBindings': [], 'nativeEventBindings': [{'bindingId': name + '-event', 'executable': pin} for name in ('first', 'second')], 'process': {
            'processId': 'actual-rollback-' + args.parent,
            'elements': [{'elementId': 'start', 'elementType': 'startEvent'}, {'elementId': 'fork', 'elementType': 'parallelGateway'}] + [
                {'elementId': name, 'elementType': 'intermediateCatchEvent', 'bindingRef': name + '-event', 'eventDefinitions': [{'type': 'signal', 'properties': {'name': event}}]}for name in ('first', 'second')] +
                [{'elementId': 'join', 'elementType': 'parallelGateway'}, {'elementId': 'end', 'elementType': 'endEvent'}],
            'sequenceFlows': [{'flowId': 'begin', 'sourceRef': 'start', 'targetRef': 'fork'}] + [{'flowId': 'fork-' + name, 'sourceRef': 'fork', 'targetRef': name}for name in ('first', 'second')] +
                [{'flowId': name + '-join', 'sourceRef': name, 'targetRef': 'join'}for name in ('first', 'second')] + [{'flowId': 'finish', 'sourceRef': 'join', 'targetRef': 'end'}]}}
        published = api.request('/bpmn/artifacts', {'requestId': 'publish-actual-rollback', 'definition': definition})
        orphan = api.request('/bpmn/artifacts', {'requestId': 'unreferenced-control', 'definition': simple('unreferenced-control')})
        denied = api.request('/bpmn/artifacts', {'requestId': 'refuse-new-after-rollback', 'definition': simple('refuse-new-after-rollback')})
        parent_publication = None
        if args.parent != 'Direct':
            invoke = node('run-bpmn', 'Elsa.Activities.Bpmn.Activities.RunBpmn', [q.literal('Model', published['sourceReferenceId'])]); parent_node = invoke
            if args.parent != 'Root':
                after = node('after', 'Elsa.Activities.Primitives.Activities.WriteLine', [q.literal('text', 'The retained native parent completes after rollback.')])
                if args.parent == 'Sequence': structure = {'kind': 'elsa.sequence.structure', 'schemaVersion': '1.0.0', 'payload': {'activities': [invoke, after]}}
                elif args.parent == 'Flowchart': structure = {'kind': 'elsa.flowchart.structure', 'schemaVersion': '1.0.0', 'payload': {'startNodeId': 'run-bpmn', 'activities': [invoke, after], 'connections': [{'id': 'after', 'source': {'nodeId': 'run-bpmn', 'port': 'Done'}, 'target': {'nodeId': 'after'}}]}}
                else: structure = {'kind': 'elsa.compensation-scope.structure', 'schemaVersion': '1.0.0', 'payload': {'steps': [{'name': 'process', 'activity': invoke}, {'name': 'after', 'activity': after}]}}
                parent_node = node('native-parent', 'Elsa.Activities.' + ('Compensation'if args.parent == 'CompensationScope'else args.parent) + '.Activities.' + args.parent, structure=structure)
            parent_publication = publish('Actual rollback parent ' + args.parent, parent_node)
        q.write_json(control / 'plan.json', {'caseId': 'before-worker-admission', 'phase': 'before-native-start', 'eventName': ''})
        executor = ThreadPoolExecutor(max_workers=1)
        if args.parent == 'Direct': pending = executor.submit(api.request, '/bpmn/executions', {'requestId': 'actual-model-rollback', 'sourceReferenceId': published['sourceReferenceId']})
        else: pending = executor.submit(api.request, '/runtime/workflows/executables/' + parent_publication['artifactId'] + '/execute', {'sourceReferenceId': parent_publication['sourceReferenceId']})
        wait((control / 'before-worker-admission/reached.json').exists, bool, 'The actual committed native-start source did not reach its dispatch cut.', 90)
        marker = json.loads((control / 'before-worker-admission/reached.json').read_text()); model_id = marker['workflowExecutionId']
        q.write_json(output / 'pending-marker.json', marker); initial = model(); worker_ids = [w['nativeWork']['workerExecutionId']for w in initial['model']['payload']['work']]
        if len(worker_ids) != 2 or initial['model']['revision'] != 1 or initial['nativeWorkflowRows'] != int(args.parent != 'Direct'): raise ValueError('Native work was admitted before the claimed dispatch cut.')
        if args.parent != 'Direct':
            encoded_parent = initial['model']['payload']['nativeParent']['invocation']['nativeExecutionId']
            if not encoded_parent.startswith('u16:'): raise ValueError('The retained native invocation lost its lossless identity encoding.')
            native_parent = base64.b64decode(encoded_parent[4:], validate=True).decode('utf-16le')
        q.write_json(output / 'initial-model.json', initial)
        if native_parent: q.write_json(output / 'pending-parent.json', parent())
        retirement = [{'sourceReferenceId': published['sourceReferenceId'], 'executable': published['executable']},
            {'sourceReferenceId': worker['sourceReferenceId'], 'executable': pin}, {'sourceReferenceId': orphan['sourceReferenceId'], 'executable': orphan['executable']}]
        if parent_publication: retirement.append({'sourceReferenceId': parent_publication['sourceReferenceId'], 'executable': {k: parent_publication[k]for k in pin}})
        nonce = uuid.uuid4().hex
        q.write_json(root / 'owned-bpmn-recovery.json', {'schemaVersion': 1, 'nonce': nonce, 'controllerProcessId': os.getpid(), 'modelExecutionId': model_id,
            'retirementSources': retirement, 'orphanArtifactId': orphan['executable']['artifactId']})
        stage = 'retire-and-collect'; result = subprocess.run(['dotnet', str(sdk), '--retire-and-sweep', str(root), model_id, nonce], text=True, capture_output=True, timeout=60)
        if result.returncode: raise ValueError('The normal retirement/GC SDK refused: ' + result.stdout[-1500:])
        collection = json.loads(result.stdout); q.write_json(output / 'collection.json', collection)
        if any(not x['informationalVersion'].endswith('+' + source)for x in collection['products']): raise ValueError('The real retirement SDK products differ from the committed journey source.')
        after_collection = model()
        if after_collection['model'] != initial['model']: raise ValueError('Collection changed the original model decision.')
        q.write_json(output / 'pending-model.json', after_collection)
        q.write_json(output / 'pending-source.json', read('--read-post-commit-source', root / 'elsa.db', marker['sourceOutboxItemId']))
        capture('pending'); captured = datetime.now(timezone.utc).isoformat()
        expiry = re.sub(r'\.(\d{1,7})(?=[+-])', lambda m: '.' + m[1].ljust(6, '0')[:6], marker['visibleAfter'].replace('Z', '+00:00'))
        if datetime.now(timezone.utc) >= datetime.fromisoformat(expiry): raise ValueError('The actual claim expired before the retained crash cut.')
        stop(host)
        try: submitted = pending.result(timeout=5)
        except (OSError, TimeoutError, urllib.error.URLError, RuntimeError): submitted = None
        executor.shutdown(wait=True); executor = None
        stage = 'disable-admission-and-design'; unavailable = root / 'unavailable-design'; unavailable.mkdir()
        for path in list(root.glob('design.db*')): shutil.move(path, unavailable / path.name)
        allowed = {'ActivitiesBpmnHosting', 'ActivitiesBpmnHostingApi', 'ActivitiesBpmn', 'ActivitiesRuntime', 'ActivitiesPrimitives', 'ActivitiesSequence', 'ActivitiesFlowchart',
            'ActivitiesCompensation', 'ActivitiesControlFlow', 'ActivitiesDispatchWorkflowRuntime', 'ActivitiesGraphRuntime', 'Events', 'Expressions', 'FileSystemDistributedLocking', 'Primitives',
            'Serialization', 'Tasks', 'ApiCapabilities', 'WorkflowsRuntimeApi', 'WorkflowsRuntimeEntityFrameworkCore', 'WorkflowsRuntimeResumption', 'WorkflowsRuntimeTriggers', 'WorkflowsRuntimeCheckpointPersistence',
            'FoundationIdentityAbstractions', 'FoundationIdentityApi', 'FoundationIdentityAspNetCoreIdentity', 'FoundationIdentityAspNetCoreIdentityEntityFrameworkCore', 'IdentityIamEntityFrameworkCore', 'IdentityProviderConfigurationEntityFrameworkCore'}
        for name in list(features):
            if name not in allowed: features.pop(name)
        features['ActivitiesBpmnHosting']['AdmitNewExecutions'] = False; runtime_only = True
        q.write_json(output / 'runtime-only-features.json', features); launch(False); stage = 'recover-native-work'
        def retained_wait(value): return value['model'] == initial['model'] and all(s['status'] == 2 for s in value['sources'])
        wait(model, retained_wait, 'The original committed native-start obligations did not recover with admission/Design off.', 240)
        q.write_json(output / 'recovered-native-waits.json', wait(wait_workers, bool, 'Both original Event workers did not arm after rollback.'))
        observations('recovered'); recovered_source = read('--read-post-commit-source', root / 'elsa.db', marker['sourceOutboxItemId'])['source']
        if recovered_source['deliveryFencingToken'] <= marker['fencingToken']: raise ValueError('Recovery did not reclaim the original committed native-start source.')
        request = urllib.request.Request(api.base + '/bpmn/executions', data=json.dumps({'requestId': 'new-start-after-rollback', 'sourceReferenceId': denied['sourceReferenceId']}).encode(), headers={'Content-Type': 'application/json'})
        try:
            with api.opener.open(request, timeout=30) as response: raise ValueError('Admission-off accepted a new model: HTTP ' + str(response.status))
        except urllib.error.HTTPError as error:
            denied_status = error.code; denied_body = json.loads(error.read())
            if error.code != 503: raise ValueError('Admission-off did not return its explicit availability refusal: HTTP ' + str(error.code))
        q.write_json(output / 'new-admission-refused.json', {'status': denied_status, 'body': denied_body})
        capture('recovered'); stage = 'publish-original-signal'
        publisher_id = api.request('/runtime/workflows/executables/' + publisher['artifactId'] + '/execute', {'sourceReferenceId': publisher['sourceReferenceId']})['workflowExecutionId']
        def terminal(value):
            if value['model']['lifecycle'] != 3 or value['queuedModelInputs'] or any(s['status'] != 2 for s in value['sources']): return False
            if native_parent:
                view = api.view(native_parent)
                if view['instance']['status'] != 'Completed': return False
                if args.parent != 'Root' and len([a for a in view['activities']if a['executableNodeId'] == 'after' and a['status'] == 'Completed']) != 1: return False
            return True
        finished = wait(model, terminal, 'The accepted model did not complete through the real runtime-only signal.', 240)
        wait(workers, lambda values: all(x['worker']['status'] == 3 and x['resultSource'] is not None and x['resultSource']['status'] == 2 for x in values), 'The actual Event result sources did not acknowledge.')
        observations('finished'); q.write_json(output / 'publisher.json', api.view(publisher_id)); capture('finished'); stop(host)
        stage = 'retain-terminal'; launch(False); observations('retained')
        if model() != finished: raise ValueError('The completed accepted model changed after another admission-off/runtime-only host.')
        capture('retained'); stop(host)
        if (root / 'design.db').exists(): raise ValueError('The Design store was recreated during runtime-only recovery.')
        q.write_json(output / 'process.json', {'schemaVersion': 1, 'kind': 'actual-model-retention-and-admission-rollback', 'sourceRevision': source, 'parent': args.parent,
            'modelExecutionId': model_id, 'nativeParentExecutionId': native_parent, 'workers': worker_ids, 'published': published, 'workerPublication': worker,
            'publisherPublication': publisher, 'publisherExecutionId': publisher_id, 'parentPublication': parent_publication, 'orphanPublication': orphan, 'deniedPublication': denied,
            'processes': processes, 'exits': exits, 'products': products, 'snapshots': snapshots, 'observerUnavailable': refusals, 'pendingCapturedAtUtc': captured,
            'submitted': submitted, 'newAdmissionHttpStatus': denied_status, 'designDatabaseRecreated': False, 'executionStateEdited': False, 'claimEdited': False, 'clockEdited': False})
        print(json.dumps({'sourceRevision': source, 'parent': args.parent, 'wholeStores': 5, 'ownedHosts': 3, 'newAdmissionHttpStatus': denied_status, 'orphanCollected': True}))
    except Exception as error:
        capture_failure = None
        if host is not None and host.poll() is None:
            try: capture('failed')
            except Exception as capture_error: capture_failure = type(capture_error).__name__
            finally: stop(host)
        q.write_json(output / 'failure.json', {'sourceRevision': source, 'parent': args.parent, 'stage': stage, 'failureType': type(error).__name__, 'failureCaptureError': capture_failure,
            'modelExecutionId': model_id, 'processes': processes, 'exits': exits, 'snapshots': snapshots, 'observerUnavailable': refusals, 'qualifiesJourney': False})
        raise
    finally:
        for child in reversed(children): stop(child)
        if executor: executor.shutdown(wait=True)
        for log in logs: log.close()


if __name__ == '__main__': main()

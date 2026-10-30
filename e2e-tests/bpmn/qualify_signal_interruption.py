#!/usr/bin/env python3
"""Real direct/RunBpmn sibling cancellation with a crash before pending cancellation dispatch."""
import argparse
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
import urllib.error
import urllib.request

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('model_broadcast', Path(__file__).with_name('qualify_model_broadcast.py'))
b = importlib.util.module_from_spec(spec); spec.loader.exec_module(b); q = b.q; wait = b.wait


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workbench-assembly', required=True, type=Path)
    parser.add_argument('--probe-assembly', required=True, type=Path)
    parser.add_argument('--reader-assembly', required=True, type=Path)
    parser.add_argument('--parent', choices=('Direct', 'Root', 'Sequence', 'Flowchart', 'CompensationScope'), required=True)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args(); source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=REPO, text=True).strip()
    if subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=all'], cwd=REPO, text=True).strip(): raise ValueError('Commit the journey source before qualification.')
    assemblies = {'probe': args.probe_assembly.resolve(strict=True), 'standard': args.workbench_assembly.resolve(strict=True)}; reader = args.reader_assembly.resolve(strict=True)
    def read(*arguments):
        result = subprocess.run(['dotnet', str(reader), *map(str, arguments)], text=True, capture_output=True, timeout=30)
        value = json.loads(result.stdout)
        if result.returncode: raise b.ObservationUnavailable(str(arguments[0]), value.get('failureType', 'UnclassifiedObserverFailure'))
        return value
    products = {name: read('--inspect-host-products', assembly) for name, assembly in assemblies.items()}
    if any(p['sourceCommit'] != source for p in products.values()): raise ValueError('Both actual host products must match the full committed source.')
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    root = Path(tempfile.mkdtemp(prefix='elsa-signal-interruption-')); control = root / 'probe'; control.mkdir(); (root / 'packages').mkdir()
    for name in ('appsettings.json', 'appsettings.Development.json', 'shells.json'): shutil.copyfile(REPO / 'Essentials/apps/Elsa.Workbench' / name, root / name)
    shells = json.loads((root / 'shells.json').read_text()); features = shells['CShells']['Shells']['default']['Features']
    features.update({'ActivitiesBpmnHosting': {'AdmitNewExecutions': True}, 'ActivitiesBpmnHostingApi': {}, 'WorkflowsRuntimeResumption': {'SweepIntervalSeconds': .2}})
    for name in ('WorkflowsRuntimeDistributed', 'WorkflowsRuntimeDistributedEntityFrameworkCorePersistence', 'WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence'): features.pop(name, None)
    features['WorkflowsRuntimeEntityFrameworkCore']['ConnectionString'] = 'Data Source=runtime.db;Pooling=False'
    for name in ('ActivitiesDesignEntityFrameworkCore', 'WorkflowsDesignEntityFrameworkCore', 'WorkflowsPublishingEntityFrameworkCore'): features[name]['ConnectionString'] = 'Data Source=design.db;Pooling=False'
    children = []; processes = []; exits = []; logs = []; snapshots = {}; host = None; api = None; native_parent = None; stage = 'start'; observations = 0

    def owned(child):
        record = next(p for p in processes if p['processId'] == child.pid)
        if int(q.process_field(child.pid, 'ppid')) != os.getpid() or q.process_field(child.pid, 'lstart') != record['started']: raise ValueError('The owned host identity changed.')
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
        children.append(host); processes.append({'role': role, 'processId': host.pid, 'controllerProcessId': os.getpid(), 'started': q.process_field(host.pid, 'lstart')})
        def address():
            if host.poll() is not None: raise RuntimeError('The owned host exited; private logs: ' + str(root))
            values = [line.split('Now listening on: ', 1)[1].strip() for line in path.read_text().splitlines() if 'Now listening on: ' in line]
            return values[0] if values else None
        url = wait(address, bool, 'The owned host did not listen.'); api = q.Api(url)
        def healthy():
            try:
                with urllib.request.urlopen(url + '/health/ready', timeout=3) as response: return response.status == 200
            except urllib.error.HTTPError as error:
                if error.code == 503: return False
                raise
        wait(healthy, bool, 'The owned host did not become ready.'); api.login('admin', 'Password123!')
    def capture(label):
        owned(host); os.kill(host.pid, signal.SIGSTOP)
        try:
            wait(lambda: 'T' in q.process_field(host.pid, 'stat'), bool, 'The owned host did not stop for capture.', 5)
            rows = {}
            for store in ('runtime', 'design'):
                raw = root / (label + '-' + store + '.sqlite'); receipt = read('--backup-sqlite', root / (store + '.db'), raw)
                path = output / (label + '-' + store + '.sqlite.gz'); path.write_bytes(gzip.compress(raw.read_bytes()))
                rows[store] = {'file': path.name, 'databaseSha256': q.digest(raw), 'gzipSha256': q.digest(path), 'observer': receipt}
            snapshots[label] = rows
        finally: owned(host); os.kill(host.pid, signal.SIGCONT)
    def node(identity, kind, inputs=None, structure=None): return q.node(identity, api.activity(kind), inputs, structure)
    def publish(name, root_node):
        draft = api.request('/design/workflows/definitions/submit', {'name': name, 'state': {'variables': [], 'inputs': [], 'outputs': [], 'rootActivity': root_node}})
        return api.request('/publishing/workflows/' + draft['version']['id'] + '/publish', {})
    def model(): return read('--read-bpmn-direct', root / 'runtime.db', model_id)
    def wait_workers(state):
        observations = {}
        for work in state['model']['payload']['work']:
            identity = work['nativeWork']['workerExecutionId']
            try: view = api.view(identity)
            except RuntimeError as error:
                if str(error).endswith('returned HTTP 404.'): return None
                raise
            if view['incidents']: raise ValueError('The actual Event worker faulted.')
            if not view['activities']: return None
            detail = api.request('/runtime/workflows/instances/' + identity + '/activity-executions/' + view['activities'][0]['activityExecutionId'])
            if len(detail['bookmarks']) != 1: return None
            observations[identity] = {'view': view, 'detail': detail}
        return observations
    def source_item(identity): return read('--read-post-commit-source', root / 'runtime.db', identity)
    def checked_workers(): return [read('--read-bpmn-native', root / 'runtime.db', model_id, identity) for identity in workers]

    try:
        launch(True); event = 'actual-sibling-interruption-' + args.parent
        worker_publication = publish('Actual sibling interruption worker', node('event', 'Elsa.Activities.Primitives.Activities.Event', [q.literal('EventName', event), q.literal('CanStartWorkflow', False)]))
        pin = {name: worker_publication[name] for name in ('artifactId', 'artifactHash', 'artifactVersion', 'definitionId', 'definitionVersionId')}
        elements = [{'elementId': 'start', 'elementType': 'startEvent'}, {'elementId': 'fork', 'elementType': 'parallelGateway'}] + [
            {'elementId': name, 'elementType': 'intermediateCatchEvent', 'bindingRef': name + '-event', 'eventDefinitions': [{'type': 'signal', 'properties': {'name': event}}]} for name in ('first', 'second')] + [
            {'elementId': 'join', 'elementType': 'parallelGateway'}, {'elementId': 'end', 'elementType': 'endEvent'}]
        flows = [{'flowId': 'begin', 'sourceRef': 'start', 'targetRef': 'fork'}] + [{'flowId': 'fork-' + name, 'sourceRef': 'fork', 'targetRef': name} for name in ('first', 'second')] + [
            {'flowId': name + '-join', 'sourceRef': name, 'targetRef': 'join'} for name in ('first', 'second')] + [{'flowId': 'finish', 'sourceRef': 'join', 'targetRef': 'end'}]
        publication = api.request('/bpmn/artifacts', {'requestId': 'publish-model', 'definition': {'formatVersion': 5, 'humanBindings': [],
            'nativeEventBindings': [{'bindingId': name + '-event', 'executable': pin} for name in ('first', 'second')], 'process': {'processId': 'actual-sibling-interruption', 'elements': elements, 'sequenceFlows': flows}}})
        parent_publication = None
        if args.parent == 'Direct': model_id = api.request('/bpmn/executions', {'requestId': 'initial-model', 'sourceReferenceId': publication['sourceReferenceId']})['executionId']
        else:
            invoke = node('run-bpmn', 'Elsa.Activities.Bpmn.Activities.RunBpmn', [q.literal('Model', publication['sourceReferenceId'])]); parent_node = invoke
            if args.parent != 'Root':
                after = node('after', 'Elsa.Activities.Primitives.Activities.WriteLine', [q.literal('text', 'This successor must not run after cancellation.')])
                if args.parent == 'Sequence': structure = {'kind': 'elsa.sequence.structure', 'schemaVersion': '1.0.0', 'payload': {'activities': [invoke, after]}}
                elif args.parent == 'Flowchart': structure = {'kind': 'elsa.flowchart.structure', 'schemaVersion': '1.0.0', 'payload': {'startNodeId': 'run-bpmn', 'activities': [invoke, after], 'connections': [{'id': 'after', 'source': {'nodeId': 'run-bpmn', 'port': 'Done'}, 'target': {'nodeId': 'after'}}]}}
                else: structure = {'kind': 'elsa.compensation-scope.structure', 'schemaVersion': '1.0.0', 'payload': {'steps': [{'name': 'process', 'activity': invoke}, {'name': 'after', 'activity': after}]}}
                parent_node = node('native-parent', 'Elsa.Activities.' + ('Compensation' if args.parent == 'CompensationScope' else args.parent) + '.Activities.' + args.parent, structure=structure)
            parent_publication = publish('Actual sibling interruption parent ' + args.parent, parent_node)
            native_parent = api.request('/runtime/workflows/executables/' + parent_publication['artifactId'] + '/execute', {'sourceReferenceId': parent_publication['sourceReferenceId']})['workflowExecutionId']
            def embedded():
                parent = api.view(native_parent)
                if parent['incidents']: raise ValueError('The actual native RunBpmn parent faulted before its wait.')
                matches = [a for a in parent['activities'] if a['activityType'] == 'Elsa.Activities.Bpmn.Activities.RunBpmn']
                if not matches: return None
                detail = api.request('/runtime/workflows/instances/' + native_parent + '/activity-executions/' + matches[0]['activityExecutionId'])
                if len(detail['bookmarks']) != 1: return None
                identity = detail['bookmarks'][0]['stimulusHash']
                try: api.request('/bpmn/executions/' + identity)
                except RuntimeError as error:
                    if str(error).endswith('returned HTTP 404.'): return None
                    raise
                return identity
            model_id = wait(embedded, bool, 'The real native parent did not admit its model.')
        initial = model(); workers = [w['nativeWork']['workerExecutionId'] for w in initial['model']['payload']['work']]
        if len(workers) != 2: raise ValueError('The actual model did not start both signal catches.')
        q.write_json(output / 'initial.json', initial); q.write_json(output / 'initial-native-waits.json', wait(lambda: wait_workers(initial), bool, 'The actual Event workers did not arm.'))
        q.write_json(control / 'plan.json', {'caseId': 'pending-sibling', 'phase': 'before-native-cancel', 'eventName': '', 'targetExecutionId': model_id})
        stage = 'cancel-one-worker'; executor = ThreadPoolExecutor(max_workers=1)
        def cancel():
            request = urllib.request.Request(api.base + '/runtime/workflows/alteration-plans', data=json.dumps({'target': {'executionIds': [workers[0]]},
                'alterations': [{'kind': 'CancelWorkflow', 'schemaVersion': 1, 'payload': {}}]}).encode(), headers={'Content-Type': 'application/json', 'Idempotency-Key': 'cancel-one-event-before-pending-crash'})
            with api.opener.open(request, timeout=240) as response: return json.load(response)
        pending = executor.submit(cancel); marker_path = control / 'pending-sibling/reached.json'
        wait(marker_path.exists, bool, 'The real sibling cancellation did not reach its committed pre-dispatch cut.', 60)
        marker = json.loads(marker_path.read_text()); q.write_json(output / 'pending-marker.json', marker)
        interrupted = model(); q.write_json(output / 'pending-model.json', interrupted); q.write_json(output / 'pending-workers.json', checked_workers())
        if interrupted['model']['revision'] != 2 or interrupted['model']['lifecycle'] != 2 or interrupted['model']['terminalResult'] != {'status': 'Cancelled'}: raise ValueError('The model did not retain a real pending sibling cancellation.')
        if native_parent: q.write_json(output / 'pending-parent.json', read('--read-bpmn-embedding', root / 'runtime.db', model_id, native_parent))
        q.write_json(output / 'pending-source.json', source_item(marker['sourceOutboxItemId']))
        capture('pending'); pending_at = datetime.now(timezone.utc).isoformat()
        deadline = re.sub(r'\.(\d{1,7})(?=[+-])', lambda m: '.' + m[1].ljust(6, '0')[:6], marker['visibleAfter'].replace('Z', '+00:00'))
        if datetime.now(timezone.utc) >= datetime.fromisoformat(deadline): raise ValueError('The original pending claim expired before its crash capture.')
        stop(host)
        try: submitted = pending.result(timeout=5)
        except (OSError, TimeoutError, urllib.error.URLError): submitted = None
        executor.shutdown(wait=True); stage = 'recover-pending'; launch(False)
        def settled(value):
            if value['model']['lifecycle'] != 3 or value['queuedModelInputs'] or any(s['status'] != 2 for s in value['sources']): return False
            if native_parent:
                parent = api.view(native_parent)
                if parent['instance']['status'] != 'Cancelled' or any(a['executableNodeId'] == 'after' for a in parent['activities']): return False
            return True
        finished = wait(model, settled, 'The actual pending model/parent cancellation did not settle after recovery.', 240)
        completed_workers = wait(checked_workers, lambda values: all(x['worker']['status'] == 5 and x['resultSource'] is not None and x['resultSource']['status'] == 2 for x in values), 'The original native result sources did not acknowledge.')
        completed_source = source_item(marker['sourceOutboxItemId'])
        if completed_source['source']['status'] != 2 or completed_source['source']['deliveryFencingToken'] <= marker['fencingToken']: raise ValueError('The original pending cancellation source did not complete through a fresh claim.')
        q.write_json(output / 'finished-model.json', finished); q.write_json(output / 'finished-workers.json', completed_workers); q.write_json(output / 'finished-source.json', completed_source)
        if native_parent: q.write_json(output / 'finished-parent.json', read('--read-bpmn-embedding', root / 'runtime.db', model_id, native_parent))
        capture('finished'); stop(host); stage = 'retain-settled'; launch(False)
        retained = model(); q.write_json(output / 'retained-model.json', retained)
        if retained != finished: raise ValueError('The actual settled model changed after another cold host.')
        q.write_json(output / 'retained-source.json', source_item(marker['sourceOutboxItemId'])); q.write_json(output / 'retained-workers.json', checked_workers())
        if native_parent: q.write_json(output / 'retained-parent.json', read('--read-bpmn-embedding', root / 'runtime.db', model_id, native_parent))
        capture('retained'); stop(host)
        q.write_json(output / 'process.json', {'schemaVersion': 1, 'kind': 'actual-pending-event-sibling-interruption', 'sourceRevision': source, 'parent': args.parent,
            'modelExecutionId': model_id, 'nativeParentExecutionId': native_parent, 'workers': workers, 'cancelledWorker': workers[0], 'publication': publication,
            'workerPublication': worker_publication, 'parentPublication': parent_publication, 'processes': processes, 'exits': exits, 'products': products,
            'snapshots': snapshots, 'pendingCapturedAtUtc': pending_at, 'submitted': submitted, 'stateSeeded': False, 'clockEdited': False, 'standardRecoveryHosts': True})
        print(json.dumps({'sourceRevision': source, 'parent': args.parent, 'wholeStores': 6, 'ownedHosts': 3}))
    except Exception as error:
        if host is not None and host.poll() is None:
            try: capture('failed')
            finally: stop(host)
        q.write_json(output / 'failure.json', {'stage': stage, 'sourceRevision': source, 'parent': args.parent, 'type': type(error).__name__, 'processes': processes, 'exits': exits, 'snapshots': snapshots, 'qualifiesJourney': False})
        raise
    finally:
        for child in reversed(children): stop(child)
        for log in logs: log.close()


if __name__ == '__main__': main()

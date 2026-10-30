#!/usr/bin/env python3
"""Actual model signal delivery-time selection, rearm and empty-cohort crash cuts.

Uses the existing test-only production-port observer, then standard recovery hosts.
Only this controller's owned processes and isolated stores are stopped or captured.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
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

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('hosting', REPO / 'tools/bpmn/qualify-hosting.py')
q = importlib.util.module_from_spec(spec); spec.loader.exec_module(q)


def wait(action, complete, message, seconds=180):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        value = action()
        if complete(value): return value
        time.sleep(.1)
    raise TimeoutError(message)


class ObservationUnavailable(RuntimeError):
    def __init__(self, operation, failure_type):
        self.operation = operation; self.failure_type = failure_type
        super().__init__('The actual read-only observer refused ' + operation + ': ' + failure_type)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workbench-assembly', required=True, type=Path)
    parser.add_argument('--probe-assembly', required=True, type=Path)
    parser.add_argument('--reader-assembly', required=True, type=Path)
    parser.add_argument('--cut', choices=('before-selection', 'after-resume', 'empty-cohort'), required=True)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=REPO, text=True).strip()
    if subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=all'], cwd=REPO, text=True).strip():
        raise ValueError('Commit the controller and host source before recording qualification.')
    reader = args.reader_assembly.resolve(strict=True)
    assemblies = {'probe': args.probe_assembly.resolve(strict=True), 'standard': args.workbench_assembly.resolve(strict=True)}
    products = {name: json.loads(subprocess.check_output(['dotnet', str(reader), '--inspect-native-bpmn-products', str(assembly)], text=True)) for name, assembly in assemblies.items()}
    if any(product['sourceCommit'] != source for product in products.values()): raise ValueError('Build both actual hosts from this exact source.')
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    root = Path(tempfile.mkdtemp(prefix='elsa-model-broadcast-')); control = root / 'probe'; control.mkdir()
    for name in ('appsettings.json', 'appsettings.Development.json', 'shells.json'):
        shutil.copyfile(REPO / 'Essentials/apps/Elsa.Workbench' / name, root / name)
    (root / 'packages').mkdir()
    shells = json.loads((root / 'shells.json').read_text()); features = shells['CShells']['Shells']['default']['Features']
    features.update({'ActivitiesBpmnHosting': {'AdmitNewExecutions': True}, 'ActivitiesBpmnHostingApi': {}, 'WorkflowsRuntimeResumption': {'SweepIntervalSeconds': .2}})
    for name in ('WorkflowsRuntimeDistributed', 'WorkflowsRuntimeDistributedEntityFrameworkCorePersistence', 'WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence'):
        features.pop(name, None)
    features['WorkflowsRuntimeEntityFrameworkCore']['ConnectionString'] = 'Data Source=runtime.db;Pooling=False'
    for name in ('ActivitiesDesignEntityFrameworkCore', 'WorkflowsDesignEntityFrameworkCore', 'WorkflowsPublishingEntityFrameworkCore'):
        features[name]['ConnectionString'] = 'Data Source=design.db;Pooling=False'
    processes = []; exits = []; children = []; logs = []; snapshots = {}; host = None; api = None; models = []; observations = 0; observer_unavailable = []
    event = 'actual-model-broadcast-' + args.cut

    def owned(child):
        receipt = next(item for item in processes if item['processId'] == child.pid)
        if int(q.process_field(child.pid, 'ppid')) != os.getpid() or q.process_field(child.pid, 'lstart') != receipt['started']:
            raise ValueError('The owned host identity changed.')

    def stop(child):
        if child is None or child.poll() is not None: return
        owned(child); child.kill(); child.wait(timeout=30); exits.append({'processId': child.pid, 'exitCode': child.returncode})

    def launch(probe, assist=False):
        nonlocal host, api
        role = 'probe' if probe else 'standard'; content_root = root
        selected = json.loads(json.dumps(shells)); selected_features = selected['CShells']['Shells']['default']['Features']
        if probe: selected_features['BpmnRecoveryProbe'] = {'ControlDirectory': str(control)}
        else: selected_features.pop('BpmnRecoveryProbe', None)
        if assist:
            content_root = root / 'assist'; content_root.mkdir(); (content_root / 'packages').mkdir()
            for name in ('appsettings.json', 'appsettings.Development.json'): shutil.copyfile(root / name, content_root / name)
            selected_features['WorkflowsRuntimeEntityFrameworkCore']['ConnectionString'] = 'Data Source=' + str(root / 'runtime.db') + ';Pooling=False'
            for name in ('ActivitiesDesignEntityFrameworkCore', 'WorkflowsDesignEntityFrameworkCore', 'WorkflowsPublishingEntityFrameworkCore'):
                selected_features[name]['ConnectionString'] = 'Data Source=' + str(root / 'design.db') + ';Pooling=False'
        q.write_json(content_root / 'shells.json', selected)
        log_path = root / ('host-' + str(len(processes)) + '.log'); log = log_path.open('w'); logs.append(log)
        host = subprocess.Popen(['dotnet', str(assemblies[role]), '--contentRoot', str(content_root), '--urls', 'http://127.0.0.1:0'], cwd=content_root,
            env={**os.environ, 'ASPNETCORE_ENVIRONMENT': 'Development'}, stdout=log, stderr=subprocess.STDOUT)
        children.append(host); processes.append({'role': 'assist-standard' if assist else role, 'processId': host.pid, 'controllerProcessId': os.getpid(), 'started': q.process_field(host.pid, 'lstart')})
        def address():
            if host.poll() is not None: raise RuntimeError('The owned host exited; private logs: ' + str(root))
            hits = [line.split('Now listening on: ', 1)[1].strip() for line in log_path.read_text().splitlines() if 'Now listening on: ' in line]
            return hits[0] if hits else None
        url = wait(address, bool, 'The owned host did not listen.'); api = q.Api(url)
        def healthy():
            try:
                with urllib.request.urlopen(url + '/health/ready', timeout=3) as response: return response.status == 200
            except urllib.error.HTTPError as error:
                if error.code == 503: return False
                raise
        wait(healthy, bool, 'The owned host did not become ready.'); api.login('admin', 'Password123!')

    def read(*arguments):
        result = subprocess.run(['dotnet', str(reader), *map(str, arguments)], text=True, capture_output=True, timeout=30)
        value = json.loads(result.stdout)
        if result.returncode: raise ObservationUnavailable(str(arguments[0]), value.get('failureType', 'UnclassifiedObserverFailure'))
        return value
    def observe_snapshot(action, label):
        for attempt in range(1, 4):
            try:
                with frozen(): return action(attempt)
            except ObservationUnavailable as error:
                observer_unavailable.append({'operation': error.operation, 'failureType': error.failure_type, 'label': label,
                    'attempt': attempt, 'observedAtUtc': datetime.now(timezone.utc).isoformat(), 'sourceStateEdited': False})
                q.write_json(output / 'observer-unavailable.json', observer_unavailable)
                if error.failure_type != 'SqliteException' or attempt == 3: raise
                # All hosts have resumed: let an in-flight real transaction release its lock before a new complete read.
                time.sleep(.1)
    def model(identity): return read('--read-bpmn-direct', root / 'runtime.db', identity)
    @contextmanager
    def frozen():
        stopped = []
        try:
            for child in children:
                if child.poll() is not None: continue
                owned(child); os.kill(child.pid, signal.SIGSTOP); stopped.append(child)
                wait(lambda: 'T' in q.process_field(child.pid, 'stat'), bool, 'The owned host did not stop for capture.', 5)
            if not stopped: raise ValueError('A live owned host is required for this capture.')
            yield
        finally:
            for child in stopped: owned(child); os.kill(child.pid, signal.SIGCONT)
    def broadcast(publisher):
        nonlocal observations
        def backup(attempt):
            nonlocal observations
            observations += 1; path = root / ('source-' + str(observations) + '.sqlite')
            read('--backup-sqlite', root / 'runtime.db', path); return path
        path = observe_snapshot(backup, 'source-observation')
        return read('--find-publisher-source', path, q.digest(path), 'default', publisher)

    def capture(label):
        def backup(attempt):
            rows = []
            for store in ('runtime', 'design'):
                raw = root / (label + '-' + str(attempt) + '-' + store + '.sqlite')
                rows.append((store, raw, read('--backup-sqlite', root / (store + '.db'), raw)))
            return rows
        rows = observe_snapshot(backup, label); result = {}
        for store, raw, receipt in rows:
            path = output / (label + '-' + store + '.sqlite.gz'); path.write_bytes(gzip.compress(raw.read_bytes()))
            result[store] = {'file': path.name, 'databaseSha256': q.digest(raw), 'gzipSha256': q.digest(path), 'observer': receipt}
        snapshots[label] = result

    def native_node(identity, kind, inputs): return q.node(identity, api.activity(kind), inputs)
    def publish(name, node):
        definition = api.request('/design/workflows/definitions/submit', {'name': name, 'state': {'variables': [], 'inputs': [], 'outputs': [], 'rootActivity': node}})
        return api.request('/publishing/workflows/' + definition['version']['id'] + '/publish', {})
    def native_waits(state):
        waits = {}
        for work in state['model']['payload']['work']:
            worker = work['nativeWork']['workerExecutionId']
            try: view = api.view(worker)
            except RuntimeError as error:
                if str(error).endswith('returned HTTP 404.'): return None
                raise
            if view['incidents']: raise ValueError('An actual Event worker failed.')
            if not view['activities']: return None
            detail = api.request('/runtime/workflows/instances/' + worker + '/activity-executions/' + view['activities'][0]['activityExecutionId'])
            if len(detail['bookmarks']) != 1: return None
            waits[worker] = {'view': view, 'detail': detail}
        return waits
    def start_model(publication, request_id):
        identity = api.request('/bpmn/executions', {'requestId': request_id, 'sourceReferenceId': publication['sourceReferenceId']})['executionId']
        models.append(identity)
        def waiting():
            state = model(identity)
            return state if native_waits(state) else None
        return wait(waiting, bool, 'The model did not reach its real native Event waits.')

    try:
        launch(True)
        worker = publish('Actual model broadcast worker', native_node('event', 'Elsa.Activities.Primitives.Activities.Event', [q.literal('EventName', event), q.literal('CanStartWorkflow', False)]))
        pin = {name: worker[name] for name in ('artifactId', 'artifactHash', 'artifactVersion', 'definitionId', 'definitionVersionId')}
        looping = args.cut == 'after-resume'
        elements = [{'elementId': 'start', 'elementType': 'startEvent'}, {'elementId': 'fork', 'elementType': 'parallelGateway'}] + [
            {'elementId': name, 'elementType': 'intermediateCatchEvent', 'bindingRef': name + '-event', 'eventDefinitions': [{'type': 'signal', 'properties': {'name': event}}]} for name in ('first', 'second')]
        flows = [{'flowId': 'begin', 'sourceRef': 'start', 'targetRef': 'fork'}] + [{'flowId': 'fork-' + name, 'sourceRef': 'fork', 'targetRef': name} for name in ('first', 'second')]
        if looping: flows += [{'flowId': name + '-again', 'sourceRef': name, 'targetRef': name} for name in ('first', 'second')]
        else:
            elements += [{'elementId': 'join', 'elementType': 'parallelGateway'}, {'elementId': 'end', 'elementType': 'endEvent'}]
            flows += [{'flowId': name + '-join', 'sourceRef': name, 'targetRef': 'join'} for name in ('first', 'second')] + [{'flowId': 'finish', 'sourceRef': 'join', 'targetRef': 'end'}]
        definition = {'formatVersion': 5, 'humanBindings': [], 'nativeEventBindings': [{'bindingId': name + '-event', 'executable': pin} for name in ('first', 'second')],
            'process': {'processId': 'actual-model-broadcast', 'elements': elements, 'sequenceFlows': flows}}
        publication = api.request('/bpmn/artifacts', {'requestId': 'publish-model', 'definition': definition})
        initial = None if args.cut == 'empty-cohort' else start_model(publication, 'initial-model')
        if initial is not None: q.write_json(output / 'initial.json', initial)
        phase = 'after-cohort' if args.cut == 'empty-cohort' else args.cut
        q.write_json(control / 'plan.json', {'caseId': 'actual-cut', 'phase': phase, 'eventName': event})
        publisher = publish('Actual durable model broadcast sender', native_node('send', 'Elsa.Activities.Primitives.Activities.PublishEvent', [q.literal('EventName', event), q.literal('IsLocalEvent', False)]))
        executor = ThreadPoolExecutor(max_workers=1)
        def send():
            request = urllib.request.Request(api.base + '/runtime/workflows/executables/' + publisher['artifactId'] + '/execute',
                data=json.dumps({'sourceReferenceId': publisher['sourceReferenceId']}).encode(), headers={'Content-Type': 'application/json'})
            with api.opener.open(request, timeout=240) as response: return json.load(response)
        pending = executor.submit(send)
        marker_path = control / 'actual-cut/reached.json'
        wait(lambda: marker_path.exists(), bool, 'The real publisher did not reach its selected cut.', 60)
        marker = json.loads(marker_path.read_text()); publisher_id = marker['workflowExecutionId']; q.write_json(output / 'cut-marker.json', marker)
        at_cut = broadcast(publisher_id); q.write_json(output / 'cut-source.json', at_cut)
        assist = None
        if args.cut == 'after-resume':
            primary = host, api; launch(False, assist=True); assist = host; host, api = primary
            initial_id = initial['model']['executionId']; initial_ids = {w['nativeWork']['workerExecutionId'] for w in initial['model']['payload']['work']}
            rearmed = wait(lambda: model(initial_id), lambda value: len(value['model']['payload'].get('nativeResults', [])) == 1 and
                len({w['nativeWork']['workerExecutionId'] for w in value['model']['payload']['work']} - initial_ids) == 1,
                'The actual completed Event worker did not return and rearm while its source ACK was held.', 30)
            q.write_json(output / 'rearmed-before-crash.json', rearmed)
            q.write_json(output / 'rearmed-native-waits.json', wait(lambda: native_waits(rearmed), bool,
                'The rearmed model work did not become a real native Event registration before source retry.', 30))
        late = start_model(publication, 'late-model'); q.write_json(output / 'late-before-crash.json', late)
        before_source = broadcast(publisher_id); q.write_json(output / 'before-source.json', before_source)
        if at_cut['source']['snapshot'] != before_source['source']['snapshot']: raise ValueError('A newly armed listener changed an installed source cohort.')
        current = before_source['source']; deadline = re.sub(r'\.(\d{1,7})(?=[+-])', lambda m: '.' + m[1].ljust(6, '0')[:6], current['deliveryVisibleAfter'].replace('Z', '+00:00'))
        if current['status'] != 'Delivering' or current['deliveringOwnerId'] != marker['ownerId'] or current['deliveryFencingToken'] != marker['fencingToken'] or datetime.now(timezone.utc) >= datetime.fromisoformat(deadline):
            raise ValueError('The original source claim did not remain fresh and unacknowledged through the actual crash cut.')
        capture('pending'); pending_at = datetime.now(timezone.utc).isoformat()
        if datetime.now(timezone.utc) >= datetime.fromisoformat(deadline): raise ValueError('The source lease expired during the actual pending capture.')
        stop(host); stop(assist)
        try: submitted = pending.result(timeout=5)
        except (OSError, TimeoutError, urllib.error.URLError): submitted = None
        executor.shutdown(wait=True)
        launch(False)
        finished_source = wait(lambda: broadcast(publisher_id), lambda value: value['source']['status'] == 'Delivered', 'The original durable publisher source did not recover.', 240)
        q.write_json(output / 'finished-source.json', finished_source)
        def ready(identity, value):
            if value['queuedModelInputs'] or any(s['status'] != 2 for s in value['sources']): return False
            if args.cut == 'before-selection': return value['model']['lifecycle'] == 3
            if args.cut == 'after-resume' and identity == initial_id: return len(value['model']['payload'].get('nativeResults', [])) == 2
            return True
        finished = {identity: wait(lambda: model(identity), lambda value: ready(identity, value), 'The actual model work did not settle.') for identity in models}
        q.write_json(output / 'finished-models.json', finished)
        if args.cut == 'before-selection':
            if len(finished_source['source']['snapshot']['resumes']) != 4 or any(v['model']['lifecycle'] != 3 for v in finished.values()): raise ValueError('Delivery-time matching omitted waits created after publisher commit.')
        elif args.cut == 'empty-cohort':
            if finished_source['source']['snapshot']['resumes'] or finished[late['model']['executionId']]['model'] != late['model']: raise ValueError('An installed empty cohort delivered to a later listener.')
        else:
            first = finished[initial_id]['model']; late_final = finished[late['model']['executionId']]['model']
            if first['revision'] != 3 or len(first['payload']['nativeResults']) != 2 or len(first['payload']['work']) != 2 or late_final != late['model']:
                raise ValueError('The saved retry changed a later listener or did not finish both original callbacks.')
            retained = [w for w in first['payload']['work'] if w in rearmed['model']['payload']['work'] and w['nativeWork']['workerExecutionId'] not in initial_ids]
            if len(retained) != 1: raise ValueError('The original source retry consumed an already rearmed model listener.')
        capture('finished'); stop(host); launch(False)
        retained = {identity: model(identity) for identity in models}; q.write_json(output / 'retained-models.json', retained)
        if retained != finished: raise ValueError('The model/source decision changed after another cold process.')
        retained_source = broadcast(publisher_id); q.write_json(output / 'retained-source.json', retained_source)
        if retained_source['source'] != finished_source['source']: raise ValueError('The original durable source changed after another cold process.')
        capture('retained'); stop(host)
        q.write_json(output / 'process.json', {'schemaVersion': 1, 'kind': 'actual-model-broadcast-recovery', 'sourceRevision': source, 'cut': args.cut,
            'publication': publication, 'workerPublication': worker, 'publisherPublication': publisher, 'publisherExecutionId': publisher_id,
            'submitted': submitted, 'modelExecutionIds': models, 'products': products, 'processes': processes, 'exits': exits, 'snapshots': snapshots,
            'ownedHosts': len(processes), 'separateStandardConsumerBeforeCrash': assist is not None,
            'pendingCapturedAtUtc': pending_at, 'observerUnavailable': observer_unavailable,
            'stateSeeded': False, 'clockEdited': False, 'standardRecoveryHosts': True, 'retainedLoopWaitsAreIntentional': looping})
        print(json.dumps({'sourceRevision': source, 'cut': args.cut, 'models': len(models), 'wholeStores': 6, 'ownedHosts': len(processes)}))
    except Exception as error:
        if host is not None and host.poll() is None:
            try: capture('failed')
            finally: stop(host)
        q.write_json(output / 'failure.json', {'sourceRevision': source, 'cut': args.cut, 'type': type(error).__name__, 'processes': processes, 'exits': exits, 'snapshots': snapshots, 'qualifiesJourney': False})
        raise
    finally:
        for child in reversed(children): stop(child)
        for log in logs: log.close()


if __name__ == '__main__': main()

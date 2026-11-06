#!/usr/bin/env python3
"""Owned independent-process native and genuine BPMN package witness, SQLite and owned PostgreSQL 16 profiles."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import runpy
import subprocess
import sys
import time
import uuid

PLANNED = (
    'native-success', 'native-failure', 'bpmn-success', 'bpmn-failure',
    'native-instance-completed-result-hold', 'native-definition-completed-result-hold-two-versions',
    'bpmn-instance-completed-result-hold', 'bpmn-definition-completed-result-hold-two-versions',
    'native-depth-pre-admission-zero-jobs', 'native-oversized-pre-admission-zero-jobs',
    'bpmn-depth-pre-admission-zero-jobs', 'bpmn-oversized-pre-admission-zero-jobs',
    'native-owner-cancel-after-admission', 'bpmn-owner-cancel-after-admission',
    'bpmn-owner-cancel-before-helper-admission', 'bpmn-helper-complete-before-root-consumption-restart',
    'native-root-consumed-before-ack-history-retained', 'bpmn-root-consumed-before-ack-history-retained',
    'native-postcommit-ack-response-loss', 'bpmn-postcommit-ack-response-loss',
    'native-sequence-success', 'native-flowchart-success',
    'native-nested-result-held-across-coordinator-restart', 'native-nested-owner-cancellation',
    'native-independent-parent-completes-before-job', 'bpmn-independent-parent-completes-before-job',
    'native-detached-parent-completes-before-job', 'bpmn-detached-parent-completes-before-job',
    'native-independent-inherits-explicit-cancellation', 'bpmn-independent-inherits-explicit-cancellation',
    'native-detached-survives-explicit-cancellation', 'bpmn-detached-survives-explicit-cancellation',
    'native-nested-independent-association', 'native-nested-detached-association',
    'bpmn-created-helper-cancel-before-job-saved-wait')
REMAINING = (
    'linked-retry-model-association', 'native-cancel-before-saved-wait',
    'selected-failed-timedout-cancelled-result-forged-or-missing-grant-runtime-control',
    'instance-and-definition-grant-before-pause-replay', 'model-host-stale-process-and-fenced-overlap',
    'authoring-submit-publish-and-import', 'authorization-and-tenant-cross-scope-negative', 'composed-studio-experience')


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n')


def matching_ack_record(record, entered):
    keys = ('contextId', 'transactionId', 'jobId', 'proposalSha256', 'processId', 'occurrence')
    assert all(record.get(key) == entered.get(key) for key in keys), 'ACK boundary record changed its exact proposal/transaction identity.'
    assert record['occurrence'] == 1 and record['proposalSha256'] and len(record['proposalSha256']) == 64


def terminate_owned(process):
    if process.poll() is not None:
        return
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        return
    try:
        process.wait(timeout=8)
    except subprocess.TimeoutExpired:
        os.killpg(process.pid, signal.SIGKILL)
        process.wait(timeout=8)


class Models:
    def __init__(self, entry, evidence, provider):
        self.entry, self.evidence, self.provider = entry, evidence, provider
        self.processes, self.steps, self.cases = [], [], []
        self.active_case = None
        self.number = 0
        self.run_id = uuid.uuid4().hex
        self.store_owner = None
        self.resources_cleaned = False
        self.base = {'provider': 'Sqlite' if provider == 'sqlite' else 'PostgreSql', 'schema': None, 'jobsConnectionString': 'Data Source=' + str(evidence / 'jobs.db') + ';Pooling=False',
                     'runtimeConnectionString': 'Data Source=' + str(evidence / 'runtime.db') + ';Pooling=False',
                     'scope': {'persistencePartition': 'initialize-' + self.run_id, 'tenantId': None}}

    def launch(self, mode, **arguments):
        self.number += 1
        prefix = f'{self.number:04d}-{mode}'
        path, output = self.evidence / (prefix + '.command.json'), self.evidence / (prefix + '.result.json')
        command = {**self.base, 'mode': mode, 'outputFile': str(output), **arguments}
        write_json(path, command)
        path.chmod(0o600)
        log_path = self.evidence / (prefix + '.log')
        log = log_path.open('w')
        process = subprocess.Popen(['dotnet', str(self.entry), str(path)], cwd=self.entry.parent,
            stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        self.processes.append((process, log))
        self.steps.append({'mode': mode, 'processId': process.pid, 'commandFile': str(path),
                           'outputFile': str(output), 'log': str(log_path)})
        return process, command

    def call(self, mode, **arguments):
        process, command = self.launch(mode, **arguments)
        try:
            code = process.wait(timeout=60)
        except subprocess.TimeoutExpired:
            terminate_owned(process)
            raise TimeoutError('Owned model command timed out: ' + mode)
        result_path = Path(command['outputFile'])
        next(item for item in self.steps if item['processId'] == process.pid)['exitCode'] = code
        result = json.loads(result_path.read_text()) if result_path.is_file() else None
        if code or result is None or not result.get('ok'):
            raise RuntimeError('Model command failed: ' + mode + '; see retained command/log/result.')
        assert result['processId'] == process.pid
        self.verify_loaded(result['loadedProducts'])
        return result['value']

    def verify_loaded(self, products):
        assert products and len({item['name'] for item in products}) == len(products)
        for product in products:
            path = Path(product['path']).resolve(strict=True)
            assert path.parent == self.entry.parent, 'Loaded model DLL escaped the immutable deployment.'
            assert hashlib.sha256(path.read_bytes()).hexdigest() == product['sha256']

    def until(self, predicate, label, timeout=75, process=None):
        deadline = time.monotonic() + timeout
        while True:
            value = predicate()
            if value:
                return value
            if process is not None and process.poll() is not None:
                raise RuntimeError('Owned role exited before ' + label)
            if time.monotonic() >= deadline:
                raise TimeoutError('Did not observe ' + label)
            time.sleep(0.15)

    def role(self, mode, **arguments):
        label = uuid.uuid4().hex
        ready, stop = self.evidence / (label + '.ready.json'), self.evidence / (label + '.stop')
        process, _ = self.launch(mode, readyFile=str(ready), stopFile=str(stop), **arguments)
        self.until(ready.is_file, mode + ' readiness', process=process)
        observed = json.loads(ready.read_text())
        assert observed['processId'] == process.pid
        self.verify_loaded(observed['loadedProducts'])
        return process, stop

    def stop(self, role):
        process, stop = role
        stop.touch()
        try:
            code = process.wait(timeout=30)
        except subprocess.TimeoutExpired:
            terminate_owned(process)
            raise TimeoutError('Owned model role did not stop.')
        next(item for item in self.steps if item['processId'] == process.pid)['exitCode'] = code
        if code:
            raise RuntimeError('Owned model role exited unsuccessfully.')

    def begin(self, name):
        self.active_case = name
        self.base['scope'] = {'persistencePartition': 'model-' + self.run_id + '-' + str(len(self.cases)), 'tenantId': None}

    def passed(self, **evidence):
        self.cases.append({'name': self.active_case, 'state': 'PASSED', **evidence})
        self.active_case = None

    def report(self, name, failure=False, blocked=False):
        return {'name': name, 'lines': [{'product': 'Widgets', 'quantity': 3, 'unitPrice': 12.5},
                                      {'product': 'Service', 'quantity': 2, 'unitPrice': 7.25}],
                'entryDirectory': str(self.evidence / (name + '.entries')), 'failUntilAttempt': 1 if failure else 0,
                'releaseDirectory': str(self.evidence / (name + '.releases')) if blocked else None}

    def create(self, kind, suffix='', invalid=None, send=True, failure=False, definition=None, version='v1', blocked=False, structure='root', mode='Awaited', active=False):
        name = self.active_case + suffix
        report = self.report(name, failure, blocked)
        created = self.call('create', modelKind=kind, executionId='request-' + uuid.uuid4().hex,
            definitionId=definition or 'definition-' + self.active_case, definitionVersion=version,
            report=report, invalidInput=invalid, sendBpmnStart=send, nativeStructure=structure, associationMode=mode, keepParentActive=active)
        return {'modelKind': kind, 'executionId': created['executionId'], 'workerExecutionId': created['workerExecutionId'],
                'created': created, 'report': report, 'nativeStructure': structure, 'associationMode': mode, 'keepParentActive': active, 'definitionId': definition or 'definition-' + self.active_case}

    def snapshot(self, run):
        return self.call('inspect', **{key: run[key] for key in ('modelKind', 'executionId', 'workerExecutionId')})

    def one_job(self, snapshot):
        assert len(snapshot['jobs']) == 1, 'Expected one canonical accepted JobId in this exact owned scope.'
        return snapshot['jobs'][0]

    def saved_wait(self, snapshot, nested=False):
        assert len(snapshot['jobActivities']) == 1 and snapshot['jobActivities'][0]['status'] == 'Suspended'
        if not nested:
            assert len(snapshot['activities']) == 1
        activity = next(item for item in snapshot['activities'] if item['invocationId'] == snapshot['jobActivities'][0]['invocationId'])
        assert activity['privateState'] is not None, 'The actual RunJob wait/private request was not retained.'
        assert self.one_job(snapshot)['request']['association']['mode'] == 'Awaited'

    def terminal_job(self, run):
        def read():
            snapshot = self.snapshot(run)
            return snapshot if self.one_job(snapshot)['state'] == 'Terminal' else None
        return self.until(read, 'retained selected Jobs result')

    def settled(self, run):
        def read():
            snapshot = self.snapshot(run)
            terminal = snapshot['model']['terminalResult'] if run['modelKind'] == 'bpmn' else (
                snapshot['worker'] if snapshot['worker'] and snapshot['worker']['status'] in ('Completed', 'Faulted', 'Cancelled') else None)
            if terminal and snapshot['pendingSource'] is None and len(snapshot['deliveries']) == 1 and snapshot['deliveries'][0]['delivery'] and snapshot['deliveries'][0]['delivery']['isAcknowledged']:
                return snapshot
            return None
        return self.until(read, 'actual owning root and confirmed Jobs acknowledgement')

    def useful_result(self, snapshot, run, failed=False):
        job = self.one_job(snapshot)
        result = job['result']
        assert result['outcome'] == ('Failed' if failed else 'Succeeded')
        if not failed:
            payload = result['payload']
            assert payload['schema'] == {'alias': 'package.report.controlled.result', 'version': 2}
            value = payload['value']
            assert (value['name'], value['lines'], value['units'], value['subtotal']) == (run['report']['name'], 2, 5, 52)
        return result

    def verify_settled(self, snapshot, run, original, failed=False, cancelled=False):
        job = self.one_job(snapshot)
        assert job['result'] == original and len(snapshot['deliveries'][0]['attempts']) == (0 if cancelled else 1)
        expected = 'Cancelled' if cancelled else 'Faulted' if failed else 'Completed'
        if run['modelKind'] == 'bpmn':
            assert snapshot['model']['protocol']['key'] == 'elsa.bpmn'
            assert snapshot['model']['terminalResult']['status'] == expected
            assert snapshot['model']['lifecycle'] == 'TerminalSettled'
            assert snapshot['worker']['modelWorkOrigin']['executionId'] == run['executionId']
            assert snapshot['owner']['terminalReceiptId']
            if not cancelled:
                assert snapshot['owner']['kind'] == 'AcceptedRootInput'
                assert snapshot['owner']['acceptedInput']['inputId'] == snapshot['deliveries'][0]['retainedGrant']['modelInputId']
        else:
            assert snapshot['worker']['status'] == expected and snapshot['worker'].get('modelWorkOrigin') is None
        if not cancelled:
            if run.get('nativeStructure', 'root') == 'root':
                assert snapshot['nativeEvidence'] is not None
            else:
                self.verify_nested_consumption(snapshot, run, original)
            assert snapshot['deliveries'][0]['retainedGrant'] == snapshot['deliveries'][0]['delivery']['permission']
            if run.get('nativeStructure', 'root') == 'root':
                assert snapshot['nativeEvidence']['completion']['jobId'] == job['jobId']
                assert snapshot['nativeEvidence']['result']['resultId'] == original['resultId']
            assert len(list(Path(run['report']['entryDirectory']).glob('*.entry.json'))) == 1
        assert snapshot['pendingSource'] is None, 'A required terminal source is still owed after Jobs ACK.'

    def verify_nested_consumption(self, snapshot, run, original):
        assert run['modelKind'] == 'native' and run['nativeStructure'] != 'root'
        assert snapshot['nativeEvidence'] is None, 'The closed-root inspector must not relabel a composite as RunJob.'
        assert len(snapshot['jobActivities']) == 1
        activity = snapshot['jobActivities'][0]
        association = self.one_job(snapshot)['request']['association']
        assert association['mode'] == 'Awaited' and association['modelKind'] == 'elsa.native'
        assert association['executionId'] == run['executionId'] and association['invocationId'] == activity['invocationId']
        pin = run['created']['executable']
        assert (association['definitionId'], association['definitionVersion'], association['materialId'], association['materialSha256']) == (
            pin['definitionId'], pin['definitionVersionId'], pin['artifactId'], pin['artifactHash'])
        assert activity['parentActivityExecutionId'] is not None and activity['status'] == 'Completed'
        chain = {item['invocationId']: item for item in snapshot['activities']}
        current, ancestors = activity, []
        while current['parentActivityExecutionId'] is not None:
            current = chain[current['parentActivityExecutionId']]
            assert current['invocationId'] not in ancestors, 'Nested execution parent chain contains a cycle.'
            ancestors.append(current['invocationId'])
        assert len(ancestors) == (2 if run['nativeStructure'] == 'nested' else 1)
        assert all(item['status'] == 'Completed' for item in snapshot['activities'])
        final = sorted(activity['attempts'], key=lambda item: item['ordinal'])[-1]
        completion, selected = activity['completion'], activity['selectedResult']
        grant = snapshot['deliveries'][0]['retainedGrant']
        assert final['reason'] == 'Resume' and final['transitionKind'] == 'Complete'
        assert completion['attemptId'] == final['attemptId'] and completion['invocationId'] == activity['invocationId']
        assert completion['contractFingerprint'] == activity['contractIdentity']['schemaFingerprint']
        assert (selected['jobId'], selected['resultId'], selected['continuationId'], selected['modelInputId']) == (
            original['jobId'], original['resultId'], grant['continuationId'], grant['modelInputId'])
        assert selected['value'] == original['payload'] and selected['resultSha256'] == original['payloadSha256']
        consumed = [item for item in activity['triggerDeliveries'] if item['status'] == 'Consumed']
        assert len(consumed) == 1 and consumed[0]['deliveryId'] == final['triggerDeliveryId']
        assert consumed[0]['providerId'] == 'elsa.jobs.native.v1'
        assert grant['association'] == association
        return activity

    def submission_admitted(self, run, terminal_root=False):
        def read():
            snapshot = self.snapshot(run)
            if len(snapshot['jobs']) != 1 or len(snapshot['submissionActivities']) != 1:
                return None
            activity = snapshot['submissionActivities'][0]
            if activity['status'] != 'Completed' or activity['submitted'] is None:
                return None
            if run['nativeStructure'] == 'root' and not run['keepParentActive'] and (
                snapshot['submissionEvidence'] is None or snapshot['submissionEvidence']['disposition'] != 'Accepted'):
                return None
            if terminal_root:
                terminal = snapshot['model']['terminalResult'] if run['modelKind'] == 'bpmn' else snapshot['worker']
                if terminal is None or terminal['status'] != 'Completed' or snapshot['pendingSource'] is not None:
                    return None
            return snapshot
        return self.until(read, 'positive canonical submission admission' + (' and completed owner' if terminal_root else ''))

    def verify_submission(self, snapshot, run):
        job = self.one_job(snapshot)
        assert len(snapshot['submissionActivities']) == 1 and snapshot['jobActivities'] == []
        activity = snapshot['submissionActivities'][0]
        submitted = activity['submitted']
        assert activity['status'] == 'Completed' and submitted['formatVersion'] == 1 and submitted['admissionState'] == 'PendingAdmission'
        assert submitted['jobId'] == job['jobId'], 'A pending model receipt is not canonical acceptance without exact Jobs-store proof.'
        assert not activity['bookmarkIds'] and not activity['triggerRegistrations'] and not activity['triggerDeliveries']
        assert all(item['reason'] != 'Resume' and item['triggerDeliveryId'] is None for item in activity['attempts'])
        assert snapshot['nativeEvidence'] is None, 'Submission is not selected-JobResult consumption.'
        assert len(snapshot['deliveries']) == 1 and snapshot['deliveries'][0]['delivery'] is None and snapshot['deliveries'][0]['retainedGrant'] is None
        association = job['request']['association']
        assert association['mode'] == run['associationMode'] and association['continuation'] is None
        assert association['executionId'] == run['executionId']
        if run['modelKind'] == 'bpmn':
            origin = snapshot['worker']['modelWorkOrigin']
            assert snapshot['model']['protocol']['key'] == 'elsa.bpmn'
            assert association['modelKind'] == 'elsa.bpmn' and association['definitionId'] == run['definitionId']
            assert association['invocationId'] == origin['invocationId']
            pin = origin['executable']
            assert association['materialId'] == pin['artifactId'] and association['materialSha256'] == pin['artifactHash']
            assert association['definitionVersion'] == pin['definitionVersionId']
        else:
            pin = run['created']['executable']
            assert association['modelKind'] == 'elsa.native' and association['invocationId'] == activity['invocationId']
            assert (association['definitionId'], association['definitionVersion'], association['materialId'], association['materialSha256']) == (
                pin['definitionId'], pin['definitionVersionId'], pin['artifactId'], pin['artifactHash'])
        if run['nativeStructure'] == 'root' and not run['keepParentActive']:
            evidence = snapshot['submissionEvidence']
            assert evidence['disposition'] == 'Accepted' and evidence['acceptance']['jobId'] == job['jobId']
            assert evidence['completion'] == submitted and evidence['requestSha256'] == job['requestSha256']
            assert evidence['origin']['payloadSha256'] == job['requestSha256']
            assert evidence['owner']['executionId'] == run['executionId']
        return job, submitted

    def run(self):
        if self.provider == 'postgresql':
            owner_type = runpy.run_path(str(Path(__file__).with_name('owned_model_store.py')))['OwnedModelStores']
            self.store_owner = owner_type(self.evidence, self.run_id)
            jobs, runtime, schema = self.store_owner.create()
            self.base.update(jobsConnectionString=jobs, runtimeConnectionString=runtime, schema=schema)
        self.call('initialize')
        self.provider_version = self.call('provider-version')
        assert self.provider_version['provider'] == self.base['provider'] and self.provider_version['version'] and self.provider_version['runtimeVersion']
        if self.provider == 'postgresql':
            assert self.provider_version['version'].startswith('PostgreSQL 16.') and self.provider_version['runtimeVersion'].startswith('PostgreSQL 16.')
        for kind in ('native', 'bpmn'):
            for failed in (False, True):
                self.begin(kind + ('-failure' if failed else '-success'))
                run = self.create(kind, failure=failed)
                self.saved_wait(self.snapshot(run))
                worker = self.role('worker')
                selected = self.terminal_job(run)
                original = self.useful_result(selected, run, failed)
                self.stop(worker)
                coordinator = self.role('coordinator')
                final = self.settled(run)
                self.stop(coordinator)
                self.verify_settled(self.snapshot(run), run, original, failed)
                self.passed(jobId=original['jobId'], resultId=original['resultId'], rootExecutionId=run['executionId'])

        for kind in ('native', 'bpmn'):
            for definition_gate in (False, True):
                self.begin(kind + ('-definition-completed-result-hold-two-versions' if definition_gate else '-instance-completed-result-hold'))
                definition = 'held-definition-' + kind
                runs = [self.create(kind, definition=definition, blocked=True)]
                if definition_gate:
                    runs.append(self.create(kind, suffix='-v2', definition=definition, version='v2', blocked=True))
                # One scope contains two Jobs only in the explicit definition-wide control.
                gate = {'kind': 'WorkflowDefinition' if definition_gate else 'WorkflowInstance',
                        'name': definition if definition_gate else runs[0]['executionId'],
                        'modelKind': 'elsa.bpmn' if kind == 'bpmn' else 'elsa.native'}
                hold = 'hold-' + uuid.uuid4().hex
                worker = self.role('worker')
                self.until(lambda: all(len(list(Path(run['report']['entryDirectory']).glob('*.entry.json'))) == 1 for run in runs),
                           'physical handler entry before workflow pause', process=worker[0])
                self.call('pause', gate=gate, holdId=hold, operationId='pause-' + hold)
                for run in runs:
                    directory = Path(run['report']['releaseDirectory'])
                    directory.mkdir()
                    (directory / '1.release').touch()
                def selected_all():
                    snapshot = self.snapshot(runs[0])
                    return snapshot if len(snapshot['jobs']) == len(runs) and all(job['state'] == 'Terminal' for job in snapshot['jobs']) else None
                selected = self.until(selected_all, 'all completed reports while workflow continuation held')
                assert all(job['result']['outcome'] == 'Succeeded' and
                    (job['result']['payload']['value']['lines'], job['result']['payload']['value']['units'], job['result']['payload']['value']['subtotal']) == (2, 5, 52)
                    for job in selected['jobs'])
                original = {job['jobId']: job['result'] for job in selected['jobs']}
                self.stop(worker)
                coordinator = self.role('coordinator')
                time.sleep(0.6)
                held = self.snapshot(runs[0])
                assert all(item['delivery']['permission'] is None and not item['delivery']['isAcknowledged'] for item in held['deliveries'])
                for run in runs:
                    snapshot = self.snapshot(run)
                    assert snapshot['worker']['status'] == 'Running' and snapshot['activities'][0]['status'] == 'Suspended'
                    if kind == 'bpmn':
                        assert snapshot['model']['terminalResult'] is None and snapshot['owner']['kind'] == 'NotConsumed'
                holds = self.call('holds', gate=gate)['items']
                active = next(item for item in holds if item['holdId'] == hold and item['releasedAt'] is None)
                self.call('release', gate=gate, holdId=hold, expectedRevision=active['revision'], operationId='release-' + hold)
                def all_acked():
                    snapshot = self.snapshot(runs[0])
                    return snapshot if all(item['delivery']['isAcknowledged'] for item in snapshot['deliveries']) else None
                final = self.until(all_acked, 'same saved results after workflow hold release')
                self.stop(coordinator)
                assert {job['jobId']: job['result'] for job in final['jobs']} == original
                assert all(len(item['attempts']) == 1 for item in final['deliveries'])
                for run in runs:
                    snapshot = self.snapshot(run)
                    assert snapshot['worker']['status'] == 'Completed'
                    if kind == 'bpmn':
                        assert snapshot['model']['terminalResult']['status'] == 'Completed'
                    assert len(list(Path(run['report']['entryDirectory']).glob('*.entry.json'))) == 1
                self.passed(rootExecutionIds=[run['executionId'] for run in runs], retainedResults=list(original), gate=gate)

        for kind in ('native', 'bpmn'):
            for invalid in ('depth', 'oversized'):
                self.begin(kind + '-' + invalid + '-pre-admission-zero-jobs')
                run = self.create(kind, invalid=invalid)
                coordinator = self.role('coordinator')
                def faulted():
                    snapshot = self.snapshot(run)
                    status = snapshot['model']['terminalResult'] if kind == 'bpmn' else snapshot['worker']
                    return snapshot if status and status['status'] == 'Faulted' else None
                snapshot = self.until(faulted, 'composed positive pre-admission FinalFault')
                self.stop(coordinator)
                assert snapshot['jobs'] == [] and snapshot['deliveries'] == [] and snapshot['nativeEvidence'] is None
                assert snapshot['activities'][0]['fault']['code'] == 'jobs.native.admission-invalid'
                assert not list(Path(run['report']['entryDirectory']).glob('*.entry.json'))
                self.passed(rootExecutionId=run['executionId'], canonicalJobs=0)

        for kind in ('native', 'bpmn'):
            self.begin(kind + '-owner-cancel-after-admission')
            run = self.create(kind)
            self.saved_wait(self.snapshot(run))
            self.call('cancel', modelKind=kind, executionId=run['executionId'], operationId='cancel-' + uuid.uuid4().hex)
            coordinator = self.role('coordinator')
            final = self.settled(run)
            self.stop(coordinator)
            original = self.one_job(final)['result']
            assert original['outcome'] == 'Cancelled'
            self.verify_settled(self.snapshot(run), run, original, cancelled=True)
            assert not list(Path(run['report']['entryDirectory']).glob('*.entry.json'))
            self.passed(rootExecutionId=run['executionId'], jobId=original['jobId'], proof='ParentProvenTerminal')

        self.begin('bpmn-owner-cancel-before-helper-admission')
        run = self.create('bpmn', send=False)
        assert self.snapshot(run)['worker'] is None
        self.call('cancel', modelKind='bpmn', executionId=run['executionId'], operationId='cancel-' + uuid.uuid4().hex)
        coordinator = self.role('coordinator')
        def retired():
            snapshot = self.snapshot(run)
            return snapshot if snapshot['model']['lifecycle'] == 'TerminalSettled' else None
        final = self.until(retired, 'positive original native-start retirement')
        self.stop(coordinator)
        assert final['jobs'] == [] and final['worker'] is None and final['model']['terminalResult']['status'] == 'Cancelled'
        assert final['model']['settlements'] and all(item['settled'] and item['notAdmitted'] for item in final['model']['settlements'])
        self.passed(rootExecutionId=run['executionId'], canonicalJobs=0)

        self.begin('bpmn-helper-complete-before-root-consumption-restart')
        run = self.create('bpmn')
        worker = self.role('worker')
        original = self.useful_result(self.terminal_job(run), run)
        self.stop(worker)
        deliverer = self.role('deliver-only')
        def helper_complete():
            snapshot = self.snapshot(run)
            return snapshot if snapshot['worker']['status'] == 'Completed' else None
        cut = self.until(helper_complete, 'completed helper with root consumption still owed')
        self.stop(deliverer)
        assert cut['model']['terminalResult'] is None and cut['owner']['kind'] == 'NotConsumed'
        assert not cut['deliveries'][0]['delivery']['isAcknowledged'] and cut['pendingSource']['retainsPublisher']
        assert self.call('delete-worker', executionId=run['executionId'], workerExecutionId=run['workerExecutionId'])['result'] == 'DependencyRetained'
        coordinator = self.role('coordinator')
        self.settled(run)
        self.stop(coordinator)
        self.verify_settled(self.snapshot(run), run, original)
        assert self.call('delete-worker', executionId=run['executionId'], workerExecutionId=run['workerExecutionId'])['result'] == 'Deleted'
        self.passed(rootExecutionId=run['executionId'], resultId=original['resultId'], previousProcessId=deliverer[0].pid,
                    resumedProcessId=coordinator[0].pid)

        for event in ('before-save', 'after-commit'):
            for kind in ('native', 'bpmn'):
                self.begin(kind + ('-root-consumed-before-ack-history-retained' if event == 'before-save' else '-postcommit-ack-response-loss'))
                run = self.create(kind)
                worker = self.role('worker')
                original = self.useful_result(self.terminal_job(run), run)
                self.stop(worker)
                marker = self.evidence / (self.active_case + '.ack.json')
                release = self.evidence / (self.active_case + '.release')
                coordinator = self.role('coordinator', ackFault={'jobId': original['jobId'], 'event': event,
                    'markerFile': str(marker), 'releaseFile': str(release) if event == 'before-save' else None})
                self.until(marker.is_file, 'exact ACK transaction boundary', process=coordinator[0])
                observed = json.loads(marker.read_text())
                assert observed['commitObserved'] == (event == 'after-commit') and observed['jobId'] == original['jobId']
                assert observed['processId'] == coordinator[0].pid and observed['state'] == 'Entered' and observed['occurrence'] == 1
                timed_out = Path(str(marker) + '.timed-out.json')
                released = Path(str(marker) + '.released.json')
                committed = Path(str(marker) + '.committed.json')
                release_id = None
                if event == 'before-save':
                    assert not timed_out.exists() and not released.exists() and not committed.exists()
                    cut = self.snapshot(run)
                    assert not cut['deliveries'][0]['delivery']['isAcknowledged']
                    assert cut['worker']['status'] == 'Completed' and cut['pendingSource']['retainsPublisher']
                    if kind == 'bpmn':
                        assert cut['owner']['kind'] == 'AcceptedRootInput'
                    deletion = self.call('delete-worker', workerExecutionId=run['workerExecutionId'], executionId=run['executionId'])['result']
                    assert deletion == 'DependencyRetained'
                    assert not timed_out.exists() and not released.exists() and not committed.exists(), 'The original pre-ACK barrier no longer holds.'
                    release_id = uuid.uuid4().hex
                    request = {key: observed[key] for key in ('contextId', 'transactionId', 'jobId', 'proposalSha256', 'processId', 'occurrence')}
                    request.update(releaseId=release_id, afterCut=True, historyDeletionResult=deletion)
                    temporary = release.with_suffix('.tmp-' + uuid.uuid4().hex)
                    write_json(temporary, request)
                    os.replace(temporary, release)
                    def released_original(path):
                        assert not timed_out.exists(), 'The original pre-ACK barrier timed out; a later retry cannot satisfy this case.'
                        return path.is_file()
                    self.until(lambda: released_original(released), 'matching external pre-ACK barrier release', process=coordinator[0])
                    released_record = json.loads(released.read_text())
                    matching_ack_record(released_record, observed)
                    assert released_record['releaseId'] == release_id and released_record['state'] == 'Released'
                    assert released_record['timedOut'] is False and released_record['proposalUnchanged'] is True
                    assert released_record['afterCut'] is True and released_record['historyDeletionResult'] == deletion
                    self.until(lambda: released_original(committed), 'commit of the same released ACK transaction', process=coordinator[0])
                    committed_record = json.loads(committed.read_text())
                    matching_ack_record(committed_record, observed)
                    assert committed_record['releaseId'] == release_id and committed_record['state'] == 'Committed'
                    assert committed_record['commitObserved'] is True and committed_record['timedOut'] is False and committed_record['proposalUnchanged'] is True
                else:
                    self.until(lambda: Path(str(marker) + '.propagated.json').is_file(), 'same injected ACK exception propagation', process=coordinator[0])
                    propagated = json.loads(Path(str(marker) + '.propagated.json').read_text())
                    matching_ack_record(propagated, observed)
                    assert propagated['exactExceptionInstance']
                self.settled(run)
                self.stop(coordinator)
                recovery = self.role('coordinator')
                self.settled(run)
                self.stop(recovery)
                self.verify_settled(self.snapshot(run), run, original)
                assert not timed_out.exists(), 'Timeout cannot be promoted by an eventual retry ACK.'
                self.passed(rootExecutionId=run['executionId'], resultId=original['resultId'], marker=str(marker),
                            releaseId=release_id, sameProposalCommitted=event == 'before-save',
                            independentRecoveryProcessId=recovery[0].pid)

        for structure in ('sequence', 'flowchart'):
            self.begin('native-' + structure + '-success')
            run = self.create('native', structure=structure)
            self.saved_wait(self.snapshot(run), nested=True)
            worker = self.role('worker')
            original = self.useful_result(self.terminal_job(run), run)
            self.stop(worker)
            coordinator = self.role('coordinator')
            final = self.settled(run)
            self.stop(coordinator)
            self.verify_settled(self.snapshot(run), run, original)
            self.passed(rootExecutionId=run['executionId'], resultId=original['resultId'], structure=structure)

        self.begin('native-nested-result-held-across-coordinator-restart')
        run = self.create('native', structure='nested', blocked=True)
        self.saved_wait(self.snapshot(run), nested=True)
        gate = {'kind': 'WorkflowInstance', 'name': run['executionId'], 'modelKind': 'elsa.native'}
        hold = 'hold-' + uuid.uuid4().hex
        worker = self.role('worker')
        self.until(lambda: len(list(Path(run['report']['entryDirectory']).glob('*.entry.json'))) == 1,
                   'nested physical handler entry before pause', process=worker[0])
        self.call('pause', gate=gate, holdId=hold, operationId='pause-' + hold)
        directory = Path(run['report']['releaseDirectory'])
        directory.mkdir()
        (directory / '1.release').touch()
        original = self.useful_result(self.terminal_job(run), run)
        self.stop(worker)
        first = self.role('coordinator')
        time.sleep(0.6)
        held = self.snapshot(run)
        assert held['worker']['status'] == 'Running' and held['jobActivities'][0]['status'] == 'Suspended'
        assert held['deliveries'][0]['delivery']['permission'] is None and not held['deliveries'][0]['delivery']['isAcknowledged']
        self.stop(first)
        second = self.role('coordinator')
        time.sleep(0.6)
        held_again = self.snapshot(run)
        assert held_again['jobs'][0]['result'] == original and held_again['jobActivities'][0]['status'] == 'Suspended'
        assert held_again['deliveries'][0]['delivery']['permission'] is None
        active = next(item for item in self.call('holds', gate=gate)['items'] if item['holdId'] == hold and item['releasedAt'] is None)
        self.call('release', gate=gate, holdId=hold, expectedRevision=active['revision'], operationId='release-' + hold)
        self.settled(run)
        self.stop(second)
        final = self.snapshot(run)
        self.verify_settled(final, run, original)
        self.passed(rootExecutionId=run['executionId'], resultId=original['resultId'], invocationId=final['jobActivities'][0]['invocationId'],
                    previousProcessId=first[0].pid, resumedProcessId=second[0].pid)

        self.begin('native-nested-owner-cancellation')
        run = self.create('native', structure='nested')
        before = self.snapshot(run)
        self.saved_wait(before, nested=True)
        association = self.one_job(before)['request']['association']
        self.call('cancel', modelKind='native', executionId=run['executionId'], operationId='cancel-' + uuid.uuid4().hex)
        coordinator = self.role('coordinator')
        final = self.settled(run)
        self.stop(coordinator)
        original = self.one_job(final)['result']
        assert original['outcome'] == 'Cancelled' and self.one_job(final)['request']['association'] == association
        assert association['invocationId'] == before['jobActivities'][0]['invocationId']
        self.verify_settled(self.snapshot(run), run, original, cancelled=True)
        assert not list(Path(run['report']['entryDirectory']).glob('*.entry.json'))
        self.passed(rootExecutionId=run['executionId'], jobId=original['jobId'], invocationId=association['invocationId'])

        for mode in ('Independent', 'Detached'):
            for kind in ('native', 'bpmn'):
                self.begin(kind + '-' + mode.lower() + '-parent-completes-before-job')
                run = self.create(kind, mode=mode)
                coordinator = self.role('coordinator')
                accepted = self.submission_admitted(run, terminal_root=True)
                self.stop(coordinator)
                job, submitted = self.verify_submission(accepted, run)
                assert job['state'] != 'Terminal' and accepted['deliveries'][0]['attempts'] == []
                assert not list(Path(run['report']['entryDirectory']).glob('*.entry.json'))
                # Submission retains its exact owning pause scope even after its parent completes.
                gate = {'kind': 'WorkflowInstance', 'name': run['executionId'],
                        'modelKind': 'elsa.bpmn' if kind == 'bpmn' else 'elsa.native'}
                hold = 'hold-' + uuid.uuid4().hex
                self.call('pause', gate=gate, holdId=hold, operationId='pause-' + hold)
                worker = self.role('worker')
                time.sleep(0.6)
                paused = self.snapshot(run)
                assert paused['deliveries'][0]['attempts'] == [] and self.one_job(paused)['state'] != 'Terminal'
                active_hold = next(item for item in self.call('holds', gate=gate)['items'] if item['holdId'] == hold and item['releasedAt'] is None)
                self.call('release', gate=gate, holdId=hold, expectedRevision=active_hold['revision'], operationId='release-' + hold)
                original = self.useful_result(self.terminal_job(run), run)
                self.stop(worker)
                final = self.snapshot(run)
                _, same_submitted = self.verify_submission(final, run)
                assert same_submitted == submitted and len(final['deliveries'][0]['attempts']) == 1
                assert len(list(Path(run['report']['entryDirectory']).glob('*.entry.json'))) == 1
                self.passed(rootExecutionId=run['executionId'], jobId=job['jobId'], resultId=original['resultId'],
                            submissionState='PendingAdmission', canonicalAdmissionProved=True, resultWaitCount=0, gate=gate)

        for mode in ('Independent', 'Detached'):
            for kind in ('native', 'bpmn'):
                self.begin(kind + '-' + mode.lower() + ('-inherits-explicit-cancellation' if mode == 'Independent' else '-survives-explicit-cancellation'))
                run = self.create(kind, mode=mode, active=kind == 'native')
                before = self.submission_admitted(run)
                job, submitted = self.verify_submission(before, run)
                assert before['deliveries'][0]['attempts'] == []
                if kind == 'native':
                    assert before['worker']['status'] == 'Running'
                    assert any(item['status'] == 'Suspended' and item['execution']['executableNodeId'] == 'parent-lifetime' for item in before['activities'])
                else:
                    assert before['worker']['status'] == 'Completed' and before['model']['terminalResult'] is None
                self.call('cancel', modelKind=kind, executionId=run['executionId'], operationId='cancel-' + uuid.uuid4().hex)
                coordinator = self.role('coordinator')
                def cancelled_owner():
                    snapshot = self.snapshot(run)
                    terminal = snapshot['model']['terminalResult'] if kind == 'bpmn' else snapshot['worker']
                    return snapshot if terminal and terminal['status'] == 'Cancelled' and snapshot['pendingSource'] is None else None
                cut = self.until(cancelled_owner, 'exact submitting owner cancellation and settled handoff')
                self.stop(coordinator)
                if mode == 'Independent':
                    terminal = self.terminal_job(run)
                    assert self.one_job(terminal)['result']['outcome'] == 'Cancelled' and terminal['deliveries'][0]['attempts'] == []
                    assert not list(Path(run['report']['entryDirectory']).glob('*.entry.json'))
                else:
                    assert self.one_job(cut)['state'] != 'Terminal' and not self.one_job(cut)['cancellationRequested']
                    worker = self.role('worker')
                    self.useful_result(self.terminal_job(run), run)
                    self.stop(worker)
                final = self.snapshot(run)
                _, same_submitted = self.verify_submission(final, run)
                assert same_submitted == submitted
                assert len(final['deliveries'][0]['attempts']) == (0 if mode == 'Independent' else 1)
                self.passed(rootExecutionId=run['executionId'], jobId=job['jobId'], mode=mode, resultWaitCount=0,
                            cancellationInherited=mode == 'Independent')

        for mode in ('Independent', 'Detached'):
            self.begin('native-nested-' + mode.lower() + '-association')
            run = self.create('native', mode=mode, structure='nested')
            coordinator = self.role('coordinator')
            accepted = self.submission_admitted(run, terminal_root=True)
            self.stop(coordinator)
            job, submitted = self.verify_submission(accepted, run)
            activity = accepted['submissionActivities'][0]
            chain = {item['invocationId']: item for item in accepted['activities']}
            inner = chain[activity['parentActivityExecutionId']]
            outer = chain[inner['parentActivityExecutionId']]
            assert inner['execution']['executableNodeId'] == 'inner-flowchart' and outer['parentActivityExecutionId'] is None
            assert all(item['status'] == 'Completed' for item in accepted['activities'])
            worker = self.role('worker')
            original = self.useful_result(self.terminal_job(run), run)
            self.stop(worker)
            final = self.snapshot(run)
            _, same_submitted = self.verify_submission(final, run)
            assert same_submitted == submitted and len(final['deliveries'][0]['attempts']) == 1
            self.passed(rootExecutionId=run['executionId'], jobId=job['jobId'], resultId=original['resultId'], invocationId=activity['invocationId'])

        self.begin('bpmn-created-helper-cancel-before-job-saved-wait')
        run = self.create('bpmn', send=False)
        marker = self.evidence / 'created-helper-before-wait.json'
        observer = self.role('coordinator', preWaitCut={'rootExecutionId': run['executionId'],
            'workerExecutionId': run['workerExecutionId'], 'markerFile': str(marker)})
        self.until(marker.is_file, 'actual committed helper initial-attempt claim before RunJob body', timeout=60, process=observer[0])
        observed = json.loads(marker.read_text())
        assert observed['state'] == 'CommittedBeforeBody' and observed['occurrence'] == 1 and observed['processId'] == observer[0].pid
        assert observed['checkpointName'] == 'ActivityAttemptClaimed' and observed['commitId'] and observed['checkpointId']
        assert observed['workerExecutionId'] == run['workerExecutionId'] and observed['rootExecutionId'] == run['executionId']
        cut = self.snapshot(run)  # A separate inspector process reads the real committed provider, outside the observer.
        assert cut['worker'] is not None and cut['worker']['status'] == 'Running'
        assert cut['worker']['modelWorkOrigin'] == observed['modelWorkOrigin']
        assert cut['worker']['pinnedExecutable'] == observed['pinnedExecutable']
        assert len(cut['activities']) == 1 and cut['jobs'] == [] and cut['deliveries'] == []
        activity = cut['activities'][0]
        assert activity['invocationId'] == observed['activity']['invocationId'] and activity['status'] == 'Running'
        assert activity['privateState'] is None and activity['completion'] is None and activity['bookmarkIds'] == []
        assert not activity['triggerRegistrations'] and not activity['triggerDeliveries']
        assert len(activity['attempts']) == 1 and activity['attempts'][0]['attemptId'] == observed['initialAttemptId']
        assert activity['attempts'][0]['reason'] == 'Initial' and activity['attempts'][0]['endedAt'] is None
        assert activity['metadata'][observed['activationClaimMetadataKey']] == observed['initialAttemptId']
        timed_out = Path(str(marker) + '.timed-out.json')
        assert not timed_out.exists(), 'The created-helper cut expired before the independent inspection.'
        assert observer[0].poll() is None, 'The owned observer exited before the required actual kill.'
        os.killpg(observer[0].pid, signal.SIGKILL)
        exit_code = observer[0].wait(timeout=10)
        assert exit_code == -signal.SIGKILL
        next(item for item in self.steps if item['processId'] == observer[0].pid).update(
            exitCode=exit_code, expectedTermination='SIGKILL_AFTER_COMMITTED_PRE_WAIT_CUT', preWaitMarker=str(marker))
        assert not timed_out.exists()
        cancellation = self.call('cancel', modelKind='bpmn', executionId=run['executionId'], operationId='cancel-created-helper-' + uuid.uuid4().hex)
        # Root cancellation must commit before a replacement coordinator can run ordinary recovery work.
        before_recovery = self.snapshot(run)
        assert before_recovery['model']['terminalResult']['status'] == 'Cancelled'
        assert before_recovery['jobs'] == [] and before_recovery['worker'] is not None
        successor = self.role('coordinator')
        def cancelled_created_helper():
            snapshot = self.snapshot(run)
            return snapshot if snapshot['model']['lifecycle'] == 'TerminalSettled' and snapshot['worker']['status'] == 'Cancelled' and snapshot['pendingSource'] is None else None
        final = self.until(cancelled_created_helper, 'positive created-helper pre-wait cancellation and actual bridge settlement', process=successor[0])
        self.stop(successor)
        assert final['jobs'] == [] and final['deliveries'] == [] and final['nativeEvidence'] is None
        assert final['owner']['terminalReceiptId'] and final['activities'][0]['bookmarkIds'] == []
        assert final['activities'][0]['privateState'] is None and final['activities'][0]['completion'] is None
        assert final['activities'][0]['invocationId'] == activity['invocationId']
        assert final['activities'][0]['attempts'][0]['attemptId'] == observed['initialAttemptId']
        assert not list(Path(run['report']['entryDirectory']).glob('*.entry.json'))
        assert self.call('delete-worker', executionId=run['executionId'], workerExecutionId=run['workerExecutionId'])['result'] == 'Deleted'
        self.passed(rootExecutionId=run['executionId'], workerExecutionId=run['workerExecutionId'], canonicalJobs=0,
                    initialAttemptId=observed['initialAttemptId'], committedClaimId=observed['commitId'], killedProcessId=observer[0].pid,
                    successorProcessId=successor[0].pid, actualCancellation=cancellation, terminalReceiptId=final['owner']['terminalReceiptId'])

    def close(self):
        errors = []
        for process, log in reversed(self.processes):
            try:
                terminate_owned(process)
            except (OSError, subprocess.SubprocessError) as error:
                errors.append(type(error).__name__)
            finally:
                log.close()
        processes_cleaned = not errors and all(process.poll() is not None for process, _ in self.processes)
        stores_cleaned = self.store_owner.close() if self.store_owner is not None else True
        self.resources_cleaned = processes_cleaned and stores_cleaned
        return processes_cleaned


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    if not __debug__:
        parser.error('Run without optimized Python; model witness assertions must remain enabled.')
    parser.add_argument('--entry', required=True, type=Path)
    parser.add_argument('--evidence-dir', required=True, type=Path)
    parser.add_argument('--provider', choices=('sqlite', 'postgresql'), default='sqlite')
    args = parser.parse_args()
    entry = args.entry.resolve(strict=True)
    evidence = args.evidence_dir.resolve()
    evidence.mkdir(parents=True, exist_ok=False)
    witness = Models(entry, evidence, args.provider)
    receipt = {'schemaVersion': 1, 'provider': args.provider, 'profile': 'jobs-native-genuine-bpmn', 'state': 'NOT RUN',
        'fullAcceptanceState': 'INCOMPLETE', 'entryAssembly': str(entry), 'entrySha256': hashlib.sha256(entry.read_bytes()).hexdigest(),
        'controllerSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'controllerFilesSha256': {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(Path(__file__).parent.glob('*.py'))}, 'plannedCases': list(PLANNED),
        'remainingCases': [{'name': name, 'state': 'NOT RUN'} for name in REMAINING]}
    def stop_requested(_signum, _frame):
        raise RuntimeError('Owned model witness received stop request.')
    signal.signal(signal.SIGTERM, stop_requested)
    code = 1
    try:
        witness.run()
        names = [case['name'] for case in witness.cases]
        if len(names) != len(PLANNED) or set(names) != set(PLANNED) or any(case['state'] != 'PASSED' for case in witness.cases):
            raise ValueError('The actual first-slice cases do not exactly match the complete declared plan.')
        receipt['state'], code = 'PASSED', 0
    except (AssertionError, OSError, ValueError, KeyError, TypeError, RuntimeError, subprocess.SubprocessError) as error:
        receipt['state'] = 'FAILED'
        receipt['error'] = str(error) or type(error).__name__
        if witness.active_case:
            witness.cases.append({'name': witness.active_case, 'state': 'FAILED', 'error': receipt['error']})
    finally:
        try:
            receipt['ownedProcessesCleaned'] = witness.close()
        except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
            receipt['ownedProcessesCleaned'] = False
            receipt['cleanupError'] = type(error).__name__
        receipt['ownedResourcesCleaned'] = witness.resources_cleaned
        receipt['ownedModelStores'] = witness.store_owner.proof if witness.store_owner is not None else {'cleanupState': 'NOT APPLICABLE', 'retainedStores': {}}
        if not receipt['ownedProcessesCleaned'] or not receipt['ownedResourcesCleaned']:
            receipt['state'], code = 'FAILED', 1
        actual = {case['name']: case for case in witness.cases}
        receipt['cases'] = [actual.pop(name, {'name': name, 'state': 'NOT RUN'}) for name in PLANNED]
        if actual:
            receipt['unexpectedCases'] = list(actual.values())
            receipt['state'], code = 'FAILED', 1
        receipt['processes'] = witness.steps
        receipt['providerVersion'] = getattr(witness, 'provider_version', None)
        receipt['retainedStoreFiles'] = {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
            for path in sorted([*evidence.glob('*.db*'), *evidence.glob('*.pgdump')]) if path.is_file()}
        write_json(evidence / 'witness-receipt.json', receipt)
    return code


if __name__ == '__main__':
    sys.exit(main())

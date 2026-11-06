"""Additional actual model controls; invoked only by the immutable model controller."""
import json
import os
from pathlib import Path
import signal
import time
import uuid


def jobs_by_id(snapshot):
    return {item['jobId']: item for item in snapshot['jobs']}


def deliveries_by_id(snapshot):
    return {item['jobId']: item for item in snapshot['deliveries']}


def holds_for(witness, run):
    model = 'elsa.bpmn' if run['modelKind'] == 'bpmn' else 'elsa.native'
    result = []
    for kind, name in (('WorkflowInstance', run['executionId']), ('WorkflowDefinition', run['definitionId'])):
        gate = {'kind': kind, 'name': name, 'modelKind': model}
        hold = 'supplemental-' + uuid.uuid4().hex
        changed = witness.call('pause', gate=gate, holdId=hold, operationId='pause-' + hold)
        assert changed['status'] == 'Applied'
        result.append((gate, hold))
    return result


def release(witness, held):
    gate, hold = held
    active = next(item for item in witness.call('holds', gate=gate)['items'] if item['holdId'] == hold and item['releasedAt'] is None)
    changed = witness.call('release', gate=gate, holdId=hold, expectedRevision=active['revision'], operationId='release-' + hold)
    assert changed['status'] == 'Applied'


def parent_identity(snapshot, kind):
    if kind == 'bpmn':
        model = snapshot['model']
        return model['revision'], model['lifecycle'], model['terminalResult']
    worker = snapshot['worker']
    return worker['status'], worker['completedAt'], worker['systemMetadata'], [
        (item['invocationId'], item['status'], item['completion'], item['attempts']) for item in snapshot['activities']]


def assert_cut_held(marker):
    assert not Path(str(marker) + '.timed-out.json').exists(), 'The actual committed cut timed out before its required kill.'
    assert not Path(str(marker) + '.aborted.json').exists(), 'The actual committed cut exited exceptionally; a retry cannot satisfy it.'


def kill_at_cut(witness, role, marker, label):
    process = role[0] if isinstance(role, tuple) else role
    assert_cut_held(marker)
    assert process.poll() is None
    os.killpg(process.pid, signal.SIGKILL)
    code = process.wait(timeout=10)
    assert code == -signal.SIGKILL
    next(item for item in witness.steps if item['processId'] == process.pid).update(
        exitCode=code, expectedTermination=label, committedCutMarker=str(marker))
    assert_cut_held(marker)


def manual_retry(witness, kind, terminal_parent):
    witness.begin(kind + '-linked-manual-retry-' + ('terminal-parent' if terminal_parent else 'waiting-parent'))
    failure = witness.evidence / (witness.active_case + '.fail')
    failure.touch()
    run = witness.create(kind, report_overrides={'failureFile': str(failure)})
    witness.saved_wait(witness.snapshot(run))
    first_worker = witness.role('worker')
    selected = witness.terminal_job(run)
    original = witness.useful_result(selected, run, failed=True)
    witness.stop(first_worker)
    source = witness.one_job(selected)
    if terminal_parent:
        first_coordinator = witness.role('coordinator')
        witness.settled(run)
        witness.stop(first_coordinator)
        selected = witness.snapshot(run)
        witness.verify_settled(selected, run, original, failed=True)
    else:
        witness.saved_wait(selected)
        assert selected['deliveries'][0]['retainedGrant'] is None
    identity = parent_identity(selected, kind)
    failure.unlink()  # Same immutable input, ordinary real handler condition now recovered.
    held = holds_for(witness, run)
    operation, key = 'manual-' + uuid.uuid4().hex, 'retry-key-' + uuid.uuid4().hex
    retry = witness.call('retry-new', jobId=source['jobId'], expectedRevision=source['revision'],
        proposedJobId='linked-' + uuid.uuid4().hex, idempotencyKey=key, operationId=operation)
    assert retry['status'] == 'Applied' and retry['acceptance']['jobId'] != source['jobId']
    replay = witness.call('retry-new', jobId=source['jobId'], expectedRevision=source['revision'],
        proposedJobId='ignored-proposal-' + uuid.uuid4().hex, idempotencyKey=key, operationId=operation)
    assert replay['status'] == 'Replayed' and replay['acceptance'] == retry['acceptance']
    linked_id = retry['acceptance']['jobId']
    accepted = witness.snapshot(run)
    assert len(accepted['jobs']) == 2
    linked = jobs_by_id(accepted)[linked_id]
    assert linked['retriedFromJobId'] == source['jobId'] and linked['state'] == 'Accepted'
    association = linked['request']['association']
    expected = {**source['request']['association'], 'mode': 'Independent', 'continuation': None}
    assert association == expected and linked['request']['input'] == source['request']['input']
    assert linked['request']['handler'] == source['request']['handler'] and linked['request']['policy'] == source['request']['policy']
    assert deliveries_by_id(accepted)[linked_id]['delivery'] is None and deliveries_by_id(accepted)[linked_id]['retainedGrant'] is None
    worker, coordinator = witness.role('worker'), witness.role('coordinator')
    for index in range(2):
        time.sleep(0.6)
        cut = witness.snapshot(run)
        assert deliveries_by_id(cut)[linked_id]['attempts'] == [] and jobs_by_id(cut)[linked_id]['state'] == 'Accepted'
        assert parent_identity(cut, kind) == identity, 'A linked retry or partial hold release advanced/resurrected the old parent.'
        if not terminal_parent:
            assert deliveries_by_id(cut)[source['jobId']]['retainedGrant'] is None
        if index == 0:
            release(witness, held[0])
            assert any(item['holdId'] == held[1][1] and item['releasedAt'] is None for item in witness.call('holds', gate=held[1][0])['items'])
    release(witness, held[1])
    def completed():
        snapshot = witness.snapshot(run)
        jobs, delivery = jobs_by_id(snapshot), deliveries_by_id(snapshot)
        return snapshot if jobs[linked_id]['state'] == 'Terminal' and delivery[source['jobId']]['delivery']['isAcknowledged'] and snapshot['pendingSource'] is None else None
    final = witness.until(completed, 'linked result and unchanged original parent responsibility')
    witness.stop(worker); witness.stop(coordinator)
    final = witness.snapshot(run)
    jobs, delivery = jobs_by_id(final), deliveries_by_id(final)
    assert jobs[source['jobId']]['result'] == original and jobs[linked_id]['result']['outcome'] == 'Succeeded'
    payload = jobs[linked_id]['result']['payload']
    assert payload['schema'] == source['request']['resultSchema']
    assert (payload['value']['name'], payload['value']['lines'], payload['value']['units'], payload['value']['subtotal']) == (run['report']['name'], 2, 5, 52)
    assert all(len(item['attempts']) == 1 for item in final['deliveries'])
    assert delivery[linked_id]['delivery'] is None and delivery[linked_id]['retainedGrant'] is None
    assert final['worker']['status'] == 'Faulted'
    if kind == 'bpmn':
        assert final['model']['terminalResult']['status'] == 'Faulted' and final['owner']['kind'] == 'AcceptedRootInput'
    if terminal_parent:
        assert parent_identity(final, kind) == identity
    entries = [json.loads(path.read_text()) for path in Path(run['report']['entryDirectory']).glob('*.entry.json')]
    assert len(entries) == 2 and {item['jobId'] for item in entries} == {source['jobId'], linked_id}
    assert len({item['attemptId'] for item in entries}) == 2
    witness.passed(rootExecutionId=run['executionId'], originalJobId=source['jobId'], originalResultId=original['resultId'],
        linkedJobId=linked_id, linkedResultId=jobs[linked_id]['result']['resultId'], exactOperationReplay=True,
        association=association, parentInitiallyTerminal=terminal_parent, inheritedHoldCount=2,
        originalWorkerProcessId=first_worker[0].pid, retryWorkerProcessId=worker[0].pid)


def grant_before_pause(witness, kind):
    witness.begin(kind + '-grant-before-instance-and-definition-pause-replay')
    run = witness.create(kind)
    worker = witness.role('worker')
    original = witness.useful_result(witness.terminal_job(run), run)
    witness.stop(worker)
    marker = witness.evidence / (witness.active_case + '.granted.json')
    first = witness.role('coordinator', continuationCut={'jobId': original['jobId'], 'markerFile': str(marker)})
    witness.until(marker.is_file, 'actual durable continuation grant before dispatch', process=first[0])
    observed = json.loads(marker.read_text())
    assert observed['state'] == 'DurablyGrantedBeforeDispatch' and observed['occurrence'] == 1 and observed['processId'] == first[0].pid
    cut = witness.snapshot(run)
    witness.saved_wait(cut)
    permission = cut['deliveries'][0]['retainedGrant']
    assert permission == observed['permission'] and permission == cut['deliveries'][0]['delivery']['permission']
    assert permission['resultId'] == original['resultId'] and not cut['deliveries'][0]['delivery']['isAcknowledged']
    held = holds_for(witness, run)
    paused = witness.snapshot(run)
    witness.saved_wait(paused)
    assert paused['deliveries'][0]['retainedGrant'] == permission and parent_identity(paused, kind) == parent_identity(cut, kind)
    kill_at_cut(witness, first, marker, 'SIGKILL_AFTER_DURABLE_CONTINUATION_GRANT')
    after_kill = witness.snapshot(run)  # A new inspector runs after the exact sender is confirmed dead.
    witness.saved_wait(after_kill)
    assert parent_identity(after_kill, kind) == parent_identity(paused, kind)
    assert witness.one_job(after_kill)['result'] == original and after_kill['deliveries'][0]['retainedGrant'] == permission
    assert not after_kill['deliveries'][0]['delivery']['isAcknowledged']
    if kind == 'bpmn':
        assert after_kill['model']['terminalResult'] is None and after_kill['owner']['kind'] == 'NotConsumed'
    assert_cut_held(marker)
    successor = witness.role('coordinator')
    witness.settled(run)
    witness.stop(successor)
    final = witness.snapshot(run)
    witness.verify_settled(final, run, original)
    assert final['deliveries'][0]['retainedGrant'] == permission
    for hold in held:
        assert any(item['holdId'] == hold[1] and item['releasedAt'] is None for item in witness.call('holds', gate=hold[0])['items'])
        release(witness, hold)
    witness.passed(rootExecutionId=run['executionId'], jobId=original['jobId'], resultId=original['resultId'],
        permission=permission, activeHoldsAtConsumption=2, killedProcessId=first[0].pid, successorProcessId=successor[0].pid,
        actualGrantMarker=str(marker), freshPostKillSavedWaitAndOwedAckVerified=True)


def native_pre_wait_cancel(witness):
    witness.begin('native-cancel-after-initial-claim-before-saved-wait')
    execution = 'native-prewait-' + uuid.uuid4().hex
    report = witness.report(witness.active_case)
    run = {'modelKind': 'native', 'executionId': execution, 'workerExecutionId': execution, 'report': report}
    marker = witness.evidence / (witness.active_case + '.claim.json')
    creator, _ = witness.launch('create', modelKind='native', executionId=execution, definitionId=witness.active_case,
        report=report, preWaitCut={'rootExecutionId': execution, 'workerExecutionId': execution, 'markerFile': str(marker), 'requireModelOrigin': False})
    witness.until(marker.is_file, 'actual direct native committed initial claim before body', process=creator)
    observed = json.loads(marker.read_text())
    assert observed['state'] == 'CommittedBeforeBody' and observed['occurrence'] == 1 and observed['processId'] == creator.pid
    assert observed['modelWorkOrigin'] is None and observed['rootExecutionId'] == observed['workerExecutionId'] == execution
    assert observed['checkpointName'] == 'ActivityAttemptClaimed' and observed['commitId'] and observed['checkpointId']
    cut = witness.snapshot(run)
    assert cut['worker']['status'] == 'Running' and cut['worker']['modelWorkOrigin'] is None
    assert cut['worker']['pinnedExecutable'] == observed['pinnedExecutable'] and cut['jobs'] == [] and cut['deliveries'] == []
    assert len(cut['activities']) == 1
    activity = cut['activities'][0]
    assert activity['invocationId'] == observed['activity']['invocationId'] and activity['status'] == 'Running'
    assert activity['privateState'] is None and activity['completion'] is None and not activity['bookmarkIds']
    assert not activity['triggerRegistrations'] and not activity['triggerDeliveries']
    assert len(activity['attempts']) == 1 and activity['attempts'][0]['attemptId'] == observed['initialAttemptId']
    assert activity['attempts'][0]['reason'] == 'Initial' and activity['attempts'][0]['endedAt'] is None
    assert activity['metadata'][observed['activationClaimMetadataKey']] == observed['initialAttemptId']
    kill_at_cut(witness, creator, marker, 'SIGKILL_AFTER_NATIVE_COMMITTED_PRE_WAIT_CLAIM')
    cancelled = witness.call('cancel', modelKind='native', executionId=execution, operationId='cancel-' + uuid.uuid4().hex)
    successor = witness.role('coordinator')
    def finished():
        snapshot = witness.snapshot(run)
        return snapshot if snapshot['worker']['status'] == 'Cancelled' and snapshot['pendingSource'] is None else None
    final = witness.until(finished, 'ordinary native cancellation of the existing claimed invocation', process=successor[0])
    witness.stop(successor)
    assert final['jobs'] == [] and final['deliveries'] == [] and final['nativeEvidence'] is None
    assert final['activities'][0]['invocationId'] == activity['invocationId']
    assert final['activities'][0]['attempts'][0]['attemptId'] == observed['initialAttemptId']
    assert final['activities'][0]['privateState'] is None and final['activities'][0]['completion'] is None
    assert not final['activities'][0]['bookmarkIds'] and not list(Path(report['entryDirectory']).glob('*.entry.json'))
    assert witness.call('delete-worker', executionId=execution, workerExecutionId=execution)['result'] == 'Deleted'
    witness.passed(rootExecutionId=execution, canonicalJobs=0, initialAttemptId=observed['initialAttemptId'],
        committedClaimId=observed['commitId'], killedProcessId=creator.pid, successorProcessId=successor[0].pid, actualCancellation=cancelled)


def negative_bpmn_grant(witness, outcome, fault):
    witness.begin('bpmn-selected-' + outcome.lower() + '-result-' + fault + '-grant-refused')
    arguments = {'failure': outcome == 'Failed', 'blocked': outcome == 'TimedOut'}
    if outcome == 'TimedOut':
        arguments['policy'] = {'retry': {'maximumAttempts': 1, 'initialBackoff': '00:00:00.025', 'maximumBackoff': '00:00:01',
            'backoffMultiplier': 2, 'jitterFraction': 0, 'retryTimeouts': False}, 'attemptTimeout': '00:00:01', 'minimumKeyRecognition': '30.00:00:00'}
    run = witness.create('bpmn', **arguments)
    if outcome == 'Cancelled':
        original_job = witness.one_job(witness.snapshot(run))
        witness.call('cancel-job', jobId=original_job['jobId'], operationId='cancel-job-' + uuid.uuid4().hex)
    else:
        worker = witness.role('worker')
    selected = witness.terminal_job(run)
    original = witness.one_job(selected)['result']
    assert original['outcome'] == outcome
    if outcome != 'Cancelled':
        witness.stop(worker)
        assert len(list(Path(run['report']['entryDirectory']).glob('*.entry.json'))) == 1
    deliverer = witness.role('deliver-only')
    def helper_done():
        snapshot = witness.snapshot(run)
        return snapshot if snapshot['worker']['status'] in ('Faulted', 'Cancelled') else None
    before = witness.until(helper_done, 'actual selected non-success result consumed by the closed native helper')
    witness.stop(deliverer)
    assert before['model']['terminalResult'] is None and before['owner']['kind'] == 'NotConsumed'
    assert not before['deliveries'][0]['delivery']['isAcknowledged'] and before['pendingSource']['retainsPublisher']
    permission = before['deliveries'][0]['retainedGrant']
    assert permission and before['nativeEvidence']['result'] == original
    marker = witness.evidence / (witness.active_case + '.adverse.json')
    adverse = witness.role('coordinator', grantReadFault={'jobId': original['jobId'], 'workerExecutionId': run['workerExecutionId'],
        'fault': fault, 'markerFile': str(marker)})
    witness.until(marker.is_file, 'adverse retained grant read inside the actual live BPMN result validator', process=adverse[0])
    observed = json.loads(marker.read_text())
    validation = json.loads(Path(str(marker) + '.validator.json').read_text())
    assert observed['state'] == 'AdverseReadReturned' and observed['validationObserved'] is True and observed['actual'] == permission
    assert observed['processId'] == validation['processId'] == adverse[0].pid
    assert validation['state'] == 'ActualLiveResultValidation' and validation['origin'] == before['worker']['modelWorkOrigin']
    assert validation['inputId'] == permission['modelInputId'] and validation['workerExecutionId'] == run['workerExecutionId']
    assert validation['result']['status'] == before['worker']['status']
    assert observed['returned'] is None if fault == 'missing' else observed['returned']['resultId'] != permission['resultId']
    time.sleep(0.4)
    witness.stop(adverse)
    refused = witness.snapshot(run)  # Fresh process, unaltered public provider queries.
    assert parent_identity(refused, 'bpmn') == parent_identity(before, 'bpmn'), 'Invalid grant evidence advanced or durably rejected the original model input.'
    assert refused['model']['terminalResult'] is None and refused['owner']['kind'] == 'NotConsumed'
    assert refused['deliveries'][0]['retainedGrant'] == permission and not refused['deliveries'][0]['delivery']['isAcknowledged']
    assert refused['pendingSource']['retainsPublisher'] and witness.one_job(refused)['result'] == original
    assert witness.call('delete-worker', executionId=run['executionId'], workerExecutionId=run['workerExecutionId'])['result'] == 'DependencyRetained'
    successor = witness.role('coordinator')
    final = witness.settled(run)
    witness.stop(successor)
    assert final['model']['terminalResult']['status'] == ('Cancelled' if outcome == 'Cancelled' else 'Faulted')
    assert final['owner']['kind'] == 'AcceptedRootInput' and final['owner']['acceptedInput']['inputId'] == permission['modelInputId']
    assert witness.one_job(final)['result'] == original and final['deliveries'][0]['retainedGrant'] == permission
    assert len(final['deliveries'][0]['attempts']) == (0 if outcome == 'Cancelled' else 1)
    assert final['pendingSource'] is None and final['deliveries'][0]['delivery']['isAcknowledged']
    assert len(list(Path(run['report']['entryDirectory']).glob('*.entry.json'))) == (0 if outcome == 'Cancelled' else 1)
    witness.passed(rootExecutionId=run['executionId'], jobId=original['jobId'], resultId=original['resultId'],
        selectedOutcome=outcome, adverseRead=fault, canonicalPermission=permission, retainedSourceWhileRefused=True,
        adverseProcessId=adverse[0].pid, successorProcessId=successor[0].pid, actualValidatorEvidence=str(marker) + '.validator.json')


def run_supplemental(witness):
    for terminal in (False, True):
        for kind in ('native', 'bpmn'):
            manual_retry(witness, kind, terminal)
    for kind in ('native', 'bpmn'):
        grant_before_pause(witness, kind)
    native_pre_wait_cancel(witness)
    for outcome in ('Failed', 'TimedOut', 'Cancelled'):
        for fault in ('missing', 'wrong-result'):
            negative_bpmn_grant(witness, outcome, fault)
    for running in (True, False):
        for kind in ('native', 'bpmn'):
            cancellation_boundary(witness, kind, running)
    for kind in ('native', 'bpmn'):
        late_origin_replay(witness, kind)


def cancellation_boundary(witness, kind, running):
    witness.begin(kind + '-owner-cancel-' + ('running-handler-with-late-physical-result' if running else 'completed-result-before-continuation'))
    run = witness.create(kind, blocked=running, report_overrides={'ignoreCancellation': True} if running else None)
    worker = witness.role('worker')
    entry_dir = Path(run['report']['entryDirectory'])
    witness.until(lambda: len(list(entry_dir.glob('*.entry.json'))) == 1, 'actual handler entry before owning parent cancellation', process=worker[0])
    entry = json.loads(next(entry_dir.glob('*.entry.json')).read_text())
    if running:
        before = witness.snapshot(run)
        assert witness.one_job(before)['state'] == 'AttemptActive' and not list(entry_dir.glob('*.exit.json'))
        original = None
    else:
        before = witness.terminal_job(run)
        original = witness.useful_result(before, run)
        witness.stop(worker)
        assert before['deliveries'][0]['retainedGrant'] is None and not before['deliveries'][0]['delivery']['isAcknowledged']
    association = witness.one_job(before)['request']['association']
    witness.call('cancel', modelKind=kind, executionId=run['executionId'], operationId='cancel-boundary-' + uuid.uuid4().hex)
    coordinator = witness.role('coordinator')
    def parent_cancelled():
        snapshot = witness.snapshot(run)
        terminal = snapshot['model']['terminalResult'] if kind == 'bpmn' else snapshot['worker']
        return snapshot if terminal and terminal['status'] == 'Cancelled' and (not running or witness.one_job(snapshot)['cancellationRequested']) else None
    cancelled = witness.until(parent_cancelled, 'committed owning parent cancellation before late physical release', process=coordinator[0])
    if running:
        witness.until(lambda: len(list(entry_dir.glob('*.cancellation.json'))) == 1,
            'actual cancellation signal observed by the still-running handler', process=worker[0])
        assert not list(entry_dir.glob('*.exit.json')), 'Cancellation was incorrectly treated as proof of physical handler termination.'
        directory = Path(run['report']['releaseDirectory']); directory.mkdir(); (directory / '1.release').touch()
        witness.until(lambda: len(list(entry_dir.glob('*.exit.json'))) == 1, 'actual late physical handler exit', process=worker[0])
        late = json.loads(next(entry_dir.glob('*.exit.json')).read_text())
        assert late['attemptId'] == entry['attemptId'] and late['outcome'] == 'Succeeded'
    final = witness.settled(run)
    if running:
        def physical_retained():
            current = witness.snapshot(run)
            receipts = current['deliveries'][0]['physicalReceipts']
            return current if any(item['metadata']['attemptId'] == entry['attemptId'] and item['metadata']['outcome'] == 'Succeeded' for item in receipts) else None
        final = witness.until(physical_retained, 'retained late successful physical receipt with cancelled authoritative result', process=worker[0])
        original = witness.one_job(final)['result']
        assert original['outcome'] == 'Cancelled'
        witness.stop(worker)
    witness.stop(coordinator)
    final = witness.snapshot(run)
    job = witness.one_job(final)
    assert job['jobId'] == entry['jobId'] and job['request']['association'] == association and job['result'] == original
    assert final['worker']['status'] == 'Cancelled' and final['pendingSource'] is None
    if kind == 'bpmn':
        assert final['model']['terminalResult']['status'] == 'Cancelled' and final['model']['lifecycle'] == 'TerminalSettled'
        assert final['owner']['terminalReceiptId']
    assert len(final['deliveries'][0]['attempts']) == 1 and len(list(entry_dir.glob('*.entry.json'))) == 1
    assert final['deliveries'][0]['delivery']['isAcknowledged']
    assert final['deliveries'][0]['delivery']['acknowledgement']['proof']['kind'] == 'ParentProvenTerminal'
    witness.passed(rootExecutionId=run['executionId'], jobId=job['jobId'], resultId=original['resultId'],
        cut='RunningPhysicalHandler' if running else 'CompletedUndeliveredResult', selectedOutcome=original['outcome'],
        latePhysicalSuccessRetained=running, physicalAttemptId=entry['attemptId'], originalAssociation=association)


def late_origin_replay(witness, kind):
    witness.begin(kind + '-admission-response-loss-and-protected-origin-replay-after-key-expiry')
    request = 'late-origin-' + uuid.uuid4().hex
    report = witness.report(witness.active_case)
    marker = witness.evidence / (witness.active_case + '.accepted.json')
    policy = {'retry': {'maximumAttempts': 1, 'initialBackoff': '00:00:00.025', 'maximumBackoff': '00:00:01',
        'backoffMultiplier': 2, 'jitterFraction': 0, 'retryTimeouts': True}, 'attemptTimeout': '00:02:00', 'minimumKeyRecognition': '00:00:02'}
    producer, _ = witness.launch('create', modelKind=kind, executionId=request, definitionId=witness.active_case, report=report,
        policy=policy, admissionCut={'markerFile': str(marker)})
    witness.until(marker.is_file, 'actual durable-origin acceptance before sender response/ACK', timeout=75, process=producer)
    observed = json.loads(marker.read_text())
    assert observed['state'] == 'AcceptedBeforeSenderAck' and observed['occurrence'] == 1 and observed['processId'] == producer.pid
    admission, acceptance, source = observed['admission'], observed['acceptance'], observed['source']
    association = admission['request']['association']
    assert admission['origin'] and admission['request']['policy']['minimumKeyRecognition'] == '00:00:02'
    assert admission['request']['policy']['retry']['maximumAttempts'] == 1
    assert source['scope']['value'] == witness.base['scope']['persistencePartition']
    assert source['claim']['item']['status'] == 'Delivering' and source['claim']['item']['stimulusDeliverySourceScope'] == source['scope']
    run = {'modelKind': kind, 'executionId': association['executionId'], 'workerExecutionId': source['intent']['workflowExecutionId'],
        'report': report, 'nativeStructure': 'root', 'definitionId': witness.active_case}
    before = witness.snapshot(run)
    witness.saved_wait(before)
    assert witness.one_job(before)['jobId'] == acceptance['jobId']
    assert witness.one_job(before)['requestSha256'] == admission['requestSha256']
    original_pin = before['worker']['pinnedExecutable']
    assert witness.one_job(before)['request']['policy'] == admission['request']['policy']
    origin = witness.call('origin', savedAdmission=admission)
    assert origin['acceptance'] == acceptance and origin['origin'] == admission['origin']
    kill_at_cut(witness, producer, marker, 'SIGKILL_AFTER_ACTUAL_JOB_ACCEPTANCE_BEFORE_SENDER_ACK')
    after_kill = witness.snapshot(run)
    witness.saved_wait(after_kill)
    assert parent_identity(after_kill, kind) == parent_identity(before, kind)
    assert witness.one_job(after_kill)['jobId'] == acceptance['jobId'] and witness.one_job(after_kill)['state'] == 'Accepted'
    assert after_kill['deliveries'][0]['retainedGrant'] is None and after_kill['deliveries'][0]['delivery'] is None
    assert after_kill['pendingSource']['outboxItemId'] == source['sourceOutboxItemId'] and after_kill['pendingSource']['status'] == 'Delivering'
    if kind == 'bpmn':
        assert after_kill['model']['terminalResult'] is None and after_kill['owner']['kind'] == 'NotConsumed'
    assert_cut_held(marker)
    worker = witness.role('worker')
    selected = witness.terminal_job(run)
    original = witness.useful_result(selected, run)
    witness.stop(worker)
    assert selected['deliveries'][0]['retainedGrant'] is None
    # Settle the actual selected result before testing ordinary-key expiry. The original sender
    # must remain owed under its still-live old claim; a slow run that delivers it here must fail.
    result_coordinator = witness.role('coordinator')
    replay_arguments = {'savedSource': source, 'savedAdmission': admission}
    def result_acked_with_sender_owed():
        snapshot = witness.snapshot(run)
        owed = witness.call('source-status', **replay_arguments)
        assert owed['outboxItemId'] == source['sourceOutboxItemId'] and owed['status'] == 'Delivering', \
            'The original admission source was delivered before the post-ACK origin-expiry cut.'
        terminal = snapshot['model']['terminalResult'] if kind == 'bpmn' else snapshot['worker']
        return snapshot if terminal and terminal['status'] == 'Completed' and snapshot['deliveries'][0]['delivery']['isAcknowledged'] else None
    acknowledged = witness.until(result_acked_with_sender_owed, 'actual parent consumption and Jobs ACK while original sender remains owed',
        timeout=45, process=result_coordinator[0])
    witness.stop(result_coordinator)
    assert witness.one_job(acknowledged)['result'] == original
    assert witness.call('source-status', **replay_arguments)['status'] == 'Delivering'
    # Natural time only: the accepted immutable short-recognition policy is never edited.
    from datetime import datetime, timezone
    expiry = datetime.fromisoformat(acceptance['recognizeUntil'].replace('Z', '+00:00'))
    witness.until(lambda: datetime.now(timezone.utc) > expiry, 'actual accepted minimum key-recognition deadline')
    assert witness.call('origin', savedAdmission=admission) == origin, 'Positive exact protected origin is required after ACK and natural key deadline.'
    fresh_key_replay = witness.call('replay-origin-fresh-key', **replay_arguments)
    assert fresh_key_replay['status'] == 'Replayed' and fresh_key_replay['acceptance'] == acceptance
    assert len(witness.snapshot(run)['jobs']) == 1, 'A fresh producer key must not recreate a lost protected origin as a new job.'
    def replay_ready():
        replayed = witness.call('replay-source', **replay_arguments)
        return replayed if replayed['claimed'] else None
    replayed = witness.until(replay_ready, 'natural old source visibility expiry and exact fresh public sender claim', timeout=90)
    assert replayed['claim']['outboxItemId'] == source['sourceOutboxItemId']
    assert replayed['claim']['item']['intent'] == source['intent'] and replayed['claim']['fencingToken'] > source['claim']['fencingToken']
    assert replayed['retained']['status'] == 'Delivered' and replayed['origin']['acceptance'] == acceptance
    duplicate = witness.call('old-source-duplicate', **replay_arguments)
    assert duplicate['refusedStaleSource'] and duplicate['sourceOutboxItemId'] == source['sourceOutboxItemId']
    after_replay = witness.snapshot(run)
    assert len(after_replay['jobs']) == 1 and witness.one_job(after_replay)['result'] == original
    assert after_replay['worker']['pinnedExecutable'] == original_pin
    assert witness.call('origin', savedAdmission=admission) == origin
    coordinator = witness.role('coordinator')
    def fully_settled():
        snapshot = witness.snapshot(run)
        return snapshot if snapshot['pendingSource'] is None and snapshot['pendingRootSource'] is None and \
            snapshot['deliveries'][0]['delivery']['isAcknowledged'] else None
    witness.until(fully_settled, 'all original native/model sender responsibility settled', process=coordinator[0])
    witness.stop(coordinator)
    final = witness.snapshot(run)
    witness.verify_settled(final, run, original)
    assert final['worker']['pinnedExecutable'] == original_pin and witness.one_job(final)['request']['policy'] == admission['request']['policy']
    assert witness.call('origin', savedAdmission=admission) == origin
    # Replay the already-dispatched duplicate again after semantic consumption/ACK; its old fence remains refused.
    assert witness.call('old-source-duplicate', **replay_arguments)['refusedStaleSource']
    assert len(witness.snapshot(run)['jobs']) == 1 and len(list(Path(report['entryDirectory']).glob('*.entry.json'))) == 1
    witness.passed(rootExecutionId=run['executionId'], jobId=acceptance['jobId'], resultId=original['resultId'],
        protectedOrigin=origin, originalSourceOutboxItemId=source['sourceOutboxItemId'], exactIntentFingerprint=source['intentFingerprint'],
        recognizeUntil=acceptance['recognizeUntil'], oldFencingToken=source['claim']['fencingToken'],
        replayFencingToken=replayed['claim']['fencingToken'], producerProcessId=producer.pid, senderReplayAfterNaturalExpiry=True,
        originalResultAcknowledgedBeforeExpiryReplay=True, freshOrdinaryKeySameOriginReplay=fresh_key_replay,
        freshPostKillSavedWaitAndOwedSenderVerified=True,
        staleInFlightDuplicateRefusedBeforeAndAfterConsumption=True)

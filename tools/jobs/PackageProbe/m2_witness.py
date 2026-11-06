"""Bounded packaged Jobs control groups. Loaded by the committed owned controller.

These are real public-API hosts and real engines. Synthetic association addresses
exercise Jobs gate metadata; they are explicitly not native/BPMN integration proof.
"""

from datetime import datetime, timedelta, timezone
import hashlib
import json
import re
from pathlib import Path
import subprocess
import sys
import time
from urllib.request import urlopen
import uuid


def instant(value):
    return value.isoformat().replace('+00:00', 'Z')


def scoped(witness, label):
    witness.base['scope'] = {'persistencePartition': 'package-' + witness.run_id + '-' + label,
                             'tenantId': 'tenant-a'}


def policy(attempts=3, timeout='00:02:00', retry_timeouts=False):
    return {'retry': {'maximumAttempts': attempts, 'initialBackoff': '00:00:00.200',
                      'maximumBackoff': '00:00:01', 'backoffMultiplier': 2, 'jitterFraction': 0,
                      'retryTimeouts': retry_timeouts},
            'attemptTimeout': timeout, 'minimumKeyRecognition': '30.00:00:00'}


def controlled(witness, label, blocked=False, **fields):
    report = witness.report(label)
    report.pop('releaseFile')
    if blocked:
        report['releaseDirectory'] = str(witness.evidence / (label + '-releases'))
        Path(report['releaseDirectory']).mkdir()
    report.update(fields)
    return report


def submit(witness, label, report, **arguments):
    value = witness.call('submit', clientKey=label, controlledInput=report, **arguments)
    assert value['ok'], value
    return value['value']['jobId']


def entries(report):
    return [json.loads(path.read_text()) for path in Path(report['entryDirectory']).glob('*.entry.json')]


def release(report, number):
    (Path(report['releaseDirectory']) / (str(number) + '.release')).touch()


def entered(witness, report, count=1):
    return witness.until(lambda: entries(report) if len(entries(report)) >= count else None,
                         str(count) + ' controlled handler entries')


def passed_report(witness, job_id, report):
    snapshot = witness.terminal(job_id)
    assert snapshot['result']['outcome'] == 'Succeeded'
    payload = snapshot['result']['payload']
    assert payload['schema'] == {'alias': 'package.report.controlled.result', 'version': 2}
    value = payload['value']
    assert (value['name'], value['lines'], value['units'], value['subtotal']) == (report['name'], 2, 5, 52.0)
    return snapshot


def gate(kind='Queue', name='default', model=None):
    return {'kind': kind, 'name': name, 'modelKind': model}


def pause(witness, address, label):
    result = witness.call('pause', gate=address, holdId=label, operationId='pause-' + label)
    assert result['ok'] and result['value']['status'] == 'Applied'


def resume(witness, address, label):
    holds = witness.call('holds', gate=address)
    assert holds['ok']
    hold = next(item for item in holds['value']['items'] if item['holdId'] == label and item['releasedAt'] is None)
    result = witness.call('release', gate=address, holdId=label, expectedRevision=hold['revision'], operationId='release-' + label)
    assert result['ok'] and result['value']['status'] == 'Applied'


def associated(model='qualified-model', definition='definition-a', version='1', instance='instance-a'):
    identity = uuid.uuid4().hex
    return {'modelKind': model, 'definitionId': definition, 'definitionVersion': version,
            'executionId': instance, 'invocationId': identity, 'materialId': identity,
            'materialSha256': hashlib.sha256(b'qualification material').hexdigest(),
            'mode': 'Independent', 'continuation': None}


def admit_associated(witness, label, report, association, controlled_input=True):
    result = witness.call('admit-associated', clientKey=label, association=association,
                          **{'controlledInput' if controlled_input else 'input': report})
    assert result['ok'] and result['value']['status'] == 'Applied'
    return result['value']['acceptance']['jobId']


def finished(witness, process, command, expected_code=0):
    code = process.wait(timeout=45)
    witness.completed(process)
    next(item for item in witness.steps if item['processId'] == process.pid)['exitCode'] = code
    result = json.loads(Path(command['outputFile']).read_text())
    assert code == expected_code, result
    return result


def fault(witness, label, kind, event='after-commit', action='throw', **fields):
    value = {'writeKind': kind, 'event': event, 'action': action,
             'markerFile': str(witness.evidence / (label + '-fault.json')), **fields}
    if 'block' in action:
        value['releaseFile'] = str(witness.evidence / (label + '-fault-release'))
    return value


def observed(witness, descriptor):
    path = Path(descriptor['markerFile'])
    witness.until(path.is_file, 'exact EF diagnostic fault marker')
    value = json.loads(path.read_text())
    assert value['evidence']['kind'] == descriptor['writeKind']
    assert value['commitObserved'] == (descriptor['event'] == 'after-commit')
    assert value['evidence']['transactionId']
    assert not Path(descriptor['markerFile'] + '.timed-out.json').exists()
    return value


def unfreeze(descriptor):
    assert not Path(descriptor['markerFile'] + '.released.json').exists()
    assert not Path(descriptor['markerFile'] + '.timed-out.json').exists()
    Path(descriptor['releaseFile']).touch()


def released(witness, descriptor, marker):
    path = Path(descriptor['markerFile'] + '.released.json')
    witness.until(path.is_file, 'external barrier release without timeout')
    value = json.loads(path.read_text())
    assert not value['timedOut'] and value['transactionId'] == marker['evidence']['transactionId']
    assert value['contextId'] == marker['evidence']['contextId']
    assert not Path(descriptor['markerFile'] + '.timed-out.json').exists()


def propagated(witness, descriptor, marker):
    path = Path(descriptor['markerFile'] + '.propagated.json')
    witness.until(path.is_file, 'the exact controlled DbException at EF TransactionError', timeout=8)
    value = json.loads(path.read_text())
    assert value['exactExceptionInstance'] and value['count'] == 1 and value['occurrence'] == 1
    assert value['contextId'] == marker['evidence']['contextId']
    assert value['transactionId'] == marker['evidence']['transactionId']
    assert value['providerEvent'].endswith('.TransactionError')
    return value


def utc_ticks(value):
    match = re.fullmatch(r'(.*T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})', value)
    assert match is not None
    whole = datetime.fromisoformat(match[1] + match[3].replace('Z', '+00:00')).astimezone(timezone.utc)
    delta = whole - datetime(1, 1, 1, tzinfo=timezone.utc)
    return (delta.days * 86400 + delta.seconds) * 10_000_000 + int((match[2] or '').ljust(7, '0'))


def _jobs(witness, queue):
    result = witness.call('list')
    assert result['ok'] and result['value']['continuationToken'] is None
    return [job for job in result['value']['items'] if job['request'] and job['request']['queue'] == queue]


def _save(witness, label, recurrence, first, missed='CoalesceOne', queue=None):
    report = controlled(witness, label)
    result = witness.call('save-schedule', scheduleId=label, operationId='save-' + label,
                          controlledInput=report, queue=queue or label, recurrence=recurrence,
                          firstOccurrence=instant(first), missedPolicy=missed)
    assert result['ok'] and result['value']['status'] == 'Applied'
    return report


def _disable(witness, label):
    for index in range(8):
        schedule = witness.call('schedule', scheduleId=label)['value']
        result = witness.call('schedule-enabled', scheduleId=label, enabled=False,
                              expectedRevision=schedule['revision'], operationId='disable-' + label + '-' + str(index))
        assert result['ok']
        if result['value']['status'] == 'Applied':
            return
        assert result['value']['status'] == 'Conflict'
    raise AssertionError('Could not revision-check the schedule disable within the bound.')


def schedule_interval(witness, receipt):
    scoped(witness, 'interval')
    queue = 'interval'
    pause(witness, gate(name=queue), 'interval-outage')
    report = _save(witness, queue, {'kind': 'Interval', 'expression': 'PT1M'},
                   datetime.now(timezone.utc) - timedelta(minutes=5))
    worker = witness.worker('interval-pump', queues=[queue], maximumOccurrences=2)
    time.sleep(0.5)
    assert _jobs(witness, queue) == []
    before = witness.call('schedule', scheduleId=queue)['value']
    resume(witness, gate(name=queue), 'interval-outage')
    jobs = witness.until(lambda: _jobs(witness, queue), 'one interval catch-up acceptance')
    assert len(jobs) == 1
    selected = passed_report(witness, jobs[0]['jobId'], report)
    witness.stop(worker)
    after = witness.call('schedule', scheduleId=queue)['value']
    assert after['nextOccurrence'] > before['nextOccurrence']
    assert datetime.fromisoformat(after['nextOccurrence'].replace('Z', '+00:00')) > datetime.now(timezone.utc)
    restarted = witness.worker('interval-restarted', queues=[queue], maximumOccurrences=2)
    time.sleep(0.4)
    assert [job['jobId'] for job in _jobs(witness, queue)] == [selected['jobId']]
    assert witness.snapshot(selected['jobId'])['result']['resultId'] == selected['result']['resultId']
    witness.stop(restarted)
    receipt.update(jobId=selected['jobId'], resultId=selected['result']['resultId'], nextOccurrence=after['nextOccurrence'])


def schedule_cron_replay(witness, receipt):
    scoped(witness, 'cron-replay')
    now = datetime.now(timezone.utc)
    cron = _save(witness, 'cron', {'kind': 'Cron', 'expression': '*/2 * * * * *'}, now - timedelta(seconds=20))
    replay = _save(witness, 'replay', {'kind': 'Interval', 'expression': '01:00:00'}, now - timedelta(hours=4),
                   missed='ReplayEveryOccurrence')
    worker = witness.worker('cron-replay-pump', queues=['cron', 'replay'], maximumOccurrences=2)
    cron_jobs = witness.until(lambda: _jobs(witness, 'cron'), 'real cron acceptance')
    _disable(witness, 'cron')
    replay_jobs = witness.until(lambda: _jobs(witness, 'replay') if len(_jobs(witness, 'replay')) >= 4 else None,
                                'bounded explicit replay acceptance')
    _disable(witness, 'replay')
    cron_schedule = witness.call('schedule', scheduleId='cron')['value']
    next_cron = datetime.fromisoformat(cron_schedule['nextOccurrence'].replace('Z', '+00:00'))
    assert next_cron.second % 2 == 0 and next_cron.microsecond == 0
    for job in _jobs(witness, 'cron'):
        passed_report(witness, job['jobId'], cron)
    final_replay = _jobs(witness, 'replay')
    assert 4 <= len(final_replay) <= 5 and len({job['jobId'] for job in final_replay}) == len(final_replay)
    for job in final_replay:
        passed_report(witness, job['jobId'], replay)
    witness.stop(worker)
    retained = {job['jobId'] for job in _jobs(witness, 'replay')}
    assert retained == {job['jobId'] for job in final_replay}
    receipt.update(cronJobIds=[job['jobId'] for job in cron_jobs], replayJobIds=sorted(retained),
                   cronNextOccurrence=cron_schedule['nextOccurrence'])


def delayed(witness, receipt):
    scoped(witness, 'delayed')
    # Refusal is established against a distant immutable due time, so host
    # bootstrap latency cannot turn a valid later grant into a false failure.
    distant = controlled(witness, 'delayed-distant')
    distant_due = datetime.now(timezone.utc) + timedelta(minutes=10)
    distant_id = submit(witness, 'delayed-distant', distant, notBefore=instant(distant_due))
    refused = witness.call('claim', claimRequestId=uuid.uuid4().hex)
    assert refused['ok'] and refused['value']['status'] == 'NoEligibleWork'
    assert refused['value']['lease'] is None and witness.attempts(distant_id) == []
    report = controlled(witness, 'delayed-report')
    due = datetime.now(timezone.utc) + timedelta(seconds=3)
    job_id = submit(witness, 'delayed', report, notBefore=instant(due))
    worker = witness.worker('delayed-worker', materializeSchedules=False)
    selected = passed_report(witness, job_id, report)
    attempt = witness.attempts(job_id)[0]
    assert datetime.fromisoformat(attempt['lease']['grantedAt'].replace('Z', '+00:00')) >= due
    assert witness.attempts(distant_id) == [] and entries(distant) == []
    assert witness.snapshot(distant_id)['state'] == 'Accepted'
    witness.stop(worker)
    receipt.update(jobId=job_id, resultId=selected['result']['resultId'], notBefore=instant(due),
                   distantHeldJobId=distant_id, distantNotBefore=instant(distant_due),
                   noEarlyGrant='Distant direct-claim refusal plus actual grant timestamp at/after its own due time.')


def post_commit(witness, receipt, kind, marker_only=False):
    scoped(witness, 'ack-' + kind)
    report = controlled(witness, 'ack-' + kind, blocked=kind == 'renew')
    descriptor = fault(witness, 'ack-' + kind, kind, action='block' if marker_only else 'block-then-throw',
                       requirePhysicalStarted=kind == 'renew')
    if kind == 'admit':
        producer, command = witness.command('submit', clientKey='ack-admit', controlledInput=report, fault=descriptor)
        marker = observed(witness, descriptor)
        job_id = marker['evidence']['jobId']
        assert witness.snapshot(job_id)['state'] == 'Accepted'
        unfreeze(descriptor)
        released(witness, descriptor, marker)
        proof = propagated(witness, descriptor, marker)
        unavailable = finished(witness, producer, command, expected_code=1)
        assert not unavailable['ok'] and unavailable['code'] == 'jobs.persistence.unavailable'
        assert submit(witness, 'ack-admit', report) == job_id
        worker = witness.worker('ack-admit-worker', materializeSchedules=False)
    else:
        job_id = submit(witness, 'ack-' + kind, report)
        descriptor['targetJobId'] = job_id
        worker = witness.worker('ack-' + kind + '-worker', fault=descriptor, materializeSchedules=False,
                                leaseSeconds=10)
        marker = observed(witness, descriptor)
        snapshot = witness.snapshot(job_id)
        assert marker['evidence']['jobId'] == job_id
        if kind == 'complete':
            assert snapshot['state'] == 'Terminal'
            assert snapshot['result']['resultId'] == marker['evidence']['resultId']
        else:
            assert snapshot['activeAttempt']['attemptId'] == marker['evidence']['attemptId']
            if kind == 'renew':
                active = snapshot['activeAttempt']
                assert active['revision'] >= marker['evidence']['ownershipRevision']
                assert utc_ticks(active['expiresAt']) >= marker['evidence']['expiresAt']
                receipt['persistedRenewal'] = {'revision': active['revision'], 'expiresAt': active['expiresAt'],
                    'recordedRevision': marker['evidence']['ownershipRevision'], 'recordedExpiresAtTicks': marker['evidence']['expiresAt'],
                    'laterLegitimateRenewal': active['revision'] > marker['evidence']['ownershipRevision']}
        unfreeze(descriptor)
        released(witness, descriptor, marker)
        proof = propagated(witness, descriptor, marker)
        if kind == 'renew':
            release(report, 1)
    result = passed_report(witness, job_id, report)
    assert len(witness.attempts(job_id)) == 1 and len(entries(report)) == 1
    physical = witness.call('receipts', jobId=job_id)
    assert physical['ok'] and len(physical['value']['items']) == 1
    witness.stop(worker)
    proof = propagated(witness, descriptor, marker)
    receipt.update(jobId=job_id, resultId=result['result']['resultId'], providerEvent=marker['providerEvent'],
                   transactionId=marker['evidence']['transactionId'], commitObserved=True,
                   controlledExceptionPropagation=proof,
                   fault='Post-COMMIT store-response failure; no network-packet loss claim.')


def pause_order(witness, receipt, after_commit):
    direct_sqlite = not after_commit and witness.provider == 'sqlite'
    label = 'grant-before-pause' if after_commit else 'pause-before-direct-transaction' if direct_sqlite else 'pause-after-selection-before-save'
    scoped(witness, label)
    report = controlled(witness, label)
    job_id = submit(witness, label, report)
    descriptor = fault(witness, label, 'claim', event='after-commit' if after_commit else 'before-transaction' if direct_sqlite else 'before-save',
                       action='block', targetJobId=job_id)
    if direct_sqlite:
        direct, command = witness.command('claim', fault=descriptor, leaseSeconds=10, claimRequestId=uuid.uuid4().hex)
        worker = None
    else:
        worker = witness.worker(label + '-worker', fault=descriptor, leaseSeconds=10, materializeSchedules=False)
    marker = observed(witness, descriptor)
    assert entries(report) == []
    pause(witness, gate(), label)
    # These checks occur after a successful external Pause commit and before
    # release; a timed-out/rolled-back claim cannot satisfy this evidence gate.
    assert not Path(descriptor['markerFile'] + '.released.json').exists()
    assert not Path(descriptor['markerFile'] + '.timed-out.json').exists()
    snapshot = witness.snapshot(job_id)
    if after_commit:
        assert snapshot['activeAttempt']['attemptId'] == marker['evidence']['attemptId']
    else:
        assert snapshot['activeAttempt'] is None
    unfreeze(descriptor)
    released(witness, descriptor, marker)
    if direct_sqlite:
        confirmation = finished(witness, direct, command)
        assert confirmation['ok'] and confirmation['value']['status'] == 'NoEligibleWork'
        assert confirmation['value']['lease'] is None
        assert marker['evidence']['changeTrackerWriteObserved'] is False
        worker = witness.worker(label + '-worker', materializeSchedules=False)
    if after_commit:
        passed_report(witness, job_id, report)
    else:
        time.sleep(0.6)
        assert witness.attempts(job_id) == [] and entries(report) == []
    resume(witness, gate(), label)
    result = passed_report(witness, job_id, report)
    witness.stop(worker)
    receipt.update(jobId=job_id, resultId=result['result']['resultId'], commitObserved=after_commit,
                   controlledEvent=marker['providerEvent'],
                   topology='SQLite direct claim before transaction/writer lock; no post-selection interleaving claim.'
                       if direct_sqlite else 'PostgreSQL selection-before-save' if not after_commit else 'Committed grant before physical entry',
                   barrierReleasedWithoutTimeout=True)


def four_holds(witness, receipt):
    scoped(witness, 'four-holds')
    addresses = {'queue': gate(), 'type': gate('JobType', 'package.report'),
                 'instance': gate('WorkflowInstance', 'instance-a', 'qualified-model'),
                 'definition': gate('WorkflowDefinition', 'definition-a', 'qualified-model')}
    for label, address in addresses.items():
        pause(witness, address, 'overlap-' + label)
    first = controlled(witness, 'associated-v2')
    first_id = admit_associated(witness, 'associated-v2', first, associated())
    future = witness.report('associated-future-definition-v1')
    future_id = admit_associated(witness, 'future-definition', future,
                                  associated(version='2', instance='instance-b'), controlled_input=False)
    unrelated = witness.report('unrelated-shared-queue')
    unrelated_id = witness.call('submit', clientKey='unrelated', input=unrelated, submitUnrelated=True)['value']['jobId']
    workers = [witness.worker('holds-worker-' + str(index), materializeSchedules=False) for index in range(2)]
    assert all(worker[0].poll() is None for worker in workers)
    assert all(witness.attempts(job) == [] for job in (first_id, future_id, unrelated_id))
    resume(witness, addresses['queue'], 'overlap-queue')
    witness.check_report(witness.terminal(unrelated_id), unrelated)
    assert witness.attempts(first_id) == [] and witness.attempts(future_id) == []
    resume(witness, addresses['type'], 'overlap-type')
    assert witness.attempts(first_id) == [] and witness.attempts(future_id) == []
    resume(witness, addresses['definition'], 'overlap-definition')
    witness.check_report(witness.terminal(future_id), future)
    assert witness.attempts(first_id) == []
    resume(witness, addresses['instance'], 'overlap-instance')
    passed_report(witness, first_id, first)
    for worker in workers:
        witness.stop(worker)
    assert all(len(witness.attempts(job)) == 1 for job in (first_id, future_id, unrelated_id))
    receipt.update(jobIds=[first_id, future_id, unrelated_id], simultaneousWorkerProcesses=2,
                   handlerVersions=['1', '2'], definitionVersions=['1', '2'],
                   associationFixture='Synthetic qualified-model addresses; native/BPMN integration NOT RUN.')


def parallel_workers(witness, receipt):
    scoped(witness, 'parallel-workers')
    reports = [controlled(witness, 'parallel-' + str(index), blocked=True) for index in range(2)]
    jobs = [submit(witness, 'parallel-' + str(index), report) for index, report in enumerate(reports)]
    workers = [witness.worker('parallel-worker-' + str(index), materializeSchedules=False) for index in range(2)]
    for report in reports:
        entered(witness, report)
    active = [witness.snapshot(job)['activeAttempt'] for job in jobs]
    assert len({attempt['owner'] for attempt in active}) == 2
    assert all(not list(Path(report['entryDirectory']).glob('*.exit.json')) for report in reports)
    for report in reports:
        release(report, 1)
    for job, report in zip(jobs, reports):
        passed_report(witness, job, report)
    for worker in workers:
        witness.stop(worker)
    receipt.update(jobIds=jobs, distinctLiveOwners=[attempt['owner'] for attempt in active], simultaneousPhysicalAttempts=2)


def capacity(witness, receipt, kind):
    scoped(witness, 'capacity-' + kind.lower())
    address = gate(kind, {'Queue': 'default', 'JobType': 'package.report',
                          'WorkflowInstance': 'capacity-instance', 'WorkflowDefinition': 'capacity-definition'}[kind],
                   'qualified-model' if kind.startswith('Workflow') else None)
    limited = witness.call('capacity', gate=address, capacity=1, operationId='limit-' + kind)
    assert limited['ok'] and limited['value']['status'] == 'Applied'
    reports = [controlled(witness, 'capacity-' + kind + '-' + str(index), blocked=True) for index in range(2)]
    jobs = [admit_associated(witness, 'capacity-' + str(index), report,
                              associated(definition='capacity-definition', version=str(index + 1), instance='capacity-instance'))
            for index, report in enumerate(reports)]
    workers = [witness.worker('capacity-' + kind + '-worker-' + str(index), materializeSchedules=False) for index in range(2)]
    # Use index+1 because index zero is a valid winner and not a false predicate.
    winner = witness.until(lambda: next((index + 1 for index, report in enumerate(reports) if entries(report)), 0),
                           'one capacity winner') - 1
    other = 1 - winner
    time.sleep(0.5)
    assert len(entries(reports[winner])) == 1 and entries(reports[other]) == []
    assert witness.attempts(jobs[other]) == []
    assert all(worker[0].poll() is None for worker in workers)
    release(reports[winner], 1)
    passed_report(witness, jobs[winner], reports[winner])
    entered(witness, reports[other])
    assert list(Path(reports[winner]['entryDirectory']).glob('*.exit.json'))
    release(reports[other], 1)
    passed_report(witness, jobs[other], reports[other])
    for worker in workers:
        witness.stop(worker)
    receipt.update(gate=address, jobIds=jobs, simultaneousWorkerProcesses=2,
                   ceiling='Valid authorized leases; stale physical exclusion is separately qualified.')


def held_backlog(witness, receipt):
    scoped(witness, 'held-backlog')
    address = gate('JobType', 'package.report')
    pause(witness, address, 'thousand-held')
    report = witness.report('held-backlog-input')
    accepted = witness.call('submit-batch', clientKey='held-batch', input=report, batchCount=1001)
    assert accepted['ok'] and len(accepted['value']) == 1001
    healthy = witness.report('healthy-after-thousand')
    healthy_id = witness.call('submit', clientKey='healthy-after-thousand', input=healthy, submitUnrelated=True)['value']['jobId']
    workers = [witness.worker('backlog-worker-' + str(index), materializeSchedules=False) for index in range(2)]
    witness.check_report(witness.terminal(healthy_id), healthy)
    assert witness.attempts(accepted['value'][0]['jobId']) == []
    assert list(Path(report['entryDirectory']).glob('*.json')) == []
    for worker in workers:
        witness.stop(worker)
    receipt.update(heldAcceptedJobs=1001, healthyJobId=healthy_id, simultaneousWorkerProcesses=2,
                   heldBacklog='Retained and held; no purge or coalescing of accepted jobs.')


def automatic_retry(witness, receipt):
    scoped(witness, 'automatic-retry')
    report = controlled(witness, 'automatic-retry', blocked=True, failUntilAttempt=1)
    job_id = submit(witness, 'automatic-retry', report, policy=policy())
    worker = witness.worker('retry-worker', materializeSchedules=False)
    entered(witness, report)
    address = gate('JobType', 'package.report')
    pause(witness, address, 'retry-held')
    release(report, 1)
    witness.until(lambda: witness.snapshot(job_id)['state'] == 'Accepted', 'durable retry responsibility')
    time.sleep(0.5)
    assert len(entries(report)) == len(witness.attempts(job_id)) == 1
    resume(witness, address, 'retry-held')
    entered(witness, report, 2)
    release(report, 2)
    selected = passed_report(witness, job_id, report)
    witness.stop(worker)
    assert len(witness.attempts(job_id)) == 2 and len({item['idempotencyKey'] for item in entries(report)}) == 1
    receipt.update(jobId=job_id, resultId=selected['result']['resultId'], attempts=2, sameInvocationKey=True)


def cancel_deadline(witness, receipt, cancel):
    label = 'cancel' if cancel else 'deadline'
    scoped(witness, label)
    report = controlled(witness, label, blocked=True)
    job_id = submit(witness, label, report, policy=policy(attempts=3, timeout='00:00:01' if not cancel else '00:02:00'))
    worker = witness.worker(label + '-worker', materializeSchedules=False)
    entered(witness, report)
    if cancel:
        result = witness.call('cancel', jobId=job_id, operationId='cancel-running')
        assert result['ok'] and result['value']['status'] == 'Applied'
    selected = witness.terminal(job_id)
    assert selected['result']['outcome'] == ('Cancelled' if cancel else 'TimedOut')
    assert len(witness.attempts(job_id)) == 1
    witness.stop(worker)
    assert list(Path(report['entryDirectory']).glob('*.exit.json'))
    if not cancel:
        attempt = witness.attempts(job_id)[0]
        assert selected['result']['completedAt'] >= attempt['lease']['deadline']
    receipt.update(jobId=job_id, outcome=selected['result']['outcome'], resultId=selected['result']['resultId'])


def graceful_drain(witness, receipt):
    scoped(witness, 'drain')
    report = controlled(witness, 'drain', workMilliseconds=800)
    job_id = submit(witness, 'drain', report)
    worker = witness.worker('drain-worker', materializeSchedules=False, shutdownGraceMilliseconds=3000)
    entered(witness, report)
    witness.stop(worker)
    passed_report(witness, job_id, report)
    assert len(witness.attempts(job_id)) == 1
    abandoned = controlled(witness, 'drain-expired', blocked=True)
    abandoned_id = submit(witness, 'drain-expired', abandoned)
    worker = witness.worker('short-drain-worker', materializeSchedules=False, shutdownGraceMilliseconds=100)
    entered(witness, abandoned)
    witness.stop(worker)
    assert witness.snapshot(abandoned_id)['state'] == 'Accepted'
    successor = witness.worker('drain-successor', materializeSchedules=False)
    entered(witness, abandoned, 2)
    release(abandoned, 2)
    passed_report(witness, abandoned_id, abandoned)
    witness.stop(successor)
    assert len(witness.attempts(abandoned_id)) == 2
    receipt.update(drainedJobId=job_id, abandonedThenRecoveredJobId=abandoned_id,
                   shutdown='A grace-expired attempt remains retry responsibility, not user cancellation.')


def manual_retry(witness, receipt):
    scoped(witness, 'manual-retry')
    failure = witness.evidence / 'manual-failure-switch'
    failure.touch()
    report = controlled(witness, 'manual-retry', failureFile=str(failure))
    job_id = submit(witness, 'manual-retry', report, policy=policy(attempts=1))
    worker = witness.worker('manual-retry-worker', materializeSchedules=False)
    original = witness.terminal(job_id)
    assert original['result']['outcome'] == 'Failed'
    failure.unlink()
    operation = {'jobId': job_id, 'expectedRevision': original['revision'],
                 'operationId': 'manual-retry', 'clientKey': 'manual-new-run'}
    accepted = witness.call('retry-as-new', **operation)
    assert accepted['ok'] and accepted['value']['status'] == 'Applied'
    new_id = accepted['value']['acceptance']['jobId']
    assert new_id != job_id
    replay = witness.call('retry-as-new', **operation)
    assert replay['ok'] and replay['value']['status'] == 'Replayed'
    assert replay['value']['acceptance']['jobId'] == new_id
    selected = passed_report(witness, new_id, report)
    assert selected['retriedFromJobId'] == job_id
    assert witness.snapshot(job_id)['result']['resultId'] == original['result']['resultId']
    witness.stop(worker)
    receipt.update(originalJobId=job_id, originalResultId=original['result']['resultId'], newJobId=new_id,
                   newResultId=selected['result']['resultId'], authorization='Trusted qualification host; API authorization NOT RUN.')


def _destination(witness, label, database, port=0):
    ready, stop = witness.evidence / (label + '-ready.json'), witness.evidence / (label + '-stop')
    log_path = witness.evidence / (label + '.log')
    log = log_path.open('w')
    process = subprocess.Popen([sys.executable, str(Path(__file__).with_name('ledger_destination.py')),
                                '--database', str(database), '--ready-file', str(ready),
                                '--stop-file', str(stop), '--port', str(port)],
                               cwd=witness.entry.parent, stdin=subprocess.DEVNULL, stdout=log,
                               stderr=subprocess.STDOUT, start_new_session=True)
    witness.processes.append((process, log))
    witness.steps.append({'mode': 'effect-destination', 'processId': process.pid, 'log': str(log_path),
                          'qualificationFixture': True, 'database': str(database)})
    witness.until(ready.is_file, 'owned HTTP destination readiness', process=process)
    return process, stop, json.loads(ready.read_text())['url']


def _stop_destination(witness, destination):
    process, stop, _ = destination
    stop.touch()
    code = process.wait(timeout=5)
    witness.completed(process)
    next(item for item in witness.steps if item['processId'] == process.pid)['exitCode'] = code
    assert code == 0


def _effect_state(url):
    with urlopen(url + '/state', timeout=5) as response:
        return json.loads(response.read())


def physical_overlap_dedup(witness, receipt):
    scoped(witness, 'physical-overlap')
    ledger = witness.evidence / 'owned-effect-ledger.db'
    destination = _destination(witness, 'effect-first', ledger)
    report = controlled(witness, 'physical-overlap', blocked=True, ignoreCancellation=True,
                        effectUrl=destination[2] + '/effects')
    limited = witness.call('capacity', gate=gate(), capacity=1, operationId='overlap-limit')
    assert limited['ok'] and limited['value']['status'] == 'Applied'
    job_id = submit(witness, 'physical-overlap', report)
    descriptor = fault(witness, 'physical-overlap', 'renew', event='before-save', action='throw',
                       targetJobId=job_id, requirePhysicalStarted=True, repeat=True)
    first = witness.worker('overlap-old-worker', fault=descriptor, materializeSchedules=False)
    initial = entered(witness, report)[0]
    witness.until(lambda: list(Path(report['entryDirectory']).glob('*.effect.json')), 'first committed destination effect')
    observed(witness, descriptor)
    witness.until(lambda: list(Path(report['entryDirectory']).glob('*.cancellation.json')), 'stale handler ignoring cancellation')
    assert len(_effect_state(destination[2])['effects']) == 1
    original_url = destination[2]
    port = int(original_url.rsplit(':', 1)[1])
    _stop_destination(witness, destination)
    destination = _destination(witness, 'effect-restarted', ledger, port=port)
    assert destination[2] == original_url and len(_effect_state(original_url)['effects']) == 1
    successor = witness.worker('overlap-successor-worker', materializeSchedules=False)
    both = entered(witness, report, 2)
    witness.until(lambda: len(list(Path(report['entryDirectory']).glob('*.effect.json'))) == 2,
                   'second physical request recognized by durable destination')
    assert first[0].poll() is None and successor[0].poll() is None
    assert list(Path(report['entryDirectory']).glob('*.exit.json')) == []
    state = _effect_state(original_url)
    assert len(state['requests']) == 2 and len(state['effects']) == 1
    assert len({item['idempotencyKey'] for item in both}) == 1
    assert len({item['attemptId'] for item in state['requests']}) == 2
    release(report, 2)
    selected = passed_report(witness, job_id, report)
    assert selected['result']['attemptId'] != initial['attemptId']
    assert not (Path(report['entryDirectory']) / (initial['attemptId'] + '.exit.json')).is_file()
    release(report, 1)
    witness.until(lambda: len(list(Path(report['entryDirectory']).glob('*.exit.json'))) == 2, 'late stale physical exit')
    witness.until(lambda: len(witness.call('receipts', jobId=job_id)['value']['items']) == 2, 'two durable physical receipts')
    assert witness.snapshot(job_id)['result']['resultId'] == selected['result']['resultId']
    witness.stop(first)
    witness.stop(successor)
    _stop_destination(witness, destination)
    receipt.update(jobId=job_id, resultId=selected['result']['resultId'], physicalAttempts=2, destinationRequests=2,
                   durableUniqueEffects=1, destinationRestarted=True,
                   capacityLimit=1, simultaneousPhysicalAttempts=2,
                   limitMeaning='One authorized live lease; stale physical work can overlap after authority loss.')


def groups(witness):
    return {
        'schedules': [('delayed-provider-time-start', delayed),
                      ('default-interval-catch-up-and-restart', schedule_interval),
                      ('default-cron-and-bounded-replay', schedule_cron_replay)],
        'chaos': [('actual-post-commit-' + kind + '-reply-loss',
                   lambda w, r, selected=kind: post_commit(w, r, selected)) for kind in ('admit', 'claim', 'renew', 'complete')]
                 + [('pause-before-direct-claim-transaction' if witness.provider == 'sqlite' else 'pause-before-selected-claim-save',
                     lambda w, r: pause_order(w, r, False)),
                    ('committed-grant-before-pause-before-entry', lambda w, r: pause_order(w, r, True)),
                    ('stale-physical-overlap-and-durable-downstream-dedup', physical_overlap_dedup)],
        'coordination': [('four-scope-overlap-version-coverage', four_holds),
                         ('two-live-workers-physical-parallelism', parallel_workers)]
                        + [(kind.lower() + '-authorized-capacity', lambda w, r, selected=kind: capacity(w, r, selected))
                           for kind in ('Queue', 'JobType', 'WorkflowInstance', 'WorkflowDefinition')]
                        + [('1001-held-jobs-unrelated-shared-queue-progress', held_backlog)],
        'attempts': [('automatic-retry-held-without-physical-rerun', automatic_retry),
                     ('cooperative-cancel', lambda w, r: cancel_deadline(w, r, True)),
                     ('absolute-provider-deadline-no-timeout-retry', lambda w, r: cancel_deadline(w, r, False)),
                     ('graceful-drain-and-grace-expired-recovery', graceful_drain),
                     ('idempotent-linked-retry-as-new-run', manual_retry)]
    }


def selected_cases(witness, group, negative_control=None):
    selected = [item for key, values in groups(witness).items() if group in ('all', key) for item in values]
    if negative_control is not None:
        kind = negative_control.removeprefix('marker-only-')
        selected = [('marker-only-' + kind + '-must-not-qualify',
                     lambda w, r: post_commit(w, r, kind, marker_only=True))]
    if not selected:
        raise ValueError('This control group is not implemented yet; acceptance remains NOT RUN.')
    return selected


def planned_names(witness, group, negative_control=None):
    return [name for name, _ in selected_cases(witness, group, negative_control)]


def run_group(witness, group, negative_control=None):
    selected = selected_cases(witness, group, negative_control)
    pending = [(name, action, {'name': name, 'state': 'NOT RUN'}) for name, action in selected]
    witness.cases.extend(item[2] for item in pending)
    for name, action, receipt in pending:
        receipt['state'] = 'IN PROGRESS'
        try:
            action(witness, receipt)
            receipt['state'] = 'PASSED'
        except Exception:
            receipt['state'] = 'FAILED'
            raise

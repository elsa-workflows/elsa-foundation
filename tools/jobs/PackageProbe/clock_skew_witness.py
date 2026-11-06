#!/usr/bin/env python3
"""Separate actual worker wall-clock skew witness; leaves the M1/M2 case plans unchanged."""

import argparse
from datetime import datetime, timedelta, timezone
import hashlib
import json
from pathlib import Path
import signal
import subprocess
import sys
import time

from run_witness import Witness, write_json


CASES = ('application-clock-ahead-provider-time-authority',
         'application-clock-behind-provider-time-authority')
LEASE_SECONDS = 3
OFFSETS = (48, -48)


def utc(value):
    return datetime.fromisoformat(value.replace('Z', '+00:00')).astimezone(timezone.utc)


def instant(value):
    return value.isoformat().replace('+00:00', 'Z')


def entries(report):
    return [json.loads(path.read_text()) for path in Path(report['entryDirectory']).glob('*.json')]


def run(witness):
    witness.active_case = CASES[0]
    witness.initialize_store()
    version = witness.call('provider-version')
    assert version['ok'] and version['value']['version']
    witness.provider_version = version['value']
    runs = []
    for index, offset in enumerate(OFFSETS):
        witness.active_case = CASES[index]
        label = 'clock-ahead' if offset > 0 else 'clock-behind'
        queue = label
        worker = witness.worker(label, queues=[queue], maximumConcurrentAttempts=1,
            materializeSchedules=False, applicationClockOffsetHours=offset,
            leaseSeconds=LEASE_SECONDS, renewalMilliseconds=500, timeoutSeconds=150)
        ready = json.loads((witness.evidence / (label + '-ready.json')).read_text())
        assert ready['ready'] and ready['processId'] == worker[0].pid
        assert ready['applicationClockOffsetHours'] == offset
        assert ready['timeProviderType'] == 'ProbeApplicationClock'
        skew = (utc(ready['applicationUtcNow']) - utc(ready['systemUtcNow'])).total_seconds()
        assert abs(skew - offset * 3600) < 1, 'Worker did not observe the requested actual application clock.'
        runs.append({'name': CASES[index], 'label': label, 'queue': queue, 'offset': offset,
                     'worker': worker, 'ready': ready})
    assert runs[0]['worker'][0].pid != runs[1]['worker'][0].pid

    # Both opposing-clock hosts are alive concurrently, but can only claim their own queue.
    for item in runs:
        witness.active_case = item['name']
        report = witness.report(item['label'], blocked=True)
        due = datetime.now(timezone.utc) + timedelta(seconds=15)
        submitted = witness.call('submit', clientKey=item['label'], input=report,
                                 queue=item['queue'], notBefore=instant(due))
        assert submitted['ok'], submitted
        acceptance = submitted['value']
        assert due - utc(acceptance['acceptedAt']) >= timedelta(seconds=5), 'No future provider-time window remained.'
        snapshot = witness.snapshot(acceptance['jobId'])
        assert snapshot['acceptedAt'] == acceptance['acceptedAt']
        assert snapshot['request']['queue'] == item['queue'] and utc(snapshot['request']['notBefore']) == due
        assert snapshot['state'] == 'Accepted' and snapshot['activeAttempt'] is None
        assert witness.attempts(acceptance['jobId']) == [] and entries(report) == []
        observed_at = datetime.now(timezone.utc)
        assert observed_at < due, 'The pre-due refusal observation missed its window.'
        application_ready = utc(item['ready']['applicationUtcNow'])
        assert (application_ready > due if item['offset'] > 0 else application_ready < utc(acceptance['acceptedAt']))
        item.update(report=report, due=due, acceptance=acceptance,
                    preDueObservedAt=instant(observed_at))

    def require_workers():
        for item in runs:
            assert item['worker'][0].poll() is None, 'An owned skewed worker exited before the control completed.'

    for item in runs:
        witness.active_case = item['name']
        witness.until(lambda: entries(item['report']), 'skewed worker physical report entry', process=item['worker'][0])
        require_workers()
        snapshot = witness.snapshot(item['acceptance']['jobId'])
        lease = snapshot['activeAttempt']
        assert lease is not None and snapshot['result'] is None
        assert utc(lease['grantedAt']) >= item['due'], 'Authority granted a delayed invocation before NotBefore.'
        assert utc(lease['grantedAt']) >= utc(item['acceptance']['acceptedAt'])
        assert utc(lease['deadline']) - utc(lease['grantedAt']) == timedelta(minutes=2)
        observed = entries(item['report'])
        assert len(observed) == 1 and observed[0]['attemptId'] == lease['attemptId']
        assert observed[0]['jobId'] == item['acceptance']['jobId'] and observed[0]['attemptNumber'] == 1
        assert len(witness.attempts(item['acceptance']['jobId'])) == 1
        item.update(initialLease=lease, entry=observed[0], blockedSince=time.monotonic())
    assert runs[0]['initialLease']['owner'] != runs[1]['initialLease']['owner']

    for item in runs:
        witness.active_case = item['name']
        original = item['initialLease']

        def renewed():
            require_workers()
            current = witness.snapshot(item['acceptance']['jobId'])
            lease = current['activeAttempt']
            assert current['result'] is None and lease is not None, 'Blocked handler terminated before release.'
            assert len(entries(item['report'])) == 1
            assert len(witness.attempts(item['acceptance']['jobId'])) == 1
            for field in ('jobId', 'attemptId', 'attemptNumber', 'owner', 'fence', 'grantedAt', 'deadline'):
                assert lease[field] == original[field], 'Renewal replaced the authorized attempt.'
            # ExpiresAt minus the configured lease duration is a lower bound on provider renewal time.
            # A later revision and that time beyond the original expiry prove actual committed renewal.
            past_initial_expiry = utc(lease['expiresAt']) > utc(original['grantedAt']) + timedelta(seconds=2 * LEASE_SECONDS)
            blocked_long_enough = time.monotonic() - item['blockedSince'] > 2 * LEASE_SECONDS
            return lease if past_initial_expiry and blocked_long_enough and lease['revision'] > original['revision'] else None

        item['renewedLease'] = witness.until(renewed, 'same physical attempt renewed past its original lease', timeout=25)
        item['blockedSeconds'] = time.monotonic() - item['blockedSince']

    for item in runs:
        Path(item['report']['releaseFile']).touch()
    for item in runs:
        witness.active_case = item['name']
        selected = witness.terminal(item['acceptance']['jobId'])
        item['resultId'] = witness.check_report(selected, item['report'])
        assert selected['result']['attemptId'] == item['initialLease']['attemptId']
        assert utc(selected['result']['completedAt']) >= utc(item['renewedLease']['expiresAt']) - timedelta(seconds=LEASE_SECONDS)
        assert len(entries(item['report'])) == 1
        item['selected'] = selected
    for item in runs:
        witness.stop(item['worker'])
    for item in runs:
        witness.active_case = item['name']
        fresh = witness.snapshot(item['acceptance']['jobId'])
        assert witness.check_report(fresh, item['report']) == item['resultId']
        assert fresh['result'] == item['selected']['result'] and fresh['revision'] == item['selected']['revision']
        attempts = witness.attempts(item['acceptance']['jobId'])
        assert len(attempts) == 1 and attempts[0]['state'] == 'Succeeded'
        assert attempts[0]['lease']['attemptId'] == item['initialLease']['attemptId']
        receipts = witness.call('receipts', jobId=item['acceptance']['jobId'])
        assert receipts['ok'] and receipts['value']['continuationToken'] is None
        assert len(receipts['value']['items']) == 1
        physical = receipts['value']['items'][0]
        assert physical['attemptId'] == item['initialLease']['attemptId'] and physical['outcome'] == 'Succeeded'
        # JobsWorker stamps this receipt with the injected clock. This proves the override reached
        # actual attempt execution as well as readiness; selected completion remains provider time.
        report_skew = (utc(physical['reportedAt']) - utc(fresh['result']['completedAt'])).total_seconds()
        assert abs(report_skew - item['offset'] * 3600) < 120
        assert item['entry']['idempotencyKey'] == fresh['result']['payload']['value']['invocationKey']
        assert len(entries(item['report'])) == 1
        witness.cases.append({'name': item['name'], 'state': 'PASSED',
            'jobId': item['acceptance']['jobId'], 'queue': item['queue'],
            'workerReady': item['ready'], 'acceptedAt': item['acceptance']['acceptedAt'],
            'notBefore': instant(item['due']), 'preDueObservedAt': item['preDueObservedAt'],
            'initialObservedLease': item['initialLease'], 'renewedLease': item['renewedLease'],
            'blockedSeconds': item['blockedSeconds'], 'physicalEntryCount': 1, 'attemptCount': 1,
            'physicalReportedAt': physical['reportedAt'], 'providerCompletedAt': fresh['result']['completedAt'],
            'resultId': item['resultId'], 'payloadSha256': fresh['result']['payloadSha256'], 'subtotal': 52})
    witness.active_case = None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    if not __debug__:
        parser.error('Python optimization disables clock-skew witness assertions. Run without -O/PYTHONOPTIMIZE.')
    parser.add_argument('--entry-assembly', type=Path, required=True)
    parser.add_argument('--evidence-dir', type=Path, required=True)
    parser.add_argument('--provider', choices=('sqlite', 'postgresql'), required=True)
    args = parser.parse_args()
    entry = args.entry_assembly.resolve(strict=True)
    if not entry.is_file() or entry.name != 'Jobs.PackageProbe.dll':
        raise ValueError('Use the actual published Jobs.PackageProbe.dll entry assembly.')
    evidence = args.evidence_dir.resolve()
    evidence.mkdir(parents=True, exist_ok=False)
    witness = Witness(entry, evidence, args.provider)
    source = Path(__file__).parent
    receipt = {'schemaVersion': 1, 'state': 'NOT RUN', 'fullJobsAcceptanceState': 'INCOMPLETE',
        'qualificationScope': 'T018 worker application-clock skew only',
        'provider': args.provider, 'entryAssembly': str(entry), 'entrySha256': hashlib.sha256(entry.read_bytes()).hexdigest(),
        'controllerSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'sharedControllerSha256': hashlib.sha256((source / 'run_witness.py').read_bytes()).hexdigest(),
        'probeSourceSha256': {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(source.glob('*.cs'))},
        'plannedCaseNames': list(CASES), 'cases': [],
        'ownership': {'owner': witness.owner, 'containerLabels': witness.container_labels}}

    def stop_requested(_signum, _frame):
        raise RuntimeError('Clock-skew controller received a stop request; cleaning owned resources.')

    signal.signal(signal.SIGTERM, stop_requested)
    code = 1
    try:
        run(witness)
        receipt['state'] = 'PASSED'
        code = 0
    except (AssertionError, OSError, ValueError, KeyError, TypeError, RuntimeError, subprocess.SubprocessError) as error:
        receipt['state'] = 'FAILED'
        receipt['error'] = str(error) or type(error).__name__
    finally:
        try:
            witness.close()
            receipt['ownedResourcesCleaned'] = True
        except (OSError, RuntimeError, subprocess.SubprocessError) as error:
            receipt['state'] = 'FAILED'
            receipt['ownedResourcesCleaned'] = False
            receipt['cleanupError'] = type(error).__name__
            code = 1
        actual = {case['name']: case for case in witness.cases}
        if receipt['state'] == 'FAILED' and witness.active_case is not None and witness.active_case not in actual:
            actual[witness.active_case] = {'name': witness.active_case, 'state': 'FAILED'}
        receipt['cases'] = [actual.get(name, {'name': name, 'state': 'NOT RUN'}) for name in CASES]
        receipt['processes'] = witness.steps
        receipt['providerVersion'] = getattr(witness, 'provider_version', None)
        receipt['ownedContainerLog'] = witness.container_log
        write_json(evidence / 'clock-skew-receipt.json', receipt)
    return code


if __name__ == '__main__':
    sys.exit(main())

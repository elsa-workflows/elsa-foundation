#!/usr/bin/env python3
"""Separate opt-in retention witness; does not promote or alter the M1/M2/model case plans."""
import argparse
import hashlib
import json
from pathlib import Path
import signal
import subprocess
import sys

from run_witness import Witness, write_json

CASES = ('captured-opt-in-expiry-and-default-indefinite',
         'expired-client-key-origin-and-retry-reconciliation',
         'late-report-metadata-without-payload-restoration')


def run(witness):
    witness.active_case = CASES[0]
    witness.initialize_store()
    version = witness.call('provider-version')
    assert version['ok'] and version['value']['version']
    witness.provider_version = version['value']
    finite_input = witness.report('finite')
    ordinary = witness.call('submit', clientKey='finite', input=finite_input,
        terminalInputRetention='00:00:01', terminalResultRetention='00:00:02')['value']
    indefinite_input = witness.report('indefinite')
    indefinite = witness.call('submit', clientKey='indefinite', input=indefinite_input)['value']
    origin_input = witness.report('origin')
    origin = witness.call('admit-origin', clientKey='origin', input=origin_input,
        terminalInputRetention='00:00:01', terminalResultRetention='00:00:02')['value']['acceptance']
    worker = witness.worker('retention')
    original = witness.terminal(ordinary['jobId'])
    origin_original = witness.terminal(origin['jobId'])
    witness.terminal(indefinite['jobId'])
    witness.stop(worker)
    assert original['result']['outcome'] == 'Succeeded' and original['result']['payload']['value']['subtotal'] == 52
    for report in (finite_input, indefinite_input, origin_input):
        assert len(list(Path(report['entryDirectory']).glob('*.json'))) == 1
    assert original['payloadRetention']['terminalInputRetention'] == '00:00:01'
    assert original['payloadRetention']['terminalResultRetention'] == '00:00:02'

    def expired():
        # Fresh processes now configure indefinite retention. Accepted finite promises remain authoritative.
        page = None
        while True:
            cleaned = witness.call('expire-payloads', maximumJobs=1, continuationToken=page)['value']
            page = cleaned['continuationToken']
            if page is None:
                break
        state = witness.snapshot(ordinary['jobId'])
        origin_state = witness.snapshot(origin['jobId'])
        return state if all(item['inputPayloadState'] == 'Expired' and item['resultPayloadState'] == 'Expired'
                            for item in (state, origin_state)) else None
    retained = witness.until(expired, 'provider-committed input and result expiry')
    assert retained['request'] is None and retained['result'] is None and retained['progress'] is None
    assert retained['revision'] == original['revision']
    assert retained['requestSha256'] == original['requestSha256']
    assert retained['resultMetadata']['resultId'] == original['result']['resultId']
    assert retained['resultMetadata']['payloadSha256'] == original['result']['payloadSha256']
    default = witness.snapshot(indefinite['jobId'])
    assert default['inputPayloadState'] == 'Available' and default['resultPayloadState'] == 'Available'
    assert default['request'] is not None and default['result']['payload'] is not None
    witness.cases.append({'name': CASES[0], 'state': 'PASSED', 'jobId': ordinary['jobId'],
        'defaultJobId': indefinite['jobId'], 'capturedPromise': retained['payloadRetention'], 'terminalRevision': retained['revision']})

    witness.active_case = CASES[1]
    replay = witness.call('submit', clientKey='finite', input=finite_input)
    assert replay['ok'] and replay['value']['jobId'] == ordinary['jobId']
    conflict = witness.call('submit', clientKey='finite', input={**finite_input, 'name': 'changed'})
    assert not conflict['ok'] and conflict['code'] == 'jobs.submission.conflict'
    durable = witness.call('admit-origin', clientKey='origin', input=origin_input)
    assert durable['ok'] and durable['value']['status'] == 'Replayed' and durable['value']['acceptance']['jobId'] == origin['jobId']
    origin_retained = witness.snapshot(origin['jobId'])
    assert origin_retained['resultMetadata']['resultId'] == origin_original['result']['resultId']
    retry = witness.call('retry-as-new', jobId=ordinary['jobId'], expectedRevision=retained['revision'],
        clientKey='refused-expired-retry', operationId='refused-expired-retry')
    assert retry['ok'] and retry['value']['status'] == 'PayloadExpired' and retry['value']['acceptance'] is None
    witness.cases.append({'name': CASES[1], 'state': 'PASSED', 'jobId': ordinary['jobId'], 'originJobId': origin['jobId']})

    witness.active_case = CASES[2]
    attempt = witness.attempts(ordinary['jobId'])[0]['lease']
    late = witness.call('complete-stale', lease=attempt, receiptId='post-expiry-late-report')
    assert late['ok'] and late['value']['status'] == 'LeaseLost'
    inspections = witness.call('receipt-inspections', jobId=ordinary['jobId'])['value']['items']
    assert len(inspections) == 2 and all(item['receipt'] is None and item['payloadState'] == 'Expired' for item in inspections)
    assert any(item['metadata']['receiptId'] == 'post-expiry-late-report' for item in inspections)
    final = witness.snapshot(ordinary['jobId'])
    assert final['resultMetadata'] == retained['resultMetadata'] and final['revision'] == retained['revision']
    assert len(witness.attempts(ordinary['jobId'])) == 1
    witness.cases.append({'name': CASES[2], 'state': 'PASSED', 'jobId': ordinary['jobId'], 'physicalReceiptCount': 2})
    witness.active_case = None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    if not __debug__:
        parser.error('Python optimization disables retention witness assertions. Run without -O/PYTHONOPTIMIZE.')
    parser.add_argument('--entry-assembly', type=Path, required=True)
    parser.add_argument('--evidence-dir', type=Path, required=True)
    parser.add_argument('--provider', choices=('sqlite', 'postgresql'), required=True)
    args = parser.parse_args()
    entry = args.entry_assembly.resolve(strict=True)
    evidence = args.evidence_dir.resolve()
    if evidence.exists():
        raise ValueError('Use a new owned evidence directory.')
    evidence.mkdir(parents=True)
    witness = Witness(entry, evidence, args.provider)
    receipt = {'schemaVersion': 1, 'state': 'NOT RUN', 'fullJobsAcceptanceState': 'INCOMPLETE',
        'provider': args.provider, 'entryAssembly': str(entry), 'entrySha256': hashlib.sha256(entry.read_bytes()).hexdigest(),
        'controllerSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'sharedControllerSha256': hashlib.sha256(Path(__file__).with_name('run_witness.py').read_bytes()).hexdigest(),
        'plannedCaseNames': list(CASES), 'cases': []}
    def stop_requested(_signum, _frame):
        raise RuntimeError('Retention controller received a stop request; cleaning owned resources.')
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
        write_json(evidence / 'retention-receipt.json', receipt)
    return code


if __name__ == '__main__':
    sys.exit(main())

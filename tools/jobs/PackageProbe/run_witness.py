#!/usr/bin/env python3
"""Run owned, independent processes from a published Jobs PackageReference deployment.

This is a focused standalone witness, not full Jobs, model or Studio acceptance.
The PostgreSQL profile owns and removes a throwaway local container. No external
connection string, shared database or production credential is accepted.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import runpy
import re
import subprocess
import sys
import time
import uuid


M1_CASE_NAMES = (
    'durable-admission-replay-conflict',
    'missing-handler-preserves-accepted-job',
    'useful-typed-result-and-restart-inspection',
    'durable-pause-cutoff-and-accept-while-held',
    'killed-worker-recovery-stale-fence-stable-effect-key')


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def terminate_owned(process):
    if process.poll() is not None:
        return
    # Every process passed here was started below with its own session. Never
    # discover or signal a PID by port/name, and never stop an ambient host.
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        process.wait(timeout=5)
        return
    try:
        process.wait(timeout=5)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait(timeout=5)


class Witness:
    def __init__(self, entry, evidence, provider):
        self.entry, self.evidence, self.provider = entry, evidence, provider
        self.steps, self.processes = [], []
        self.cases = []
        self.active_case = None
        self.number = 0
        self.container = None
        self.container_log = {'state': 'NOT APPLICABLE'}
        self.run_id = uuid.uuid4().hex
        self.container_name = 'elsa-jobs-package-' + self.run_id
        self.container_creation_attempted = False
        self.owner = os.environ.get('ELSA_JOBS_QUALIFICATION_OWNER', 'local-elsa-3586')
        if re.fullmatch(r'[A-Za-z0-9_.-]{1,128}', self.owner) is None:
            raise ValueError('The Jobs qualification owner must be a bounded nonsecret label identity.')
        self.container_labels = {'elsa.qualification.namespace': 'background-workflows',
                                 'elsa.qualification.program': '3586',
                                 'elsa.qualification.owner': self.owner,
                                 'elsa.qualification.run': self.run_id}
        self.cgroup_parent = os.environ.get('ELSA_JOBS_QUALIFICATION_CGROUP_PARENT')
        if self.cgroup_parent is not None and re.fullmatch(r'[A-Za-z0-9_]{1,64}\.slice', self.cgroup_parent) is None:
            raise ValueError('The Jobs qualification aggregate parent slice is invalid.')
        self.container_proof = {'name': self.container_name, 'labels': self.container_labels,
                                'image': 'postgres:16-alpine', 'expectedCgroupParent': self.cgroup_parent,
                                'creationState': 'NOT RUN', 'cleanupState': 'NOT RUN'}
        self.base = {'provider': 'Sqlite' if provider == 'sqlite' else 'PostgreSql',
                     'scope': {'persistencePartition': 'package-' + self.run_id, 'tenantId': 'tenant-a'}}

    def docker(self, arguments, timeout=60):
        return subprocess.check_output(['docker', *arguments], text=True, timeout=timeout).strip()

    def write_container_proof(self):
        write_json(self.evidence / 'owned-container.json', self.container_proof)

    def verify_container_owner(self, container):
        if re.fullmatch(r'[0-9a-f]{64}', container) is None:
            raise RuntimeError('The owned Jobs container has no exact full identity.')
        actual = json.loads(self.docker(['inspect', '--format', '{{json .Config.Labels}}', container]))
        if not isinstance(actual, dict) or any(actual.get(key) != value for key, value in self.container_labels.items()):
            raise RuntimeError('The exact Jobs container does not have all original owned labels.')
        if self.docker(['inspect', '--format', '{{.Name}}', container]) != '/' + self.container_name:
            raise RuntimeError('The exact Jobs container does not have its original owned name.')

    def reconcile_container_creation(self):
        # A failed CLI reply can follow durable daemon creation. A name alone grants no deletion authority.
        identities = self.docker(['ps', '--all', '--no-trunc', '--filter', 'name=^/' + self.container_name + '$',
                                  '--format', '{{.ID}}']).splitlines()
        if len(identities) > 1 or any(re.fullmatch(r'[0-9a-f]{64}', identity) is None for identity in identities):
            raise RuntimeError('The uncertain Jobs creation has no exact daemon identity proof.')
        if not identities:
            self.container_proof.update(creationReconciliation='CONFIRMED_ABSENT', exactContainerAbsent=True)
            return
        self.verify_container_owner(identities[0])
        self.container = identities[0]
        self.container_proof.update(id=self.container, creationReconciliation='EXACT_OWNED_CONTAINER_RECOVERED')

    def container_absent(self):
        # Only a successful daemon inventory can prove absence; an inspect error cannot.
        identities = self.docker(['ps', '--all', '--no-trunc', '--format', '{{.ID}}']).splitlines()
        if any(re.fullmatch(r'[0-9a-f]{64}', identity) is None for identity in identities):
            raise RuntimeError('The Jobs daemon inventory did not return exact full identities.')
        return self.container not in identities

    def initialize_store(self):
        if self.provider == 'sqlite':
            self.base['connectionString'] = 'Data Source=' + str(self.evidence / 'jobs.db')
            self.base['schema'] = None
        else:
            name = self.container_name
            env_file = self.evidence / 'owned-postgres.env'
            password = uuid.uuid4().hex
            env_file.write_text('POSTGRES_DB=jobs\nPOSTGRES_USER=jobs\nPOSTGRES_PASSWORD=' + password + '\n')
            env_file.chmod(0o600)
            # Record the exact attempted reservation before Docker can create anything.
            labels = [argument for key, value in sorted(self.container_labels.items())
                      for argument in ('--label', key + '=' + value)]
            parent = ['--cgroup-parent', self.cgroup_parent] if self.cgroup_parent is not None else []
            self.container_creation_attempted = True
            self.container_proof.update(creationState='IN PROGRESS', creationAttempted=True)
            self.write_container_proof()
            try:
                created = self.docker(['run', '--detach', '--rm', '--name', name, *labels, *parent,
                                       '--publish', '127.0.0.1::5432', '--env-file', str(env_file),
                                       'postgres:16-alpine'])
            finally:
                env_file.unlink(missing_ok=True)
            self.verify_container_owner(created)
            self.container = created
            self.container_proof.update(id=self.container, creationState='CONFIRMED_OWNED')
            self.write_container_proof()
            if self.cgroup_parent is not None:
                pid = int(self.docker(['inspect', '--format', '{{.State.Pid}}', self.container]))
                actual = (Path('/proc') / str(pid) / 'cgroup').read_text()
                self.container_proof.update(containerPid=pid, actualCgroup=actual)
                self.write_container_proof()
                if not any(line.startswith('0::/' + self.cgroup_parent + '/') for line in actual.splitlines()):
                    raise RuntimeError('Owned PostgreSQL container is outside the admitted aggregate slice.')
            deadline = time.monotonic() + 45
            while True:
                # The official image starts a temporary socket-only server during
                # initialization. Require TCP so that server cannot report readiness.
                ready = subprocess.run(['docker', 'exec', self.container, 'pg_isready',
                                        '-h', '127.0.0.1', '-p', '5432', '-U', 'jobs', '-d', 'jobs'],
                                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=5)
                if ready.returncode == 0:
                    break
                if time.monotonic() >= deadline:
                    raise TimeoutError('Owned PostgreSQL did not become ready.')
                time.sleep(0.1)
            port = self.docker(['port', self.container, '5432/tcp']).split(':')[-1]
            self.base['connectionString'] = f'Host=127.0.0.1;Port={port};Database=jobs;Username=jobs;Password={password}'
            self.base['schema'] = 'witness_' + self.run_id
        self.call('initialize')

    def command(self, mode, **arguments):
        self.number += 1
        prefix = f'{self.number:03d}-{mode}'
        command = {**self.base, 'mode': mode, 'outputFile': str(self.evidence / (prefix + '-result.json')),
                   **arguments}
        path = self.evidence / (prefix + '-command.json')
        write_json(path, command)
        path.chmod(0o600)
        log_path = self.evidence / (prefix + '.log')
        log = log_path.open('w')
        process = subprocess.Popen(['dotnet', str(self.entry), str(path)], cwd=self.entry.parent,
                                   stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT,
                                   start_new_session=True)
        self.processes.append((process, log))
        self.steps.append({'mode': mode, 'processId': process.pid, 'log': str(log_path),
                           'commandFile': str(path), 'outputFile': command['outputFile']})
        return process, command

    def call(self, mode, **arguments):
        process, command = self.command(mode, **arguments)
        try:
            code = process.wait(timeout=45)
        except subprocess.TimeoutExpired:
            terminate_owned(process)
            raise TimeoutError('Probe operation timed out: ' + mode)
        self.completed(process)
        result_path = Path(command['outputFile'])
        result = json.loads(result_path.read_text()) if result_path.is_file() else None
        self.steps[-1]['exitCode'] = code
        if code or result is None:
            raise RuntimeError('Probe operation failed: ' + mode + '; inspect owned log and result.')
        return result

    def completed(self, process):
        if process.poll() is not None:
            for candidate, log in self.processes:
                if candidate is process:
                    log.close()
                    break

    def worker(self, label, unrelated=False, **arguments):
        ready = self.evidence / (label + '-ready.json')
        stop = self.evidence / (label + '-stop')
        process, _ = self.command('worker', readyFile=str(ready), stopFile=str(stop),
                                  unrelatedHandler=unrelated, **arguments)
        self.until(lambda: ready.is_file(), 'worker readiness', process=process)
        return process, stop

    def stop(self, worker):
        process, stop = worker
        stop.touch()
        try:
            code = process.wait(timeout=10)
            self.completed(process)
            next(item for item in self.steps if item['processId'] == process.pid)['exitCode'] = code
            if code:
                raise RuntimeError('Owned worker failed while stopping.')
        except subprocess.TimeoutExpired:
            terminate_owned(process)
            raise TimeoutError('Owned worker did not drain.')

    def until(self, predicate, label, timeout=35, process=None):
        deadline = time.monotonic() + timeout
        while True:
            value = predicate()
            if value:
                return value
            if process is not None and process.poll() is not None:
                raise RuntimeError('Owned worker exited before ' + label)
            if time.monotonic() >= deadline:
                raise TimeoutError('Did not observe ' + label)
            time.sleep(0.1)

    def snapshot(self, job_id):
        result = self.call('inspect', jobId=job_id)
        assert result['ok'] and result['value'] is not None, 'Accepted JobId disappeared.'
        return result['value']

    def terminal(self, job_id):
        def get():
            current = self.snapshot(job_id)
            return current if current['state'] == 'Terminal' else None
        return self.until(get, 'terminal result for ' + job_id)

    def attempts(self, job_id):
        result = self.call('attempts', jobId=job_id)
        assert result['ok'] and result['value']['continuationToken'] is None
        return result['value']['items']

    def report(self, label, blocked=False):
        directory = self.evidence / (label + '-entries')
        release = self.evidence / (label + '-release') if blocked else None
        return {'name': label,
                'lines': [{'product': 'Widgets', 'quantity': 3, 'unitPrice': 12.50},
                          {'product': 'Service', 'quantity': 2, 'unitPrice': 7.25}],
                'entryDirectory': str(directory), 'releaseFile': str(release) if release else None}

    def submit(self, key, report, mode='submit'):
        result = self.call(mode, clientKey=key, input=report)
        assert result['ok'], result
        return result['value']

    def check_report(self, snapshot, report):
        result = snapshot['result']
        assert result['outcome'] == 'Succeeded'
        assert result['payload']['schema'] == {'alias': 'package.report.result', 'version': 1}
        value = result['payload']['value']
        assert (value['name'], value['lines'], value['units'], value['subtotal']) == (report['name'], 2, 5, 52.0)
        assert value['invocationKey'].startswith('elsa.jobs.invocation.v1:')
        return result['resultId']

    def run(self):
        cases = self.cases
        self.initialize_store()
        provider_version = self.call('provider-version')
        assert provider_version['ok'] and provider_version['value']['version']
        self.provider_version = provider_version['value']
        self.active_case = M1_CASE_NAMES[0]
        report = self.report('invoice-total')
        assert self.submit('lost-response', report, 'submit-discard-ack')['responseDiscarded']
        accepted = self.submit('lost-response', report)
        job_id = accepted['jobId']
        assert self.submit('lost-response', report)['jobId'] == job_id
        listed = self.call('list')['value']['items']
        assert len(listed) == 1 and listed[0]['jobId'] == job_id
        conflict = self.call('submit', clientKey='lost-response', input={**report, 'name': 'different-content'})
        assert not conflict['ok'] and conflict['code'] == 'jobs.submission.conflict'
        cases.append({'name': 'durable-admission-replay-conflict', 'state': 'PASSED', 'jobId': job_id,
                      'responseLoss': 'Producer deliberately discarded the successful acceptance response.'})

        self.active_case = M1_CASE_NAMES[1]
        missing = self.worker('missing-handler', unrelated=True)
        time.sleep(0.75)  # Deliberate observation window; a positive start is separately file-barrier controlled.
        assert self.attempts(job_id) == []
        assert self.snapshot(job_id)['state'] == 'Accepted'
        self.stop(missing)
        cases.append({'name': 'missing-handler-preserves-accepted-job', 'state': 'PASSED', 'jobId': job_id})

        self.active_case = M1_CASE_NAMES[2]
        worker = self.worker('report-worker')
        complete = self.terminal(job_id)
        result_id = self.check_report(complete, report)
        assert len(self.attempts(job_id)) == 1
        self.stop(worker)
        # A fresh inspector process and a fresh producer both observe retained responsibility/result.
        assert self.check_report(self.snapshot(job_id), report) == result_id
        assert self.submit('lost-response', report)['jobId'] == job_id
        cases.append({'name': 'useful-typed-result-and-restart-inspection', 'state': 'PASSED',
                      'jobId': job_id, 'resultId': result_id, 'subtotal': 52.0})

        self.active_case = M1_CASE_NAMES[3]
        # A hold before permission prevents entry; a hold after durable permission
        # allows the already-running report to finish and preserves the next JobId.
        assert self.call('pause', holdId='before-start', operationId='pause-before')['value']['status'] == 'Applied'
        held_report = self.report('held-report', blocked=True)
        held = self.submit('held-report', held_report)['jobId']
        worker = self.worker('pause-worker')
        time.sleep(0.75)
        assert self.attempts(held) == []
        hold = next(item for item in self.call('holds')['value']['items'] if item['holdId'] == 'before-start')
        assert self.call('release', holdId=hold['holdId'], expectedRevision=hold['revision'],
                         operationId='release-before')['value']['status'] == 'Applied'
        self.until(lambda: list(Path(held_report['entryDirectory']).glob('*.json')), 'physical handler entry')
        granted = self.snapshot(held)['activeAttempt']
        assert granted is not None
        assert self.call('pause', holdId='after-start', operationId='pause-after')['value']['status'] == 'Applied'
        Path(held_report['releaseFile']).touch()
        self.check_report(self.terminal(held), held_report)
        next_report = self.report('accepted-while-paused')
        following = self.submit('accepted-while-paused', next_report)['jobId']
        time.sleep(0.75)
        assert self.attempts(following) == []
        hold = next(item for item in self.call('holds')['value']['items'] if item['holdId'] == 'after-start')
        assert self.call('release', holdId=hold['holdId'], expectedRevision=hold['revision'],
                         operationId='release-after')['value']['status'] == 'Applied'
        self.check_report(self.terminal(following), next_report)
        self.stop(worker)
        cases.append({'name': 'durable-pause-cutoff-and-accept-while-held', 'state': 'PASSED',
                      'permittedJobId': held, 'laterJobId': following, 'grantedAttemptId': granted['attemptId']})

        self.active_case = M1_CASE_NAMES[4]
        recovery_report = self.report('crash-recovery', blocked=True)
        recover = self.submit('crash-recovery', recovery_report)['jobId']
        crashed = self.worker('crashing-worker')
        entries = Path(recovery_report['entryDirectory'])
        self.until(lambda: list(entries.glob('*.json')), 'first physical entry')
        old_lease = self.snapshot(recover)['activeAttempt']
        assert old_lease is not None
        # SIGKILL deliberately bypasses drain. We only signal this Popen-created session.
        os.killpg(crashed[0].pid, signal.SIGKILL)
        crashed[0].wait(timeout=5)
        self.completed(crashed[0])
        successor = self.worker('successor-worker')
        self.until(lambda: len(list(entries.glob('*.json'))) >= 2, 'successor entry after lease expiry')
        Path(recovery_report['releaseFile']).touch()
        recovered = self.terminal(recover)
        recovered_id = self.check_report(recovered, recovery_report)
        attempts = self.attempts(recover)
        assert len(attempts) == 2 and attempts[0]['lease']['attemptId'] != attempts[1]['lease']['attemptId']
        keys = [json.loads(path.read_text())['idempotencyKey'] for path in entries.glob('*.json')]
        assert len(keys) == 2 and len(set(keys)) == 1
        stale = self.call('complete-stale', lease=old_lease, receiptId='stale-physical-receipt')
        assert stale['ok'] and stale['value']['status'] == 'LeaseLost'
        assert self.check_report(self.snapshot(recover), recovery_report) == recovered_id
        self.stop(successor)
        cases.append({'name': 'killed-worker-recovery-stale-fence-stable-effect-key', 'state': 'PASSED',
                      'jobId': recover, 'resultId': recovered_id, 'attempts': 2,
                      'oldAttemptId': old_lease['attemptId'], 'physicalEffectDeduplication': 'NOT RUN'})
        return cases

    def close(self):
        errors = []
        for process, log in reversed(self.processes):
            try:
                terminate_owned(process)
            except (OSError, subprocess.SubprocessError) as error:
                errors.append(type(error).__name__)
            finally:
                log.close()
        if self.container is None and self.container_creation_attempted:
            try:
                self.reconcile_container_creation()
            except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
                errors.append(type(error).__name__)
                self.container_proof.update(creationReconciliation='UNRESOLVED', cleanupState='FAILED')
        if self.container is not None:
            try:
                if self.container_absent():
                    self.container_proof['exactContainerAbsent'] = True
                else:
                    self.verify_container_owner(self.container)
                    path = self.evidence / 'owned-postgres.log'
                    self.container_log = {'state': 'UNAVAILABLE', 'path': str(path)}
                    try:
                        with path.open('w') as output:
                            result = subprocess.run(['docker', 'logs', self.container], stdout=output,
                                                    stderr=subprocess.STDOUT, timeout=10)
                        self.container_log.update(state='PASSED' if result.returncode == 0 else 'FAILED',
                                                  exitCode=result.returncode)
                    except (OSError, subprocess.SubprocessError) as error:
                        self.container_log['diagnostic'] = type(error).__name__
            except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
                errors.append(type(error).__name__)
            finally:
                # Log capture is useful evidence but never owns resource cleanup.
                try:
                    if not self.container_absent():
                        self.verify_container_owner(self.container)
                        self.docker(['rm', '--force', self.container])
                    if not self.container_absent():
                        raise RuntimeError('The exact owned Jobs container remains present.')
                    self.container_proof['exactContainerAbsent'] = True
                except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
                    errors.append(type(error).__name__)
        self.finish_cleanup(errors)

    def finish_cleanup(self, errors):
        if self.container_creation_attempted:
            self.container_proof.update(cleanupState='FAILED' if errors else 'PASSED', cleanupErrors=errors)
            self.write_container_proof()
        if errors:
            raise RuntimeError('Owned resource cleanup failed: ' + ', '.join(errors))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    if not __debug__:
        parser.error('Python optimization disables witness assertions. Run without -O/PYTHONOPTIMIZE.')
    parser.add_argument('--entry', required=True, type=Path)
    parser.add_argument('--evidence-dir', required=True, type=Path)
    parser.add_argument('--provider', required=True, choices=('sqlite', 'postgresql'))
    parser.add_argument('--group', choices=('m1', 'schedules', 'coordination', 'attempts', 'chaos', 'all'), default='m1')
    parser.add_argument('--negative-control', choices=('marker-only-admit', 'marker-only-claim', 'marker-only-renew', 'marker-only-complete'))
    args = parser.parse_args()
    if args.negative_control is not None and args.group not in ('chaos', 'all'):
        parser.error('The reply-loss negative control requires the chaos group.')
    entry = args.entry.resolve(strict=True)
    evidence = args.evidence_dir.resolve()
    evidence.mkdir(parents=True, exist_ok=False)
    witness = Witness(entry, evidence, args.provider)
    def stop_requested(_signum, _frame):
        raise RuntimeError('Witness controller received a stop request; cleaning owned resources.')
    signal.signal(signal.SIGTERM, stop_requested)
    receipt = {'schemaVersion': 1, 'provider': args.provider, 'entryAssembly': str(entry),
               'entrySha256': hashlib.sha256(entry.read_bytes()).hexdigest(),
               'controllerSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
               'state': 'NOT RUN', 'fullJobsAcceptanceState': 'NOT RUN', 'cases': [],
               'witnessGroup': args.group, 'negativeControl': args.negative_control}
    receipt['ownership'] = {'owner': witness.owner, 'containerLabels': witness.container_labels}
    exit_code = 1
    planned = list(M1_CASE_NAMES) if args.group in ('m1', 'all') else []
    body_failed = False
    try:
        controls = None
        if args.group != 'm1':
            controls = runpy.run_path(str(Path(__file__).with_name('m2_witness.py')))
            planned.extend(controls['planned_names'](witness, args.group, args.negative_control))
        receipt['plannedCaseNames'] = planned
        if args.group in ('m1', 'all'):
            receipt['cases'] = witness.run()
        else:
            witness.initialize_store()
            version = witness.call('provider-version')
            assert version['ok'] and version['value']['version']
            witness.provider_version = version['value']
        if args.group != 'm1':
            controls['run_group'](witness, args.group, args.negative_control)
        receipt['witnessGroup'] = args.group
        receipt['negativeControl'] = args.negative_control
        receipt['state'] = 'PASSED'
        exit_code = 0
    except (AssertionError, OSError, ValueError, KeyError, TypeError, RuntimeError, subprocess.SubprocessError) as error:
        body_failed = True
        receipt['state'] = 'FAILED'
        receipt['error'] = str(error) or type(error).__name__
        print(receipt['error'], file=sys.stderr)
    finally:
        try:
            witness.close()
            receipt['ownedResourcesCleaned'] = True
        except (OSError, RuntimeError, subprocess.SubprocessError) as error:
            receipt['ownedResourcesCleaned'] = False
            receipt['cleanupError'] = type(error).__name__
            receipt['state'] = 'FAILED'
            exit_code = 1
        receipt['processes'] = witness.steps
        actual = {item['name']: item for item in witness.cases}
        if body_failed and witness.active_case is not None and witness.active_case not in actual:
            actual[witness.active_case] = {'name': witness.active_case, 'state': 'FAILED'}
        receipt['cases'] = [actual.pop(name, {'name': name, 'state': 'NOT RUN'}) for name in planned]
        receipt['cases'].extend(actual.values())
        receipt['providerVersion'] = getattr(witness, 'provider_version', None)
        receipt['ownedContainerLog'] = witness.container_log
        write_json(evidence / 'witness-receipt.json', receipt)
    return exit_code


if __name__ == '__main__':
    sys.exit(main())

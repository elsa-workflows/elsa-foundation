"""Independent PostgreSQL process witnesses for the clean Rules package consumer."""
import json
import subprocess
import time
import uuid

from package_adversarial import ObservedProcess, verify_stale_acceptance, verify_persistence_controls


def verify_processes(receipt, evidence, source, feed, deployment, executable, run, environment):
    design_libraries = [name for name in receipt['resolvedPackages']
                        if name.startswith('Elsa.') and ('.Design' in name or '.Definitions' in name)]
    if design_libraries:
        raise ValueError('Process consumer unexpectedly resolved mutable design packages: ' + ', '.join(design_libraries))
    process_dir = evidence / 'processes'
    process_dir.mkdir()
    container = 'rules-process-' + uuid.uuid4().hex
    password = uuid.uuid4().hex + uuid.uuid4().hex
    env = environment.copy()
    env['POSTGRES_PASSWORD'] = password
    created = False
    launch_attempted = False
    proof = {'topology': 'independent-processes-on-one-machine', 'cases': [],
             'designStorage': 'absent: consumer has no design package or project reference',
             'resolvedDesignLibraries': design_libraries,
             'qualificationScope': 'Five real-worker boundary/recovery/cancellation cases; live-successor and removal-control proofs are recorded separately.',
             'removalControlsComplete': False, 'qualified': False}
    receipt['processRecovery'] = proof

    def docker(*args, check=True):
        completed = subprocess.run(['docker', *args], env=env, text=True,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        if check and completed.returncode:
            raise RuntimeError('Owned PostgreSQL container operation failed: ' +
                               completed.stdout.replace(password, '[REDACTED]')[-3000:])
        return completed

    try:
        # Bind only on loopback; the generated credential stays in the child environment.
        launch_attempted = True
        created_run = docker('run', '--detach', '--name', container, '--publish', '127.0.0.1::5432',
                             '--env', 'POSTGRES_PASSWORD', '--env', 'POSTGRES_USER=rules',
                             '--env', 'POSTGRES_DB=rules', 'postgres:16.15')
        created = True
        container_id = created_run.stdout.strip()
        proof['containerId'] = container_id
        proof['containerImage'] = docker('inspect', '--format', '{{.Image}}', container_id).stdout.strip()
        port_info = json.loads(docker('inspect', '--format', '{{json .NetworkSettings.Ports}}', container_id).stdout)
        port = int(port_info['5432/tcp'][0]['HostPort'])
        deadline = time.monotonic() + 60
        while True:
            ready = docker('exec', container_id, 'pg_isready', '-h', '127.0.0.1', '-U', 'rules', check=False)
            if ready.returncode == 0:
                break
            if time.monotonic() >= deadline:
                raise RuntimeError('Owned PostgreSQL container did not become ready.')
            time.sleep(.25)
        proof['serverVersion'] = docker('exec', container_id, 'psql', '-U', 'rules', '-d', 'rules',
                                       '-Atc', 'select version()').stdout.strip()
        def create_database():
            database = 'rules_' + uuid.uuid4().hex
            docker('exec', container_id, 'createdb', '-U', 'rules', database)
            case_env = environment.copy()
            case_env['ELSA_RULES_PROCESS_CONNECTION'] = (
                f'Host=127.0.0.1;Port={port};Database={database};Username=rules;Password={password};Pooling=False')
            return database, case_env

        for boundary, action in [('accepted', 'crash'), ('consumed', 'crash'),
                                 ('accepted', 'delayed'), ('consumed', 'delayed'),
                                 ('accepted', 'cancel')]:
            name = boundary + '-' + action
            database, case_env = create_database()
            state = process_dir / (name + '-state.json')
            result = process_dir / (name + '-origin.json')
            case = {'name': name, 'boundary': boundary, 'action': action, 'database': database,
                    'passed': False, 'statePath': str(state)}
            proof['cases'].append(case)
            original = ObservedProcess(receipt,
                ['dotnet', executable, 'process-boundary', boundary, state, result],
                deployment, case_env, process_dir / (name + '-origin.log'))
            try:
                case['boundaryState'] = original.wait_marker(state, database)
                case['originalPid'] = original.process.pid
                if action == 'crash':
                    original.terminate_at_boundary()
                successor = process_dir / (name + '-successor.json')
                mode = 'process-cancel' if action == 'cancel' else 'process-recover'
                run(['dotnet', executable, mode, state, successor], cwd=deployment, env=case_env, timeout=210)
                case['successor'] = json.loads(successor.read_text())
                if (case['successor'].get('passed') is not True
                        or case['successor']['databaseName'] != database
                        or case['successor']['databaseEngine'] != 'PostgreSQL'
                        or case['successor']['processId'] == original.process.pid):
                    raise RuntimeError('Successor lacks matching independent-process/database evidence.')
                if action != 'crash':
                    state.with_name(state.name + '.release').write_text('release\n')
                    case['originalAfterRelease'] = original.finish(result, database)
                    if any(case['originalAfterRelease'][key] != case['successor'][key]
                           for key in ('terminalBase64', 'modelRevision')):
                        raise RuntimeError('Delayed original callback changed the successor terminal result.')
                case['passed'] = True
            finally:
                original.close()
        proof['qualified'] = len(proof['cases']) == 5 and all(case['passed'] for case in proof['cases'])
        receipt['processBoundaryProofPassed'] = proof['qualified']
        verify_stale_acceptance(receipt, evidence, deployment, executable, create_database)
        verify_persistence_controls(receipt, evidence, source, feed, deployment, executable, run, create_database)
        proof['removalControlsComplete'] = True
        receipt['processProofPassed'] = True
    finally:
        if launch_attempted:
            proof['cleanupAttempted'] = True
            cleanup = docker('rm', '--force', '--volumes', container, check=False)
            proof['ownedContainerRemoved'] = cleanup.returncode == 0
            if cleanup.returncode and created:
                proof['qualified'] = False
                receipt['processBoundaryProofPassed'] = False
                receipt['processProofPassed'] = False
                raise RuntimeError('Could not remove the owned PostgreSQL evidence container.')

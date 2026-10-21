"""Independent PostgreSQL process witnesses for the clean Rules package consumer."""
import hashlib
import json
import subprocess
import time
import uuid


def verify_processes(receipt, evidence, deployment, executable, run, environment):
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
    proof = {'topology': 'independent-processes-on-one-machine', 'cases': [],
             'designStorage': 'absent: consumer has no design package or project reference',
             'resolvedDesignLibraries': design_libraries,
             'qualificationScope': 'Five real-worker boundary/recovery/cancellation cases; separate removal controls pending.',
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
        for boundary, action in [('accepted', 'crash'), ('consumed', 'crash'),
                                 ('accepted', 'delayed'), ('consumed', 'delayed'),
                                 ('accepted', 'cancel')]:
            name = boundary + '-' + action
            database = 'rules_' + uuid.uuid4().hex
            docker('exec', container_id, 'createdb', '-U', 'rules', database)
            connection = f'Host=127.0.0.1;Port={port};Database={database};Username=rules;Password={password};Pooling=False'
            case_env = environment.copy()
            case_env['ELSA_RULES_PROCESS_CONNECTION'] = connection
            state = process_dir / (name + '-state.json')
            result = process_dir / (name + '-origin.json')
            case = {'name': name, 'boundary': boundary, 'action': action, 'database': database,
                    'passed': False, 'statePath': str(state)}
            proof['cases'].append(case)
            command = ['dotnet', str(executable), 'process-boundary', boundary, str(state), str(result)]
            log = process_dir / (name + '-origin.log')
            entry = {'command': command, 'cwd': str(deployment), 'log': str(log), 'role': 'original-worker'}
            receipt['commands'].append(entry)
            with log.open('w') as output:
                child = subprocess.Popen(command, cwd=deployment, env=case_env, stdout=output,
                                         stderr=subprocess.STDOUT)
                entry['pid'] = child.pid
                try:
                    deadline = time.monotonic() + 150
                    while not state.exists():
                        if child.poll() is not None:
                            raise RuntimeError('Original process exited before its committed boundary: ' + str(log))
                        if time.monotonic() >= deadline:
                            raise RuntimeError('Original process did not reach its committed boundary: ' + str(log))
                        time.sleep(.1)
                    case['boundaryState'] = json.loads(state.read_text())
                    case['originalPid'] = child.pid
                    if case['boundaryState']['processId'] != child.pid:
                        raise RuntimeError('Boundary marker did not come from the launched process.')
                    if (case['boundaryState']['databaseName'] != database
                            or case['boundaryState']['databaseEngine'] != 'PostgreSQL'):
                        raise RuntimeError('Boundary did not use the database created for this case.')
                    if child.poll() is not None:
                        raise RuntimeError('Original process was not alive at the observed commit boundary.')
                    if action == 'crash':
                        child.kill()
                        entry['exitCode'] = child.wait(timeout=30)
                        entry['intentionalTermination'] = True
                        if entry['exitCode'] == 0:
                            raise RuntimeError('Crash witness did not terminate the live original process.')
                    successor = process_dir / (name + '-successor.json')
                    mode = 'process-cancel' if action == 'cancel' else 'process-recover'
                    run(['dotnet', executable, mode, state, successor], cwd=deployment, env=case_env, timeout=210)
                    case['successor'] = json.loads(successor.read_text())
                    if case['successor'].get('passed') is not True:
                        raise RuntimeError('Successor did not record a passing recovery result.')
                    if (case['successor']['databaseName'] != database
                            or case['successor']['databaseEngine'] != 'PostgreSQL'):
                        raise RuntimeError('Successor did not use the database created for this case.')
                    if case['successor']['processId'] == child.pid:
                        raise RuntimeError('Recovery did not execute in a different process.')
                    if action != 'crash':
                        # Returning the old response is permitted only after the successor has finished.
                        state.with_name(state.name + '.release').write_text('release\n')
                        entry['exitCode'] = child.wait(timeout=45)
                        if entry['exitCode'] != 0:
                            raise RuntimeError('Delayed original callback failed verification: ' + str(log))
                        case['originalAfterRelease'] = json.loads(result.read_text())
                        if case['originalAfterRelease'].get('passed') is not True:
                            raise RuntimeError('Original process did not confirm unchanged successor state.')
                        if (case['originalAfterRelease']['databaseName'] != database
                                or case['originalAfterRelease']['databaseEngine'] != 'PostgreSQL'):
                            raise RuntimeError('Delayed callback used a different database.')
                        if case['originalAfterRelease']['processId'] != child.pid:
                            raise RuntimeError('Delayed callback did not originate in the original process.')
                        if any(case['originalAfterRelease'][key] != case['successor'][key]
                               for key in ('terminalBase64', 'modelRevision')):
                            raise RuntimeError('Delayed original callback changed the successor terminal result.')
                    case['passed'] = True
                finally:
                    if child.poll() is None:
                        child.kill()
                        entry['cleanupExitCode'] = child.wait(timeout=30)
                    output.flush()
                    log.write_text(log.read_text().replace(connection, '[REDACTED CONNECTION]').replace(password, '[REDACTED]'))
                    entry['logSha256'] = hashlib.sha256(log.read_bytes()).hexdigest()
        proof['qualified'] = len(proof['cases']) == 5 and all(case['passed'] for case in proof['cases'])
        receipt['processBoundaryProofPassed'] = proof['qualified']
    finally:
        if created:
            cleanup = docker('rm', '--force', '--volumes', container, check=False)
            proof['ownedContainerRemoved'] = cleanup.returncode == 0
            if cleanup.returncode:
                proof['qualified'] = False
                receipt['processBoundaryProofPassed'] = False
                raise RuntimeError('Could not remove the owned PostgreSQL evidence container.')

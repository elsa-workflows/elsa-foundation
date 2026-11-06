"""Owned PostgreSQL 16 model stores; no external database or connection string input."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time
import uuid


class OwnedModelStores:
    def __init__(self, evidence, run_id):
        self.evidence, self.run_id = evidence, run_id
        self.container = None
        self.container_name = 'elsa-jobs-model-' + run_id
        self.creation_attempted = False
        self.owner = os.environ.get('ELSA_JOBS_QUALIFICATION_OWNER', 'local-elsa-3586')
        self.parent = os.environ.get('ELSA_JOBS_QUALIFICATION_CGROUP_PARENT')
        if re.fullmatch(r'[A-Za-z0-9_.-]{1,128}', self.owner) is None:
            raise ValueError('The model qualification owner must be a bounded nonsecret label.')
        if self.parent is not None and re.fullmatch(r'[A-Za-z0-9_]{1,64}\.slice', self.parent) is None:
            raise ValueError('The model qualification aggregate parent slice is invalid.')
        self.labels = {'elsa.qualification.namespace': 'background-workflows', 'elsa.qualification.program': '3586',
                       'elsa.qualification.owner': self.owner, 'elsa.qualification.run': run_id,
                       'elsa.qualification.profile': 'jobs-native-genuine-bpmn'}
        self.proof = {'state': 'NOT RUN', 'image': 'postgres:16-alpine', 'labels': self.labels,
                      'expectedCgroupParent': self.parent, 'retainedStores': {}, 'cleanupState': 'NOT RUN'}

    def docker(self, arguments, timeout=45):
        return subprocess.check_output(['docker', *arguments], text=True, stderr=subprocess.PIPE, timeout=timeout).strip()

    def write(self):
        (self.evidence / 'owned-model-stores.json').write_text(json.dumps(self.proof, indent=2) + '\n')

    def create(self):
        password = uuid.uuid4().hex
        env = self.evidence / 'owned-postgres.env'
        env.write_text('POSTGRES_DB=jobs\nPOSTGRES_USER=jobs\nPOSTGRES_INITDB_ARGS=--locale=C --encoding=UTF8\nPOSTGRES_PASSWORD=' + password + '\n')
        env.chmod(0o600)
        try:
            labels = [part for key, value in sorted(self.labels.items()) for part in ('--label', key + '=' + value)]
            parent = ['--cgroup-parent', self.parent] if self.parent else []
            # A failed name reservation never grants permission to remove an existing container.
            self.creation_attempted = True
            self.proof.update(state='CREATION_IN_PROGRESS', containerName=self.container_name)
            self.write()
            created = self.docker(['run', '--detach', '--rm', '--name', self.container_name,
                *labels, *parent, '--publish', '127.0.0.1::5432', '--env-file', str(env), 'postgres:16-alpine'])
            if re.fullmatch(r'[0-9a-f]{64}', created) is None:
                raise ValueError('Docker did not return an exact owned container identity.')
            self.container = created
            self.proof['id'] = self.container
            self.proof['actualImageId'] = self.docker(['inspect', '--format', '{{.Image}}', self.container])
            self.verify_owner()
            if self.parent:
                pid = int(self.docker(['inspect', '--format', '{{.State.Pid}}', self.container]))
                actual = (Path('/proc') / str(pid) / 'cgroup').read_text()
                self.proof.update(containerPid=pid, actualCgroup=actual)
                if not any(line.startswith('0::/' + self.parent + '/') for line in actual.splitlines()):
                    raise RuntimeError('Owned model PostgreSQL escaped the admitted aggregate slice.')
            self.write()
            deadline = time.monotonic() + 45
            while True:
                ready = subprocess.run(['docker', 'exec', self.container, 'pg_isready', '-h', '127.0.0.1',
                    '-p', '5432', '-U', 'jobs', '-d', 'jobs'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=5)
                if ready.returncode == 0:
                    break
                if time.monotonic() >= deadline:
                    raise TimeoutError('Owned PostgreSQL 16 did not become TCP ready.')
                time.sleep(0.1)
            # Separate actual databases preserve the cross-store boundary throughout all process restarts.
            self.docker(['exec', self.container, 'createdb', '-U', 'jobs', 'runtime'])
            collation = self.docker(['exec', self.container, 'psql', '-U', 'jobs', '-d', 'jobs', '--tuples-only', '--no-align',
                '--command', "SELECT datname || ':' || datcollate || ':' || datctype FROM pg_database WHERE datname IN ('jobs','runtime') ORDER BY datname"])
            if collation.splitlines() != ['jobs:C:C', 'runtime:C:C']:
                raise RuntimeError('The owned model databases do not have their exact C collation/ctype.')
            port = self.docker(['port', self.container, '5432/tcp']).rsplit(':', 1)[-1]
            if not port.isdigit():
                raise ValueError('The owned PostgreSQL loopback port is invalid.')
            prefix = f'Host=127.0.0.1;Port={port};Username=jobs;Password={password};Pooling=False;'
            self.proof.update(state='PASSED', databases=['jobs', 'runtime'], databaseCollations=collation.splitlines(), loopbackPort=int(port))
            self.write()
            return prefix + 'Database=jobs', prefix + 'Database=runtime', 'model_' + self.run_id
        finally:
            env.unlink(missing_ok=True)

    def verify_owner(self, container=None):
        container = container or self.container
        actual = json.loads(self.docker(['inspect', '--format', '{{json .Config.Labels}}', container]))
        if not isinstance(actual, dict) or any(actual.get(key) != value for key, value in self.labels.items()):
            raise RuntimeError('The exact model container no longer has its owned labels.')
        if self.docker(['inspect', '--format', '{{.Name}}', container]) != '/' + self.container_name:
            raise RuntimeError('The exact model container no longer has its owned name.')

    def reconcile_creation(self):
        # A CLI failure can follow durable daemon creation. Only a successful exact-name
        # query and full label verification can recover deletion authority; a name alone cannot.
        identities = self.docker(['ps', '--all', '--no-trunc', '--filter', 'name=^/' + self.container_name + '$',
                                  '--format', '{{.ID}}']).splitlines()
        if len(identities) > 1 or any(re.fullmatch(r'[0-9a-f]{64}', identity) is None for identity in identities):
            raise RuntimeError('The uncertain model creation has no exact daemon identity proof.')
        if not identities:
            self.proof.update(creationReconciliation='CONFIRMED_ABSENT', exactContainerAbsent=True)
            return
        self.verify_owner(identities[0])
        self.container = identities[0]
        self.proof.update(id=self.container, creationReconciliation='EXACT_OWNED_CONTAINER_RECOVERED')

    def absent(self):
        # A successful daemon query and exact full-ID absence are required; diagnostic text is not absence proof.
        return self.container not in self.docker(['ps', '--all', '--no-trunc', '--format', '{{.ID}}']).splitlines()

    def close(self):
        if self.container is None:
            if self.creation_attempted:
                try:
                    self.reconcile_creation()
                except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
                    self.proof.update(cleanupState='FAILED', creationReconciliation='UNRESOLVED',
                                      cleanupErrors=[type(error).__name__ + ': ' + str(error)])
                    self.write()
                    return False
            if self.container is None:
                self.proof['cleanupState'] = 'PASSED' if self.creation_attempted else 'NOT APPLICABLE'
                self.write()
                return True
            self.write()
        errors = []
        try:
            if self.absent():
                raise RuntimeError('The created PostgreSQL stores disappeared before their retained exports.')
            else:
                self.verify_owner()
                log = self.evidence / 'owned-postgres.log'
                with log.open('w') as output:
                    result = subprocess.run(['docker', 'logs', self.container], stdout=output, stderr=subprocess.STDOUT, timeout=10)
                self.proof['log'] = {'path': str(log), 'exitCode': result.returncode, 'sha256': hashlib.sha256(log.read_bytes()).hexdigest()}
                for database in ('jobs', 'runtime'):
                    dump = self.evidence / ('retained-' + database + '.pgdump')
                    with dump.open('wb') as output:
                        dumped = subprocess.run(['docker', 'exec', self.container, 'pg_dump', '--format=custom', '-U', 'jobs', '--dbname', database],
                            stdout=output, stderr=subprocess.PIPE, timeout=45)
                    dump.chmod(0o600)
                    if dumped.returncode != 0 or dump.stat().st_size == 0:
                        raise RuntimeError('The actual ' + database + ' store could not be retained before cleanup.')
                    self.proof['retainedStores'][database] = {'path': str(dump), 'sha256': hashlib.sha256(dump.read_bytes()).hexdigest(), 'bytes': dump.stat().st_size}
        except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
            errors.append(type(error).__name__ + ': ' + str(error))
        finally:
            try:
                if not self.absent():
                    self.verify_owner()
                    self.docker(['rm', '--force', self.container])
                if not self.absent():
                    raise RuntimeError('The exact owned PostgreSQL model container remains present.')
                self.proof['exactContainerAbsent'] = True
            except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
                errors.append(type(error).__name__ + ': ' + str(error))
            self.proof['cleanupState'] = 'PASSED' if not errors else 'FAILED'
            self.proof['cleanupErrors'] = errors
            self.write()
        return not errors

"""Live lease replacement and isolated persistence regression controls."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import time


class ObservedProcess:
    """Own a probe process and retain its outcome even when a later assertion fails."""
    def __init__(self, receipt, command, deployment, environment, log):
        self.log = log
        self.environment = environment
        self.output = log.open('w')
        self.entry = {'command': list(map(str, command)), 'cwd': str(deployment), 'log': str(log)}
        receipt['commands'].append(self.entry)
        try:
            self.process = subprocess.Popen(list(map(str, command)), cwd=deployment, env=environment,
                                            stdout=self.output, stderr=subprocess.STDOUT)
        except OSError:
            self.output.close()
            raise
        self.entry['pid'] = self.process.pid

    def wait_marker(self, path, database, timeout=150):
        deadline = time.monotonic() + timeout
        while not path.exists():
            if self.process.poll() is not None:
                raise RuntimeError('Probe exited before its marker: ' + str(self.log))
            if time.monotonic() >= deadline:
                raise RuntimeError('Probe did not publish its marker: ' + str(self.log))
            time.sleep(.1)
        marker = json.loads(path.read_text())
        if (self.process.poll() is not None or marker['processId'] != self.process.pid
                or marker['databaseName'] != database or marker['databaseEngine'] != 'PostgreSQL'):
            raise RuntimeError('Live probe marker has inconsistent process/database identity.')
        return marker

    def finish(self, result, database):
        self.entry['exitCode'] = self.process.wait(timeout=210)
        if self.entry['exitCode']:
            raise RuntimeError('Probe failed: ' + str(self.log))
        data = json.loads(result.read_text())
        if (data.get('passed') is not True or data['processId'] != self.process.pid
                or data['databaseName'] != database or data['databaseEngine'] != 'PostgreSQL'):
            raise RuntimeError('Probe result has inconsistent success/process/database evidence.')
        return data

    def terminate_at_boundary(self):
        if self.process.poll() is not None:
            raise RuntimeError('Original worker exited before intentional termination.')
        self.process.kill()
        self.entry['exitCode'] = self.process.wait(timeout=30)
        self.entry['intentionalTermination'] = True
        if self.entry['exitCode'] == 0:
            raise RuntimeError('Original worker did not terminate abnormally.')

    def close(self):
        if self.process.poll() is None:
            self.process.kill()
            self.entry['cleanupExitCode'] = self.process.wait(timeout=30)
        self.output.close()
        connection = self.environment['ELSA_RULES_PROCESS_CONNECTION']
        password = connection.split('Password=', 1)[1].split(';', 1)[0]
        self.log.write_text(self.log.read_text().replace(connection, '[REDACTED CONNECTION]')
                           .replace(password, '[REDACTED]'))
        self.entry['logSha256'] = hashlib.sha256(self.log.read_bytes()).hexdigest()


def verify_stale_acceptance(receipt, evidence, deployment, executable, create_database):
    directory = evidence / 'live-successor'
    directory.mkdir()
    database, environment = create_database()
    state, ready = directory / 'origin-state.json', directory / 'successor-ready.json'
    original_result, successor_result = directory / 'origin.json', directory / 'successor.json'
    proof = {'passed': False, 'database': database, 'topology': 'independent-processes-on-one-machine'}
    receipt['liveSuccessorProof'] = proof
    original = ObservedProcess(receipt, ['dotnet', executable, 'process-stale-origin', state, original_result],
                               deployment, environment, directory / 'origin.log')
    successor = None
    try:
        proof['originMarker'] = original.wait_marker(state, database)
        successor = ObservedProcess(receipt,
            ['dotnet', executable, 'process-stale-successor', state, ready, successor_result],
            deployment, environment, directory / 'successor.log')
        proof['successorMarker'] = successor.wait_marker(ready, database)
        origin_marker, successor_marker = proof['originMarker'], proof['successorMarker']
        if (origin_marker['invocationFingerprint'] != successor_marker['invocationFingerprint']
                or origin_marker['durableSnapshot'] != successor_marker['durableSnapshot']
                or origin_marker['tokenOwner'] == successor_marker['tokenOwner']
                or origin_marker['tokenFence'] >= successor_marker['tokenFence']
                or origin_marker['tokenExpectedModelRevision'] != successor_marker['tokenExpectedModelRevision']):
            raise RuntimeError('Successor did not replace ownership at the unchanged semantic boundary.')
        if original.process.poll() is not None or original.process.pid == successor.process.pid:
            raise RuntimeError('Both distinct workers must be live at replacement.')
        Path(str(state) + '.origin-release').write_text('release\n')
        proof['original'] = original.finish(original_result, database)
        if (proof['original']['durableSnapshotBefore'] != successor_marker['durableSnapshot']
                or proof['original']['durableSnapshotAfter'] != successor_marker['durableSnapshot']):
            raise RuntimeError('Stale submission changed the successor-held snapshot.')
        if successor.process.poll() is not None:
            raise RuntimeError('Successor must remain live while stale submission is rejected.')
        Path(str(ready) + '.successor-release').write_text('release\n')
        proof['successor'] = successor.finish(successor_result, database)
        proof['passed'] = True
    finally:
        original.close()
        if successor is not None:
            successor.close()


def verify_persistence_controls(receipt, evidence, source, feed, deployment, executable, run, create_database):
    directory = evidence / 'persistence-controls'
    directory.mkdir()
    project = source / 'Extensions/Rules/src/Persistence/EntityFrameworkCore/Elsa.Rules.Persistence.EntityFrameworkCore.csproj'
    built_asset = project.parent / 'bin/Release/net10.0/Elsa.Rules.Persistence.EntityFrameworkCore.dll'
    deployed_asset = deployment / built_asset.name
    original_asset = deployed_asset.read_bytes()
    build = ['dotnet', 'build', project, '-c', 'Release', '--no-restore', '--no-dependencies',
             '-p:CustomBeforeDirectoryBuildProps=' + str(feed.parent / 'package-versions.props')]
    controls = [
        ('cancellation', 'EfRuleRunStore.cs',
         b'\n                        || !string.Equals(row.Fingerprint64, prepared.Fingerprint, StringComparison.Ordinal)',
         b'', 'Control: changed-fingerprint cancellation did not remain Conflict without mutation.'),
        ('fence', 'EfRuleRunStore.Leases.cs', b'row.WorkFence == lease.Fence && ', b'',
         'Control: stale same-owner token was not rejected by the work fence.'),
        ('consumed', 'EfRuleRunStore.Rounds.cs', b'record.ConsumedModelRevision = row.ModelRevision;',
         b'// Consumed-marker regression control.', 'Control: exact consumption replay did not preserve its consumed revision and confirmation.')]
    results = []
    receipt['persistenceRemovalControls'] = results

    def prepare_case(name):
        database, environment = create_database()
        state = directory / (name + '-state.json')
        original = ObservedProcess(receipt,
            ['dotnet', executable, 'process-boundary', 'accepted', state, directory / (name + '-origin.json')],
            deployment, environment, directory / (name + '-origin.log'))
        try:
            marker = original.wait_marker(state, database)
            original.terminate_at_boundary()
        finally:
            original.close()
        return database, environment, state, marker

    def probe(kind, name, prepared, expected_failure=False):
        database, environment, state, marker = prepared
        output = directory / (name + '.json')
        run(['dotnet', executable, 'process-control', kind, state, output], cwd=deployment,
            env=environment, timeout=150, expect_failure=expected_failure)
        result = json.loads(output.read_text())
        if not expected_failure and (result.get('passed') is not True
                or result['databaseName'] != database or result['databaseEngine'] != 'PostgreSQL'
                or result['processId'] == marker['processId']):
            raise RuntimeError('Persistence control baseline lacks matching process/database evidence.')
        return {'database': database, 'originMarker': marker, 'result': result}

    for kind, filename, marker, replacement, failure in controls:
        implementation = project.parent / filename
        original_source = implementation.read_bytes()
        if original_source.count(marker) != 1:
            raise ValueError('Persistence control does not uniquely match reviewed source: ' + kind)
        record = {'kind': kind, 'passed': False, 'sourcePath': str(implementation),
                  'originalSourceSha256': hashlib.sha256(original_source).hexdigest(),
                  'originalAssetSha256': hashlib.sha256(original_asset).hexdigest()}
        results.append(record)
        record['baseline'] = probe(kind, kind + '-baseline', prepare_case(kind + '-baseline'))
        prepared = prepare_case(kind + '-removed')
        try:
            implementation.write_bytes(original_source.replace(marker, replacement))
            record['changedSourceSha256'] = hashlib.sha256(implementation.read_bytes()).hexdigest()
            run(build, cwd=source)
            shutil.copyfile(built_asset, deployed_asset)
            record['changedAssetSha256'] = hashlib.sha256(deployed_asset.read_bytes()).hexdigest()
            if record['changedAssetSha256'] == record['originalAssetSha256']:
                raise RuntimeError('Persistence removal control did not change the deployed assembly.')
            record['removed'] = probe(kind, kind + '-removed', prepared, expected_failure=True)
            result = record['removed']['result']
            if result.get('passed') is not False or result.get('error') != failure:
                raise RuntimeError('Persistence control failed for a different reason: ' + kind)
            record['intendedAssertionFailed'] = True
        finally:
            implementation.write_bytes(original_source)
            deployed_asset.write_bytes(original_asset)
            record['restoredSourceSha256'] = hashlib.sha256(implementation.read_bytes()).hexdigest()
            record['restoredAssetSha256'] = hashlib.sha256(deployed_asset.read_bytes()).hexdigest()
        record['restored'] = probe(kind, kind + '-restored', prepare_case(kind + '-restored'))
        record['passed'] = (record['originalSourceSha256'] == record['restoredSourceSha256']
                            and record['originalAssetSha256'] == record['restoredAssetSha256'])
    if not all(item['passed'] for item in results) or len(results) != 3:
        raise RuntimeError('Not all persistence controls passed and restored.')

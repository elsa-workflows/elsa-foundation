#!/usr/bin/env python3
"""Qualify direct/native/BPMN decision reuse and a real owned-host restart without Decisions Design services."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('owned_ux_hosts', ROOT / 'tools/ux-acceptance/run.py')
owned = importlib.util.module_from_spec(spec)
spec.loader.exec_module(owned)


def run(output):
    output.mkdir(parents=True, exist_ok=False)
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    report = {'sourceSha': sha, 'status': 'failed', 'stage': 'build', 'provider': 'Sqlite',
              'startedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}
    try:
        if subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip():
            raise ValueError('Commit source before qualification; dirty output cannot certify a revision')
        project = ROOT / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj'
        owned.command(['dotnet', 'build', str(project), '-p:RestoreLockedMode=true', '-m:1', '/nr:false', '-v:q', '-nologo'], output / 'build.log')
        with tempfile.TemporaryDirectory(prefix='elsa-decisions-execution-') as temporary:
            data = Path(temporary)
            host_directory = data / 'workbench'
            owned.prepare_host(project, host_directory)
            home = data / 'home'
            home.mkdir()
            env = dict(owned.clean_environment(), HOME=str(home), DOTNET_CLI_HOME=str(home),
                       **{'Logging__LogLevel__Microsoft.Hosting.Lifetime': 'Information'})
            url = f'http://localhost:{owned.free_port()}'
            node_env = dict(os.environ, DECISIONS_OWNED_HOSTS='1', ELSA_WORKBENCH_URL=url,
                            DECISIONS_EXECUTION_OUT=str(output), DECISIONS_SOURCE_SHA=sha)
            report['binaries'] = {file.name: hashlib.sha256(file.read_bytes()).hexdigest() for file in host_directory.glob('*.dll')
                                  if file.name.startswith(('Elsa.Decisions', 'Elsa.Foundation.Identity', 'Elsa.Workbench'))}
            node_env.update(DECISIONS_REPOSITORY=str(ROOT),
                            DECISIONS_QUALIFICATION_TEMPLATES=str(ROOT / 'specs/1195-business-decisions/evidence/identity-qualification'),
                            DECISIONS_PERMISSION_OUT=str(data / 'permissions'), DECISIONS_OWNED_STORE_ROOT=str(data),
                            DECISIONS_IAM_DATABASE=str(host_directory / 'elsa.db'), DECISIONS_HOST_DIRECTORY=str(host_directory))
            def permission(phase):
                try:
                    owned.command(['node', str(ROOT / 'tools/decisions/qualify-permissions.cjs')],
                                  output / ('permissions-' + phase + '.log'), env=dict(node_env, DECISIONS_PERMISSION_PHASE=phase))
                finally:
                    # Only public receipts leave the disposable root. Credentials and requests stay private.
                    for name in ['permissions.json', 'provisioner-build.log', 'provisioner-failure.log']:
                        receipt = data / 'permissions' / name
                        if receipt.exists(): (output / name).write_bytes(receipt.read_bytes())
            report['stage'] = 'prepare'
            with owned.host(host_directory, 'Elsa.Workbench.dll', url, '/health/ready', output / 'before-restart.log', env) as process:
                report['firstPid'] = process.pid
                permission('provision')
                owned.command(['node', str(ROOT / 'tools/decisions/qualify-execution.cjs')], output / 'prepare.log', env=dict(node_env, DECISIONS_PHASE='prepare'))
                permission('prepare')
                permission('revoke')
            witness = json.loads((output / 'execution.json').read_text())
            if witness['status'] != 'waiting-for-owned-restart':
                raise ValueError('The initial phase did not reach its persisted wait')
            # Same store and frozen executables, no Decisions authoring/API/provider services on recovery.
            shell_path = host_directory / 'shells.json'
            shells = json.loads(shell_path.read_text(encoding='utf-8-sig'))
            features = shells['CShells']['Shells']['default']['Features']
            removed = ['DecisionsApi', 'DecisionsDesign', 'DecisionsEntityFrameworkCore', 'DecisionsWorkflowsDesign']
            for name in removed:
                if name not in features:
                    raise ValueError(f'Expected normal composed feature {name}')
                del features[name]
            shell_path.write_text(json.dumps(shells))
            report['disabledForRecovery'] = removed
            report['stage'] = 'recover'
            with owned.host(host_directory, 'Elsa.Workbench.dll', url, '/health/ready', output / 'after-restart.log', env) as process:
                report['secondPid'] = process.pid
                if report['firstPid'] == process.pid:
                    raise ValueError('The process was not restarted')
                owned.command(['node', str(ROOT / 'tools/decisions/qualify-execution.cjs')], output / 'resume.log', env=dict(node_env, DECISIONS_PHASE='resume'))
                permission('verify')
            if json.loads((output / 'permissions.json').read_text())['status'] != 'passed':
                raise ValueError('Real IAM permission assertions did not pass')
            if json.loads((output / 'execution.json').read_text())['status'] != 'passed':
                raise ValueError('Restart assertions did not pass')
        if subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip() != sha or subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip():
            raise ValueError('Source changed during qualification')
        report.update(status='passed', stage='complete')
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        report['finishedAt'] = time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())
        (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
        print(json.dumps(report, indent=2), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    def terminate(_signum, _frame):
        raise InterruptedError('Interrupted; stop owned host groups')
    signal.signal(signal.SIGTERM, terminate)
    run(args.output.resolve())

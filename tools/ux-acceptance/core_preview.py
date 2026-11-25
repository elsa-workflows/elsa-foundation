#!/usr/bin/env python3
"""Run the Core Preview acceptance journeys against owned hosts: a Workbench whose default shell serves its
publications on the kernel (Elsa:CorePreview:Publications, PostgreSQL format 24 in a throwaway container), Studio, and
a local HTTP stub for the workflows' outbound requests. Every process, container and store is owned and removed."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
import http.server
import importlib.util
import json
import os
from pathlib import Path
import re
import secrets
import signal
import subprocess
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parents[2]

# Share the acceptance runner's process ownership, readiness, clean-environment and copy guards.
_spec = importlib.util.spec_from_file_location('current_acceptance_runner', ROOT / 'tools/ux-acceptance/run.py')
_base = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_base)
command, host, clean_environment, free_port, prepare_host, source_state = (
    _base.command, _base.host, _base.clean_environment, _base.free_port, _base.prepare_host, _base.source_state)

SUITE = 'core-preview'
# The kernel journeys run once, on the desktop: they qualify the kernel path, not the appearance matrix.
PROJECT = 'desktop-light'
CAPTURES = {
    'core preview stack is ready': ['reference'],
}
EVIDENCE = {
    'core preview stack is ready': ['core-preview-smoke.json'],
}
JOURNEYS = set(CAPTURES)
# The tenant the Workbench's kernel edge starts workflows in, and its reads list from.
TENANT = 'ux-core-preview'
POSTGRES_IMAGE = os.environ.get('UX_CORE_PREVIEW_POSTGRES_IMAGE', 'postgres:17')
DATABASE = 'ux_core_preview'
# The Workbench logs one of these once the default shell's publications are served on the kernel, before its node runs.
SERVING_LINE = 'serves the publications of shell default on the kernel'
SERVING_FAILURES = ('could not serve the publications of shell', 'does not compose the WorkflowsRuntimeKernel feature')


def inventory(journeys=None):
    selected = JOURNEYS if journeys is None else set(journeys)
    if not selected or not selected <= JOURNEYS:
        raise ValueError('Unknown or empty Core Preview journey inventory')
    return {(PROJECT, title) for title in selected}


def kernel_environment(connection):
    """The Core Preview's switches and its own database, for this composition's Workbench only."""
    return {'Elsa__CorePreview__Enabled': 'true', 'Elsa__CorePreview__Publications': 'true',
            'Elsa__CorePreview__Tenant': TENANT, 'ConnectionStrings__KernelCorePreview': connection}


LABEL = 'elsa.ux-acceptance=core-preview'


def sweep_stale_containers():
    """Remove containers an interrupted earlier run left behind, by this runner's label; returns their ids."""
    stale = subprocess.check_output(['docker', 'ps', '-aq', '--filter', f'label={LABEL}'], text=True).split()
    if stale:
        subprocess.run(['docker', 'rm', '-f', *stale], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return stale


@contextmanager
def postgres(log, *, attempts=240):
    """A throwaway PostgreSQL container on a loopback port, with one database for the kernel store; stopped on exit."""
    password = secrets.token_hex(24)
    # A run-unique name stops the container even when docker run itself was interrupted before it printed an id.
    name = f'elsa-ux-core-preview-{secrets.token_hex(8)}'
    try:
        # The password reaches the container through the environment only: never in argv, a failure's text or the report.
        container = subprocess.check_output(['docker', 'run', '-d', '--rm', '--name', name, '--label', LABEL,
                                             '-e', 'POSTGRES_PASSWORD', '-p', '127.0.0.1::5432', POSTGRES_IMAGE],
                                            text=True, stderr=subprocess.STDOUT, env=dict(os.environ, POSTGRES_PASSWORD=password)).strip()
        with log.open('w') as output:
            for _ in range(attempts):
                # Ready on TCP: the image's init restarts the server once, and its socket answers before that.
                ready = subprocess.run(['docker', 'exec', container, 'pg_isready', '-h', '127.0.0.1', '-U', 'postgres'],
                                       stdout=output, stderr=subprocess.STDOUT)
                if ready.returncode == 0:
                    created = subprocess.run(['docker', 'exec', container, 'createdb', '-h', '127.0.0.1', '-U', 'postgres', DATABASE],
                                             stdout=output, stderr=subprocess.STDOUT)
                    if created.returncode == 0:
                        break
                time.sleep(.25)
            else:
                raise TimeoutError(f'The owned PostgreSQL container did not accept a database; see {log}')
        mapped = subprocess.check_output(['docker', 'port', container, '5432/tcp'], text=True).strip().splitlines()[0]
        port = int(mapped.rsplit(':', 1)[1])
        yield {'container': container, 'name': name, 'image': POSTGRES_IMAGE, 'port': port, 'database': DATABASE,
               'connection': f'Host=127.0.0.1;Port={port};Username=postgres;Password={password};Database={DATABASE};'
                             'SSL Mode=Disable;GSS Encryption Mode=Disable'}
    finally:
        subprocess.run(['docker', 'stop', '--time', '3', name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def container_gone(container, *, attempts=60):
    # A stopped --rm container is removed shortly after docker stop returns.
    for _ in range(attempts):
        if subprocess.run(['docker', 'inspect', container], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode != 0:
            return True
        time.sleep(.25)
    return False


def wait_for_kernel_serving(log, process, *, timeout=180):
    """The serving line of the default shell, or the failure the Workbench logged instead of it."""
    deadline = time.monotonic() + timeout
    while True:
        text = log.read_text(errors='replace') if log.exists() else ''
        for line in text.splitlines():
            if SERVING_LINE in line:
                return line.strip()
            if any(failure in line for failure in SERVING_FAILURES):
                raise RuntimeError(f'The Workbench did not serve its publications on the kernel: {line.strip()}')
        if process.poll() is not None:
            raise RuntimeError(f'The Workbench exited before it served its publications on the kernel; see {log}')
        if time.monotonic() > deadline:
            raise TimeoutError(f'The Workbench did not report serving its publications on the kernel; see {log}')
        time.sleep(.25)


class Stub:
    """A local HTTP stub for the workflows' outbound requests: it answers every request with JSON naming what it got,
    and records each one (method, path, body) for the journeys' receipts."""

    MAX_BODY = 64 * 1024

    def __init__(self):
        self.requests = []
        self._lock = threading.Lock()
        stub = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def _answer(self):
                length = min(int(self.headers.get('Content-Length') or 0), Stub.MAX_BODY)
                body = self.rfile.read(length).decode('utf-8', errors='replace') if length else ''
                with stub._lock:
                    stub.requests.append({'method': self.command, 'path': self.path, 'body': body})
                    count = len(stub.requests)
                payload = json.dumps({'ok': True, 'method': self.command, 'path': self.path, 'body': body, 'received': count}).encode()
                self.send_response(200)
                self.send_header('Content-Type', 'application/json')
                self.send_header('Content-Length', str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)

            do_GET = do_POST = do_PUT = do_PATCH = do_DELETE = _answer

            def log_message(self, *_):
                pass

        self.server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        self.url = f'http://127.0.0.1:{self.server.server_address[1]}'
        self._thread = threading.Thread(target=self.server.serve_forever, name='ux-core-preview-stub', daemon=True)

    def __enter__(self):
        self._thread.start()
        return self

    def __exit__(self, *_):
        self.server.shutdown()
        self.server.server_close()
        self._thread.join(timeout=5)

    def recorded(self):
        with self._lock:
            return list(self.requests)


def overlay(backend, studio_url):
    """Local origin routing only, in the owned private copy: the kernel's switches come from the environment."""
    settings = json.loads((backend / 'appsettings.json').read_text(encoding='utf-8-sig'))
    settings['Cors']['AllowedOrigins'] = [studio_url]
    (backend / 'appsettings.json').write_text(json.dumps(settings))
    shells = json.loads((backend / 'shells.json').read_text(encoding='utf-8-sig'))
    shells['CShells']['Shells']['default']['Features']['FoundationIdentityAspNetCoreIdentity']['AllowedReturnUrlOrigins'] = [studio_url]
    (backend / 'shells.json').write_text(json.dumps(shells))


def verdict(report, artifacts, journeys=None):
    """An exit code alone cannot certify that every intended journey actually ran, once, with its evidence."""
    expected = inventory(journeys)
    rows = []

    def walk(suites):
        for suite in suites:
            for spec in suite.get('specs', []):
                for test in spec.get('tests', []):
                    rows.append((test.get('projectName'), spec.get('title'), test))
            walk(suite.get('suites', []))
    walk(report.get('suites', []))
    actual = [(project, title) for project, title, _ in rows]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError(f'Incomplete Core Preview inventory: expected {sorted(expected)}, got {actual}')
    if report.get('errors'):
        raise ValueError('Playwright reported runner errors')
    for project, title, test in rows:
        results = test.get('results', [])
        if test.get('expectedStatus') != 'passed' or test.get('status') != 'expected' or len(results) != 1 or results[0].get('status') != 'passed':
            raise ValueError(f'{project} / {title}: not a single successful execution')
        attachments = results[0].get('attachments', [])
        required = {'trace'} | {f'{capture}.{suffix}' for capture in CAPTURES[title] for suffix in ('png', 'aria.yml')} | set(EVIDENCE[title])
        names = {item.get('name') for item in attachments}
        if not required <= names:
            raise ValueError(f'{project} / {title}: missing artifacts {sorted(required - names)}')
        for item in attachments:
            if item.get('name') not in required:
                continue
            file = Path(item.get('path', '')).resolve()
            if not file.is_relative_to(artifacts.resolve()) or not file.is_file() or not file.stat().st_size:
                raise ValueError(f'{project} / {title}: missing or unsafe artifact {item.get("name")}')
    return len(rows)


def run(output, journey=None):
    output.mkdir(parents=True, exist_ok=False)
    sha, dirty = source_state(ROOT)
    selected = [journey] if journey else None
    report = {'sourceSha': sha, 'headSha': os.environ.get('UX_PR_HEAD_SHA', sha), 'dirty': bool(dirty), 'kind': 'composed-source',
              'suite': SUITE, 'project': PROJECT, 'journeys': sorted(title for _, title in inventory(selected)),
              'sharedHostHelperSha256': hashlib.sha256((ROOT / 'tools/ux-acceptance/run.py').read_bytes()).hexdigest(),
              'startedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), 'status': 'failed', 'stage': 'build',
              'composition': {'corePreview': {'publications': True, 'tenant': TENANT, 'format': 'PostgreSQL shipped format 24'}},
              'visualApproval': 'requires independent review', 'ownedPids': {}}
    processes = {}
    try:
        if dirty:
            raise ValueError('Commit source changes before acceptance; a dirty run cannot certify a revision')
        backend_project = ROOT / 'apps/Elsa.Workbench.Host/Elsa.Workbench.Host.csproj'
        studio_project = ROOT / 'Studio/src/apps/Elsa.Studio.Workbench.Host/Elsa.Studio.Workbench.Host.csproj'
        build = ['-p:RestoreLockedMode=true', '-v:q', '-nologo', '-m:1', '-p:BuildInParallel=false', '/nr:false']
        command(['dotnet', 'build', str(backend_project), *build], output / 'backend-build.log')
        command(['pnpm', 'build'], output / 'frontend-build.log', cwd=ROOT / 'Studio')
        command(['dotnet', 'build', str(studio_project), *build], output / 'studio-build.log')
        with tempfile.TemporaryDirectory(prefix='elsa-ux-core-preview-') as temporary:
            data = Path(temporary)
            report['ownedRoot'] = str(data)
            backend, studio = data / 'workbench', data / 'studio'
            prepare_host(backend_project, backend)
            prepare_host(studio_project, studio)
            backend_url, studio_url = f'http://localhost:{free_port()}', f'http://localhost:{free_port()}'
            overlay(backend, studio_url)
            (data / 'home').mkdir()
            env = dict(clean_environment(), DOTNET_PROCESSOR_COUNT='2', HOME=str(data / 'home'), DOTNET_CLI_HOME=str(data / 'home'),
                       **{'Logging__LogLevel__Microsoft.Hosting.Lifetime': 'Information'})
            report['stage'] = 'postgres'
            report['staleContainersRemoved'] = sweep_stale_containers()
            with postgres(output / 'postgres.log') as database, Stub() as stub:
                # The database is empty: the Workbench installs the shipped format itself before its node starts.
                report['postgres'] = {key: database[key] for key in ('container', 'image', 'database')}
                report.update(stage='host-start', backendUrl=backend_url, studioUrl=studio_url, stubUrl=stub.url)
                backend_log = output / 'backend.log'
                with host(backend, 'Elsa.Workbench.Host.dll', backend_url, '/health/ready', backend_log,
                          dict(env, **kernel_environment(database['connection']))) as backend_process:
                    processes['backend'] = backend_process
                    report['ownedPids']['backend'] = backend_process.pid
                    report['kernelServing'] = wait_for_kernel_serving(backend_log, backend_process)
                    with host(studio, 'Elsa.Studio.Workbench.Host.dll', studio_url, '/studio-runtime.js', output / 'studio.log',
                              dict(env, Studio__BackendBaseUrl=backend_url)) as studio_process:
                        processes['studio'] = studio_process
                        report['ownedPids']['studio'] = studio_process.pid
                        report['stage'] = 'journeys'
                        browser_env = dict(os.environ, UX_ACCEPTANCE_OWNED='1', UX_ACCEPTANCE_SUITE=SUITE, ELSA_STUDIO_URL=studio_url,
                                           ELSA_WORKBENCH_URL=backend_url, UX_BACKEND_URL=backend_url, UX_HTTP_STUB_URL=stub.url,
                                           UX_CORE_PREVIEW_TENANT=TENANT, UX_ACCEPTANCE_OUT=str(output), UX_SOURCE_SHA=sha)
                        browser_command = ['pnpm', 'exec', 'playwright', 'test', '--config', 'tests/acceptance/playwright.config.ts']
                        if journey:
                            browser_command.extend(['--grep', r'(?:^|\s)' + re.escape(journey) + '$'])
                        command(browser_command, output / 'journeys.log', cwd=ROOT / 'Studio', env=browser_env)
                report['stubRequests'] = stub.recorded()
            report['postgres']['containerRemoved'] = container_gone(report['postgres']['container'])
            report['executed'] = verdict(json.loads((output / 'playwright.json').read_text()), output, selected)
        report['hostExitCodes'] = {key: process.returncode for key, process in processes.items()}
        report['ownedRootRemoved'] = not data.exists()
        if (sha, dirty) != source_state(ROOT):
            raise ValueError('Source changed during acceptance; rerun the exact committed revision')
        if not report['postgres']['containerRemoved']:
            raise ValueError('The owned PostgreSQL container outlived the run')
        report.update(status='passed', stage='complete')
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        report['hostExitCodes'] = {key: process.returncode for key, process in processes.items()}
        report['finishedAt'] = time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())
        (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
        print(json.dumps(report, indent=2), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True, help='New evidence directory outside disposable stores')
    parser.add_argument('--journey', choices=sorted(JOURNEYS), help='Run one journey; the report certifies only that journey')
    args = parser.parse_args()

    def terminate(_signum, _frame):
        raise InterruptedError('Acceptance interrupted; owned hosts and the container will be stopped')
    signal.signal(signal.SIGTERM, terminate)
    run(args.output.resolve(), args.journey)

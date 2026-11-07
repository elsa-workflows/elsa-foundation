#!/usr/bin/env python3
"""Build and inspect real source hosts, owning every process and temporary store."""
from __future__ import annotations

import argparse
from contextlib import contextmanager, nullcontext
import json
import os
import re
from pathlib import Path
import shutil
import signal
import socket
import secrets
import subprocess
import tempfile
import threading
import time
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = {'desktop-light', 'desktop-dark', 'narrow-light', 'narrow-dark'}
CAPTURES = {
    'business decisions authoring and explanations': ['reference', 'decisions-list', 'decision-create', 'decision-table', 'decision-case-editor', 'decision-case-expectation', 'decision-cases', 'decision-regression', 'decision-published', 'decision-explanation', 'decision-clone', 'decision-conflict', 'decision-history', 'decisions-no-matches', 'decision-imported', 'decision-table-320', 'decision-table-320-scrolled'],
    'feature filtering and settings dialogs': ['reference', 'features-server-no-matches', 'features-server-populated', 'features-studio-no-matches', 'features-studio-populated', 'feature-settings-dialog', 'feature-impact-dialog'],
    'collections and connection creation': ['reference', 'no-matches', 'connections-list', 'secret-create', 'secret-list', 'secret-detail', 'secret-duplicate', 'invalid-name', 'completed'],
    'publication blockers explain how to correct agent settings': ['reference', 'blocked', 'instructions-focused', 'model-blocked', 'model-focused', 'completed'],
    'save template toast': ['reference', 'save-form', 'saved-toast', 'template-list', 'reopened-template'],
    'workflow template metadata defaults': ['reference', 'template-defaults', 'template-edits', 'template-catalog-entry', 'template-persisted'],
    'user tasks inbox and response': ['reference', 'tasks-empty', 'tasks-list', 'task-detail', 'task-actions', 'task-history', 'task-forms', 'task-form-versions', 'task-form-editor', 'task-form-actions', 'completed'],
    'executable designer parity': ['reference', 'definition-sequence', 'executable-sequence', 'nested-sequence', 'draft-sequence', 'empty-sequence', 'compensation', 'flowchart', 'bpmn'],
    'agent authoring and navigation': ['reference', 'agents-list', 'agent-create', 'agent-designer', 'agent-model', 'advanced-settings', 'inputs', 'outputs', 'input-general', 'input-presentation', 'input-advanced', 'output-general', 'output-presentation', 'output-advanced', 'completed'],
}
JOURNEYS = set(CAPTURES)
JOBS_CAPTURES = {
    'jobs standalone reports': ['reference', 'jobs-loading', 'jobs-empty', 'job-submit-validation', 'job-running', 'job-result', 'job-result-precision', 'jobs-filter-return', 'jobs-page'],
    'jobs overlapping holds and restart': ['reference', 'type-hold', 'unrelated-result', 'overlapping-holds', 'remaining-holds', 'after-restart'],
    'jobs native and BPMN continuation': ['reference', 'native-held-result', 'native-parent', 'bpmn-held-result', 'bpmn-parent'],
    'jobs recovery and operation audit': ['reference', 'backoff', 'failed-history', 'retry-linked', 'cancellation', 'late-receipt', 'unconfirmed-operation', 'operation-audit', 'backend-unavailable'],
    'jobs recurring schedule policies': ['reference', 'schedule-validation', 'schedule-coalescing', 'schedule-reopened', 'schedule-replay-review', 'schedule-history'],
    'jobs current permissions and tenants': ['reference', 'read-only', 'current-revoked', 'submit-only', 'audit-only', 'denied', 'foreign-tenant'],
}
FEATURE_CAPTURES = {'baseline': CAPTURES, 'jobs': JOBS_CAPTURES}


def stop_group(process):
    # The leader can exit before Chromium/workers, including a worker that ignores TERM.
    # Drain the group even after its leader exited, then kill any remaining owned members.
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    try:
        process.wait(timeout=15)
    except subprocess.TimeoutExpired:
        pass
    finally:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait(timeout=10)


def command(args, log, *, cwd=ROOT, env=None):
    with log.open('w') as output:
        process = subprocess.Popen(args, cwd=cwd, env=env, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            result = process.wait()
            if result:
                raise subprocess.CalledProcessError(result, args)
        finally:
            stop_group(process)


def selected_journeys(feature, journeys=None):
    if feature not in FEATURE_CAPTURES:
        raise ValueError('Unknown feature inventory')
    available = set(FEATURE_CAPTURES[feature])
    if journeys is None:
        return available
    if feature != 'baseline':
        raise ValueError('Jobs requires its complete mandatory journey inventory')
    selected = set(journeys)
    if not selected or not selected <= available:
        raise ValueError('Unknown or empty journey inventory')
    return selected


def inventory(report, feature='baseline', journeys=None):
    rows = []
    def walk(suites):
        for suite in suites:
            for spec in suite.get('specs', []):
                for test in spec.get('tests', []):
                    rows.append((test.get('projectName'), spec.get('title'), test))
            walk(suite.get('suites', []))
    walk(report.get('suites', []))
    expected = {(project, journey) for project in PROJECTS for journey in selected_journeys(feature, journeys)}
    actual = [(project, title) for project, title, _ in rows]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError(f'Incomplete acceptance inventory: expected {sorted(expected)}, got {actual}')
    if report.get('errors'):
        raise ValueError('Playwright reported runner errors')
    return rows


def verdict(report, artifacts, feature='baseline', source_sha=None, journeys=None):
    """An exit code alone cannot certify that every intended journey actually ran."""
    # Preserve the existing baseline tooling's positional journey selection alongside
    # the Jobs feature selector; Jobs always requires all six journeys in all projects.
    if not isinstance(feature, str):
        if journeys is not None:
            raise ValueError('Journey selection was supplied twice')
        journeys, feature = feature, 'baseline'
    rows = inventory(report, feature, journeys)
    captures = FEATURE_CAPTURES[feature]
    for project, title, test in rows:
        results = test.get('results', [])
        if test.get('expectedStatus') != 'passed' or test.get('status') != 'expected' or len(results) != 1 or results[0].get('status') != 'passed':
            raise ValueError(f'{project} / {title}: not a single successful execution')
        attachments = results[0].get('attachments', [])
        names = {item.get('name') for item in attachments}
        required = {'trace'} | ({'source', 'console.json', 'network.json'} if feature == 'jobs' else set()) | {f'{capture}.{suffix}' for capture in captures[title] for suffix in ('png', 'aria.yml')}
        if feature == 'jobs' and title == 'jobs native and BPMN continuation':
            required.add('authored-native-genuine-bpmn-jobs.json')
        if feature == 'jobs' and title == 'jobs standalone reports':
            required.add('report-export.json')
        if not required <= names:
            raise ValueError(f'{project} / {title}: missing artifacts {sorted(required - names)}')
        for item in attachments:
            if not item.get('path') and item.get('name') not in required:
                continue
            file = Path(item.get('path', '')).resolve()
            if not file.is_relative_to(artifacts.resolve()) or not file.is_file() or not file.stat().st_size:
                raise ValueError(f'{project} / {title}: missing or unsafe artifact {item.get("name")}')
            if feature == 'jobs' and item.get('name') == 'source' and source_sha is not None and file.read_text().strip() != source_sha:
                raise ValueError(f'{project} / {title}: source attachment does not identify the committed run')
            if feature == 'jobs' and item.get('name') == 'report-export.json':
                total = json.loads(file.read_text()).get('payload', {}).get('value', {}).get('total')
                if type(total) is not int or total != 9007199254740993:
                    raise ValueError(f'{project} / {title}: exported retained decimal changed value')
            if feature == 'jobs' and item.get('name') == 'authored-native-genuine-bpmn-jobs.json':
                model = json.loads(file.read_text())
                if model.get('state') != 'PASSED' or model.get('failure') or model.get('cleanupErrors') or len(model.get('cases', [])) != 2 or {(row.get('kind'), row.get('state')) for row in model.get('cases', [])} != {('native', 'PASSED'), ('bpmn', 'PASSED')}:
                    raise ValueError(f'{project} / {title}: model proof contains failed, omitted or not-run cases')
    return len(rows)


def clean_environment():
    # Hosts read environment variables after shells.json. Never inherit a developer's stores/provider secrets.
    allowed = {'PATH', 'HOME', 'USERPROFILE', 'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'TMPDIR', 'TEMP', 'TMP',
               'DOTNET_CLI_HOME', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'LANG', 'LC_ALL', 'PLAYWRIGHT_BROWSERS_PATH'}
    env = {key: value for key, value in os.environ.items() if key in allowed}
    env.update(ASPNETCORE_ENVIRONMENT='Development', DOTNET_ENVIRONMENT='Development')
    return env


def free_port():
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        return probe.getsockname()[1]


class OwnedChild:
    """One published child handle and one serialized process-group cleanup."""
    def __init__(self, process):
        self.process = process
        self.lock = threading.Lock()
        self.stopped = False
        self.error = None

    def stop(self):
        with self.lock:
            if not self.stopped:
                try:
                    stop_group(self.process)
                except BaseException as error:
                    self.error = error
                    raise
                finally:
                    self.stopped = True
            elif self.error is not None:
                raise RuntimeError('Owned child cleanup failed') from self.error


@contextmanager
def host(directory, dll, url, ready_path, log, env, *, child_started=None, startup_cancel=None):
    def check_cancelled():
        if startup_cancel is not None and startup_cancel.is_set():
            raise InterruptedError('Owned host startup was cancelled')

    # No dotnet run / launch profile: its project directory and URL could target an existing development store.
    check_cancelled()
    with log.open('w') as output:
        process = subprocess.Popen(['dotnet', str(directory / dll), '--contentRoot', str(directory), '--urls', url],
                                   cwd=directory, env=env, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        child = OwnedChild(process)
        try:
            # Publish before readiness I/O, so teardown can stop a starting child without
            # entering or waiting for this contextmanager generator.
            if child_started is not None:
                child_started(child)
            deadline = time.monotonic() + 180
            while True:
                check_cancelled()
                if process.poll() is not None:
                    raise RuntimeError(f'{dll} exited; see {log}')
                # An HTTP 200 alone can come from a listener that won the released-port race.
                # ASP.NET logs this only after it has bound every address; the process is ours and still alive.
                bound = any(line.strip() == f'Now listening on: {url}' for line in log.read_text().splitlines())
                if bound:
                    try:
                        with urllib.request.urlopen(url + ready_path, timeout=2) as response:
                            if response.status == 200 and process.poll() is None:
                                check_cancelled()
                                break
                    except (OSError, TimeoutError):
                        pass
                if time.monotonic() > deadline:
                    raise TimeoutError(f'{dll} not ready; see {log}')
                if startup_cancel is None:
                    time.sleep(.25)
                else:
                    startup_cancel.wait(.25)
            check_cancelled()
            yield process
            if process.poll() is not None:
                raise RuntimeError(f'{dll} exited during acceptance; see {log}')
        finally:
            # The group was created here and cannot be confused with an unrelated port listener.
            child.stop()


def prepare_host(project, destination):
    output = project.parent / 'bin/Debug/net10.0'
    shutil.copytree(output, destination, ignore=shutil.ignore_patterns("*.db*", "*.sqlite*", "nuplane.lock.json", ".elsa", ".nuplane", "packages", "appsettings*.json", "shells*.json"))
    # Older local outputs may contain stores. Never seed acceptance from them.
    for file in destination.rglob('*'):
        if file.is_file() and (file.suffix in {'.db', '.db-shm', '.db-wal'} or file.name == 'nuplane.lock.json'):
            file.unlink()
    for name in ['appsettings.json', 'shells.json']:
        shutil.copy2(project.parent / name, destination / name)


class RestartableHost:
    """Retains only the child handle/context created here, never discovers a process by port."""
    def __init__(self, directory, dll, url, ready_path, log, env):
        self.args = directory, dll, url, ready_path, log, env
        self.context = None
        self.generation = 0
        self.lock = threading.RLock()
        self.state_lock = threading.Lock()
        self.closing = threading.Event()
        self.child = None
        self.starting = False
        self.stopping = False

    def __enter__(self):
        with self.lock:
            self._require_open()
            if self.context is not None:
                raise RuntimeError('Owned backend is already started')
            with self.state_lock:
                self.starting = True
            context = host(*self.args, child_started=self._publish_child, startup_cancel=self.closing)
            self.context = context
            entered = False
            try:
                context.__enter__()
                entered = True
                self._require_open()
                with self.state_lock:
                    self.starting = False
                return self
            except BaseException:
                try:
                    if entered:
                        context.__exit__(None, None, None)
                finally:
                    self.context = None
                    with self.state_lock:
                        self.starting = False
                        self.child = None
                raise

    def _require_open(self):
        if self.closing.is_set():
            raise InterruptedError('Owned backend teardown has started')

    def _publish_child(self, child):
        with self.state_lock:
            self.child = child
            closing = self.closing.is_set()
        # Covers cancellation between the pre-spawn check and Popen returning.
        if closing:
            child.stop()

    def begin_shutdown(self):
        self.closing.set()
        with self.state_lock:
            child = self.child if self.starting or self.stopping else None
        # Stop first, before waiting for the lifecycle lock held by startup/readiness.
        if child is not None:
            child.stop()

    def restart(self):
        with self.lock:
            self.stop()
            return self.start()

    def stop(self):
        with self.lock:
            self._require_open()
            if self.context is None:
                raise RuntimeError('Owned backend is already stopped')
            with self.state_lock:
                self.stopping = True
            try:
                self.context.__exit__(None, None, None)
            finally:
                self.context = None
                with self.state_lock:
                    self.child = None
                    self.stopping = False
            log = self.args[4]
            self.generation += 1
            log.rename(log.with_name(f'{log.stem}-before-restart-{self.generation}{log.suffix}'))
            return self.generation

    def start(self):
        self.__enter__()
        return self.generation

    def __exit__(self, *failure):
        self.begin_shutdown()
        if not self.lock.acquire(timeout=10):
            raise RuntimeError('Owned backend startup did not drain after cancellation')
        try:
            if self.context is not None:
                try:
                    self.context.__exit__(*failure)
                finally:
                    self.context = None
                    with self.state_lock:
                        self.child = None
        finally:
            self.lock.release()


class OwnedControlServer(ThreadingHTTPServer):
    """Track every accepted handler/socket; daemon status never substitutes for a drain."""
    daemon_threads = True
    block_on_close = False

    def __init__(self, *args):
        super().__init__(*args)
        self.requests_lock = threading.Lock()
        self.requests = {}
        self.threads = set()
        self.accepting = True

    def process_request(self, request, address):
        request.settimeout(5)
        with self.requests_lock:
            if not self.accepting:
                self.shutdown_request(request)
                return
            thread = threading.Thread(target=self._request, args=(request, address), daemon=True)
            self.requests[thread] = request
            self.threads.add(thread)
            try:
                thread.start()
            except BaseException:
                self.requests.pop(thread)
                self.threads.discard(thread)
                self.shutdown_request(request)
                raise

    def _request(self, request, address):
        try:
            super().process_request_thread(request, address)
        finally:
            with self.requests_lock:
                self.requests.pop(threading.current_thread(), None)

    def close_requests(self):
        with self.requests_lock:
            self.accepting = False
            requests = list(self.requests.values())
        for request in requests:
            try:
                request.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass

    def drain(self):
        deadline = time.monotonic() + 10
        with self.requests_lock:
            threads = list(self.threads)
        for thread in threads:
            thread.join(timeout=max(0, deadline - time.monotonic()))
        if any(thread.is_alive() for thread in threads):
            raise RuntimeError('Owned restart control handlers did not drain after cancellation')
        with self.requests_lock:
            self.threads.difference_update(threads)


@contextmanager
def restart_control(backend):
    token = secrets.token_urlsafe(32)
    failures = []
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args):
            pass  # The per-run control credential is never logged or attached.
        def do_POST(self):
            if self.path not in {'/restart', '/stop', '/start'} or not secrets.compare_digest(self.headers.get('Authorization', ''), 'Bearer ' + token):
                self.send_error(403)
                return
            try:
                operation = {'/restart': backend.restart, '/stop': backend.stop, '/start': backend.start}[self.path]
                with backend.lock:
                    body = json.dumps({'generation': operation()}).encode()
                self.send_response(200)
                self.send_header('Content-Type', 'application/json')
                self.send_header('Content-Length', str(len(body)))
                self.end_headers()
                self.wfile.write(body)
            except Exception as error:
                failures.append(type(error).__name__)
                self.send_error(503, 'Owned restart failed')
    server = OwnedControlServer(('127.0.0.1', 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    started = False
    try:
        thread.start()
        started = True
        yield {'UX_RESTART_URL': f'http://127.0.0.1:{server.server_port}/restart', 'UX_RESTART_TOKEN': token}
    finally:
        server.close_requests()
        try:
            backend.begin_shutdown()
        finally:
            try:
                try:
                    if started:
                        server.shutdown()
                finally:
                    try:
                        server.server_close()
                    finally:
                        server.drain()
            finally:
                if started:
                    thread.join(timeout=10)
                if started and thread.is_alive():
                    raise RuntimeError('Owned restart controller did not stop')
    # Handler failures during teardown are as material as those before the body returned.
    if failures:
        raise RuntimeError('Owned backend restart failed: ' + ', '.join(failures))


def configure_jobs_shell(shells):
    features = shells['CShells']['Shells']['default']['Features']
    features.pop('Jobs', None)  # Fixture uses the same public runtime registration with two trusted tenants.
    features.update(JobsUxAcceptance={}, JobsEntityFrameworkCore={'Provider': 'Sqlite', 'ConnectionString': 'Data Source=jobs-ux.db;Pooling=False'},
                    JobsApi={'PersistencePartition': 'default'},
                    JobsWorkflowsDesign={}, JobsBpmn={}, ActivitiesBpmnHosting={'AdmitNewExecutions': True},
                    ActivitiesBpmnHostingApi={}, ActivitiesBpmnDirectPublishing={},
                    JobsWorkflowsRuntime={'PersistencePartition': 'default', 'TenantId': 'default'})


def run(output, feature='baseline', journey=None):
    selection = [journey] if journey else None
    journeys = selected_journeys(feature, selection)
    output.mkdir(parents=True, exist_ok=False)
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    dirty = subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()
    report = {'sourceSha': sha, 'headSha': os.environ.get('UX_PR_HEAD_SHA', sha), 'dirty': bool(dirty),
              'kind': 'composed-source', 'feature': feature, 'intendedJourneys': sorted(journeys), 'journeys': sorted(journeys), 'startedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
              'status': 'failed', 'stage': 'build', 'visualApproval': 'requires independent review'}
    try:
        if dirty:
            raise ValueError('Commit source changes before acceptance; a dirty run cannot certify a revision')
        backend_project = ROOT / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj'
        studio_project = ROOT / 'Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj'
        command(['dotnet', 'build', str(backend_project), '-p:RestoreLockedMode=true', '-v:q', '-nologo', '-m:1', '/nr:false'] + (['-p:JobsUxAcceptance=true'] if feature == 'jobs' else []), output / 'backend-build.log')
        command(['pnpm', 'build'], output / 'frontend-build.log', cwd=ROOT / 'Studio')
        command(['dotnet', 'build', str(studio_project), '-p:RestoreLockedMode=true', '-v:q', '-nologo', '-m:1', '/nr:false'], output / 'studio-build.log')
        with tempfile.TemporaryDirectory(prefix='elsa-ux-acceptance-') as temporary:
            data = Path(temporary)
            backend, studio = data / 'workbench', data / 'studio'
            prepare_host(backend_project, backend)
            prepare_host(studio_project, studio)
            backend_url = f'http://localhost:{free_port()}'
            studio_url = f'http://localhost:{free_port()}'
            # Preserve normal module composition; only local origin routing changes in private copies.
            settings = json.loads((backend / 'appsettings.json').read_text(encoding='utf-8-sig'))
            settings['Cors']['AllowedOrigins'] = [studio_url]
            # Root-owned public DP persistence; no implicit machine/user-profile key cache and
            # no key bytes in the evidence directory. The same private key DB survives restart.
            elsa = settings.setdefault('Elsa', {})
            elsa['DataProtection'] = {'ApplicationName': 'OwnedStudioUx-' + data.name,
                                      'EntityFrameworkCore': {'Enabled': True, 'Provider': 'Sqlite',
                                                              'ConnectionString': 'Data Source=' + str(data / 'identity-keys.db') + ';Pooling=False'}}
            elsa.setdefault('Persistence', {}).setdefault('EntityFramework', {})['Migrate'] = {'Policy': 'AutoMigrate'}
            (backend / 'appsettings.json').write_text(json.dumps(settings))
            shells = json.loads((backend / 'shells.json').read_text(encoding='utf-8-sig'))
            identity = shells['CShells']['Shells']['default']['Features']['FoundationIdentityAspNetCoreIdentity']
            identity['AllowedReturnUrlOrigins'] = [studio_url]
            if feature == 'jobs':
                configure_jobs_shell(shells)
            (backend / 'shells.json').write_text(json.dumps(shells))
            env = dict(clean_environment(), DOTNET_CLI_HOME=str(data / "dotnet-cli"),
                       **{"Logging__LogLevel__Microsoft.Hosting.Lifetime": "Information"})
            (data / "dotnet-cli").mkdir()
            if feature == 'jobs':
                env['UX_ACCEPTANCE_OWNED'] = '1'
            report.update(stage='host-start', studioUrl=studio_url, backendUrl=backend_url)
            with RestartableHost(backend, 'Elsa.Workbench.dll', backend_url, '/health/ready', output / 'backend.log', env) as owned_backend:
                with host(studio, 'Elsa.Studio.Web.dll', studio_url, '/studio-runtime.js', output / 'studio.log',
                          dict(env, Studio__BackendBaseUrl=backend_url)):
                    with (restart_control(owned_backend) if feature == 'jobs' else nullcontext({})) as control:
                        browser_env = dict(clean_environment(), UX_ACCEPTANCE_OWNED='1', UX_ACCEPTANCE_FEATURE=feature, ELSA_STUDIO_URL=studio_url,
                                           ELSA_WORKBENCH_URL=backend_url, UX_BACKEND_URL=backend_url,
                                           UX_ACCEPTANCE_OUT=str(output), UX_SOURCE_SHA=sha, **control)
                        browser_command = ['pnpm', 'exec', 'playwright', 'test', '--config', 'tests/acceptance/playwright.config.ts']
                        if journey:
                            browser_command.extend(['--grep', re.escape(journey).replace(r'\ ', ' ')])
                        report['stage'] = 'discovery'
                        command([*browser_command, '--list', '--reporter=json'],
                                output / 'discovery.log', cwd=ROOT / 'Studio', env=dict(browser_env, PLAYWRIGHT_JSON_OUTPUT_FILE=str(output / 'discovery.json')))
                        report['discovered'] = len(inventory(json.loads((output / 'discovery.json').read_text()), feature, selection))
                        report['stage'] = 'journeys'
                        command(browser_command,
                                output / 'journeys.log', cwd=ROOT / 'Studio', env=browser_env)
                        report['executed'] = verdict(json.loads((output / 'playwright.json').read_text()), output, feature, sha, selection)
        end_sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
        end_dirty = subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()
        if sha != end_sha or dirty != end_dirty:
            raise ValueError('Source changed during acceptance; rerun the exact committed revision')
        if dirty:
            raise ValueError('Dirty source run is preliminary evidence, not final acceptance')
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
    parser.add_argument('--output', type=Path, required=True, help='New evidence directory outside disposable stores')
    parser.add_argument('--feature', choices=sorted(FEATURE_CAPTURES), default='baseline', help='Complete mandatory journey inventory to execute')
    parser.add_argument('--journey', choices=sorted(JOURNEYS), help='Run one complete baseline journey across all four projects; Jobs always requires its complete inventory')
    args = parser.parse_args()
    if args.feature == 'jobs' and args.journey is not None:
        parser.error('Jobs requires every mandatory journey; --journey selects only baseline journeys.')
    def terminate(_signum, _frame):
        raise InterruptedError('Acceptance interrupted; owned hosts will be stopped')
    signal.signal(signal.SIGTERM, terminate)
    run(args.output.resolve(), args.feature, args.journey)

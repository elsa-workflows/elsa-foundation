#!/usr/bin/env python3
"""Build and inspect real source hosts, owning every process and temporary store."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import json
import os
import re
from pathlib import Path
import shutil
import signal
import socket
import subprocess
import tempfile
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = {'desktop-light', 'desktop-dark', 'narrow-light', 'narrow-dark'}
CAPTURES = {
    'activity definition identity survives reopening': ['reference', 'activity-created', 'activity-reloaded', 'activity-reopened'],
    'settings tabs and preserved edits': ['reference', 'modules-studio', 'modules-server', 'modules-manifest', 'modules-diagnostics', 'feeds-studio', 'feeds-server', 'features-studio', 'features-server', 'features-error', 'theme-overview', 'theme-tokens', 'theme-mode-last', 'theme-assets', 'theme-preview', 'theme-edited', 'modules-unavailable-content', 'modules-overview-content', 'modules-manifest-content', 'modules-diagnostics-content', 'feeds-operations-content', 'feeds-retention-content', 'feeds-ledger-content', 'feeds-unavailable-content', 'features-studio-list-content', 'features-studio-inspector-content', 'features-server-list-content', 'features-server-inspector-content', 'theme-overview-content', 'theme-tokens-content', 'theme-mode-last-content', 'theme-preview-content', 'theme-preview-validation'],
    'structured logs readability and recovery': ['reference', 'readability-1440', 'readability-1280', 'readability-1024', 'readability-390', 'readability-wrap', 'readability-compact', 'readability-empty', 'readability-reset', 'readability-dock'],
    'structured logs table accessibility': ['reference', 'logs-page', 'logs-selected', 'logs-empty', 'logs-dock'],
    'tabbed Secrets authoring and metadata': ['reference', 'secret-section', 'first-invalid-field', 'storage-section', 'details-section', 'persisted-list', 'persisted-metadata', 'metadata-390', 'storage-390', 'configuration-reference', 'configuration-metadata'],
    'business decisions authoring and explanations': ['reference', 'decisions-list', 'decisions-search', 'decision-create', 'decision-table', 'decision-case-editor', 'decision-case-expectation', 'decision-cases', 'decision-regression', 'decision-published', 'decision-explanation', 'decision-clone', 'decision-conflict', 'decision-history', 'decisions-no-matches', 'decision-imported', 'decision-table-320', 'decision-table-320-scrolled', 'decision-picker', 'decision-picker-new-tab', 'decision-picker-saved-tab', 'decision-picker-selected-toast', 'decision-picker-returned'],
    'feature filtering and settings dialogs': ['reference', 'features-server-no-matches', 'features-server-populated', 'features-studio-no-matches', 'features-studio-populated', 'feature-settings-dialog', 'feature-impact-dialog'],
    'collections and connection creation': ['reference', 'no-matches', 'connections-list', 'secret-create', 'secret-list', 'secret-detail', 'secret-duplicate', 'invalid-name', 'completed'],
    'publication blockers explain how to correct agent settings': ['reference', 'blocked', 'instructions-focused', 'model-blocked', 'model-focused', 'completed'],
    'save template toast': ['reference', 'save-form', 'saved-toast', 'template-list', 'reopened-template'],
    'workflow template metadata defaults': ['reference', 'template-defaults', 'template-edits', 'template-catalog-entry', 'template-persisted'],
    'user tasks inbox and response': ['reference', 'tasks-empty', 'tasks-list', 'task-detail', 'task-actions', 'task-history', 'task-forms', 'task-form-versions', 'task-form-editor', 'task-form-actions', 'completed', 'tasks-return', 'task-not-found', 'task-tab-return'],
    'executable designer parity': ['reference', 'definition-sequence', 'executable-sequence', 'nested-sequence', 'draft-sequence', 'empty-sequence', 'compensation', 'flowchart', 'bpmn'],
    'choose data for activity outputs': ['reference', 'choose-data-entry', 'chooser-open', 'output-reference', 'not-selectable', 'replace-chooser', 'chooser-320'],
    'agent authoring and navigation': ['reference', 'agents-list', 'agent-create', 'agent-designer', 'agent-model', 'advanced-settings', 'inputs', 'outputs', 'input-general', 'input-presentation', 'input-advanced', 'output-general', 'output-presentation', 'output-advanced', 'completed'],
    'user task authoring and workflow result': ['reference', 'form-authoring', 'published-form', 'task-authoring', 'form-selection', 'json-output-definitions', 'renamed-output', 'removed-output', 'output-picker', 'frozen-task-form', 'run-outcomes-initial', 'run-outcomes', 'run-outcomes-readable', 'run-output-reached', 'workflow-result', 'task-fault-causes', 'task-fault-details', 'prefill-fault'],
    'compact runtime Inputs retain exact captured values and accessible technical details': ['reference', 'compact-values', 'technical-details', 'nested-values', 'details-closed', 'technical-details-320', 'compact-values-320'],
}
JOURNEYS = set(CAPTURES)


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


def verdict(report, artifacts, journeys=None):
    """An exit code alone cannot certify that every intended journey actually ran."""
    journeys = JOURNEYS if journeys is None else set(journeys)
    if not journeys or not journeys <= JOURNEYS:
        raise ValueError("Unknown or empty journey inventory")
    rows = []
    def walk(suites):
        for suite in suites:
            for spec in suite.get('specs', []):
                for test in spec.get('tests', []):
                    rows.append((test.get('projectName'), spec.get('title'), test))
            walk(suite.get('suites', []))
    walk(report.get('suites', []))
    expected = {(project, journey) for project in PROJECTS for journey in journeys}
    actual = [(project, title) for project, title, _ in rows]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError(f'Incomplete acceptance inventory: expected {sorted(expected)}, got {actual}')
    if report.get('errors'):
        raise ValueError('Playwright reported runner errors')
    for project, title, test in rows:
        results = test.get('results', [])
        if test.get('expectedStatus') != 'passed' or test.get('status') != 'expected' or len(results) != 1 or results[0].get('status') != 'passed':
            raise ValueError(f'{project} / {title}: not a single successful execution')
        attachments = results[0].get('attachments', [])
        names = {item.get('name') for item in attachments}
        required = {'trace'} | {f'{capture}.{suffix}' for capture in CAPTURES[title] for suffix in ('png', 'aria.yml')}
        if not required <= names:
            raise ValueError(f'{project} / {title}: missing artifacts {sorted(required - names)}')
        for item in attachments:
            if not item.get('path') and item.get('name') not in required:
                continue
            file = Path(item.get('path', '')).resolve()
            if not file.is_relative_to(artifacts.resolve()) or not file.is_file() or not file.stat().st_size:
                raise ValueError(f'{project} / {title}: missing or unsafe artifact {item.get("name")}')
    return len(rows)


def clean_environment():
    # Hosts read environment variables after shells.json. Never inherit a developer's stores/provider secrets.
    allowed = {'PATH', 'HOME', 'USERPROFILE', 'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'TMPDIR', 'TEMP', 'TMP',
               'DOTNET_CLI_HOME', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'LANG', 'LC_ALL'}
    env = {key: value for key, value in os.environ.items() if key in allowed}
    env.update(ASPNETCORE_ENVIRONMENT='Development', DOTNET_ENVIRONMENT='Development')
    return env


def free_port():
    with socket.socket() as probe:
        probe.bind(('127.0.0.1', 0))
        return probe.getsockname()[1]


@contextmanager
def host(directory, dll, url, ready_path, log, env):
    # No dotnet run / launch profile: its project directory and URL could target an existing development store.
    with log.open('w') as output:
        process = subprocess.Popen(['dotnet', str(directory / dll), '--contentRoot', str(directory), '--urls', url],
                                   cwd=directory, env=env, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            deadline = time.monotonic() + 180
            while True:
                if process.poll() is not None:
                    raise RuntimeError(f'{dll} exited; see {log}')
                # An HTTP 200 alone can come from a listener that won the released-port race.
                # ASP.NET logs this only after it has bound every address; the process is ours and still alive.
                bound = any(line.strip() == f'Now listening on: {url}' for line in log.read_text().splitlines())
                if bound:
                    try:
                        with urllib.request.urlopen(url + ready_path, timeout=2) as response:
                            if response.status == 200 and process.poll() is None:
                                break
                    except (OSError, TimeoutError):
                        pass
                if time.monotonic() > deadline:
                    raise TimeoutError(f'{dll} not ready; see {log}')
                time.sleep(.25)
            yield process
        finally:
            # The group was created here and cannot be confused with an unrelated port listener.
            stop_group(process)


def prepare_host(project, destination):
    output = project.parent / 'bin/Debug/net10.0'
    shutil.copytree(output, destination, ignore=shutil.ignore_patterns("*.db*", "*.sqlite*", "nuplane.lock.json", ".elsa", ".nuplane", "packages", "appsettings*.json", "shells*.json"))
    # Older local outputs may contain stores. Never seed acceptance from them.
    for file in destination.rglob('*'):
        if file.is_file() and (file.suffix in {'.db', '.db-shm', '.db-wal'} or file.name == 'nuplane.lock.json'):
            file.unlink()
    for name in ['appsettings.json', 'shells.json']:
        shutil.copy2(project.parent / name, destination / name)


def run(output, journey=None):
    output.mkdir(parents=True, exist_ok=False)
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    dirty = subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()
    report = {'sourceSha': sha, 'headSha': os.environ.get('UX_PR_HEAD_SHA', sha), 'dirty': bool(dirty),
              'kind': 'composed-source', 'startedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
              'status': 'failed', 'stage': 'build', 'visualApproval': 'requires independent review',
              'journeys': [journey] if journey else sorted(JOURNEYS)}
    try:
        if dirty:
            raise ValueError('Commit source changes before acceptance; a dirty run cannot certify a revision')
        backend_project = ROOT / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj'
        studio_project = ROOT / 'Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj'
        command(['dotnet', 'build', str(backend_project), '-p:RestoreLockedMode=true', '-v:q', '-nologo', '-m:1', '/nr:false'], output / 'backend-build.log')
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
            (backend / 'appsettings.json').write_text(json.dumps(settings))
            shells = json.loads((backend / 'shells.json').read_text(encoding='utf-8-sig'))
            identity = shells['CShells']['Shells']['default']['Features']['FoundationIdentityAspNetCoreIdentity']
            identity['AllowedReturnUrlOrigins'] = [studio_url]
            (backend / 'shells.json').write_text(json.dumps(shells))
            env = dict(clean_environment(), HOME=str(data / "home"), DOTNET_CLI_HOME=str(data / "home"),
                       **{"Logging__LogLevel__Microsoft.Hosting.Lifetime": "Information"})
            (data / "home").mkdir()
            report.update(stage='host-start', studioUrl=studio_url, backendUrl=backend_url)
            with host(backend, 'Elsa.Workbench.dll', backend_url, '/health/ready', output / 'backend.log', env):
                with host(studio, 'Elsa.Studio.Web.dll', studio_url, '/studio-runtime.js', output / 'studio.log',
                          dict(env, Studio__BackendBaseUrl=backend_url)):
                    report['stage'] = 'journeys'
                    browser_env = dict(os.environ, UX_ACCEPTANCE_OWNED='1', ELSA_STUDIO_URL=studio_url,
                                       ELSA_WORKBENCH_URL=backend_url, UX_BACKEND_URL=backend_url,
                                       UX_ACCEPTANCE_OUT=str(output), UX_SOURCE_SHA=sha)
                    browser_command = ['pnpm', 'exec', 'playwright', 'test', '--config', 'tests/acceptance/playwright.config.ts']
                    if journey:
                        browser_command.extend(['--grep', re.escape(journey).replace(r'\ ', ' ')])
                    command(browser_command, output / 'journeys.log', cwd=ROOT / 'Studio', env=browser_env)
                    report['executed'] = verdict(json.loads((output / 'playwright.json').read_text()), output, [journey] if journey else None)
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
    parser.add_argument('--journey', choices=sorted(JOURNEYS), help='Run one complete journey across all four projects; the default still requires every journey')
    args = parser.parse_args()
    def terminate(_signum, _frame):
        raise InterruptedError('Acceptance interrupted; owned hosts will be stopped')
    signal.signal(signal.SIGTERM, terminate)
    run(args.output.resolve(), args.journey)

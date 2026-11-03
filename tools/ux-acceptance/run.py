#!/usr/bin/env python3
"""Build and inspect real source hosts, owning every process and temporary store."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import json
import os
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
JOURNEYS = {'collections and connection creation', 'agent authoring and navigation'}


def command(args, log, *, cwd=ROOT, env=None):
    with log.open('w') as output:
        subprocess.run(args, cwd=cwd, env=env, stdout=output, stderr=subprocess.STDOUT, check=True)


def verdict(report, artifacts):
    """An exit code alone cannot certify that every intended journey actually ran."""
    rows = []
    def walk(suites):
        for suite in suites:
            for spec in suite.get('specs', []):
                for test in spec.get('tests', []):
                    rows.append((test.get('projectName'), spec.get('title'), test))
            walk(suite.get('suites', []))
    walk(report.get('suites', []))
    expected = {(project, journey) for project in PROJECTS for journey in JOURNEYS}
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
        required = {'trace', 'reference.png', 'reference.aria.yml', 'completed.png', 'completed.aria.yml'}
        if not required <= names:
            raise ValueError(f'{project} / {title}: missing artifacts {sorted(required - names)}')
        for item in attachments:
            if item.get('name') not in required:
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
                try:
                    with urllib.request.urlopen(url + ready_path, timeout=2) as response:
                        if response.status == 200:
                            break
                except (OSError, TimeoutError):
                    pass
                if time.monotonic() > deadline:
                    raise TimeoutError(f'{dll} not ready; see {log}')
                time.sleep(.25)
            yield process
        finally:
            # The group was created here and cannot be confused with an unrelated port listener.
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGTERM)
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait(timeout=10)


def prepare_host(project, destination):
    output = project.parent / 'bin/Debug/net10.0'
    shutil.copytree(output, destination, ignore=shutil.ignore_patterns("*.db*", "*.sqlite*", "nuplane.lock.json", ".elsa", ".nuplane", "packages"))
    # Older local outputs may contain stores. Never seed acceptance from them.
    for file in destination.rglob('*'):
        if file.is_file() and (file.suffix in {'.db', '.db-shm', '.db-wal'} or file.name == 'nuplane.lock.json'):
            file.unlink()
    for name in ['appsettings.json', 'shells.json']:
        shutil.copy2(project.parent / name, destination / name)


def run(output):
    output.mkdir(parents=True, exist_ok=False)
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    dirty = subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()
    report = {'sourceSha': sha, 'headSha': os.environ.get('UX_PR_HEAD_SHA', sha), 'dirty': bool(dirty),
              'kind': 'composed-source', 'startedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
              'status': 'failed', 'stage': 'build', 'visualApproval': 'requires independent review'}
    try:
        backend_project = ROOT / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj'
        studio_project = ROOT / 'Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj'
        command(['dotnet', 'build', str(backend_project), '-p:RestoreLockedMode=true', '-v:q', '-nologo'], output / 'backend-build.log')
        command(['pnpm', 'build'], output / 'frontend-build.log', cwd=ROOT / 'Studio')
        command(['dotnet', 'build', str(studio_project), '-p:RestoreLockedMode=true', '-v:q', '-nologo'], output / 'studio-build.log')
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
            env = dict(clean_environment(), HOME=str(data / "home"), DOTNET_CLI_HOME=str(data / "home"))
            (data / "home").mkdir()
            report.update(stage='host-start', studioUrl=studio_url, backendUrl=backend_url)
            with host(backend, 'Elsa.Workbench.dll', backend_url, '/health/ready', output / 'backend.log', env):
                with host(studio, 'Elsa.Studio.Web.dll', studio_url, '/studio-runtime.js', output / 'studio.log',
                          dict(env, Studio__BackendBaseUrl=backend_url)):
                    report['stage'] = 'journeys'
                    browser_env = dict(os.environ, UX_ACCEPTANCE_OWNED='1', ELSA_STUDIO_URL=studio_url,
                                       UX_ACCEPTANCE_OUT=str(output), UX_SOURCE_SHA=sha)
                    command(['pnpm', 'exec', 'playwright', 'test', '--config', 'tests/acceptance/playwright.config.ts'],
                            output / 'journeys.log', cwd=ROOT / 'Studio', env=browser_env)
                    report['executed'] = verdict(json.loads((output / 'playwright.json').read_text()), output)
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
    args = parser.parse_args()
    run(args.output.resolve())

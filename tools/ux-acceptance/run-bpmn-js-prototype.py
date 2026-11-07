#!/usr/bin/env python3
"""Qualify both BPMN editor directions on owned, disposable source hosts."""
from __future__ import annotations

import argparse
import base64
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import time
from types import ModuleType

ROOT = Path(__file__).resolve().parents[2]
BACKEND_PROJECT = ROOT / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj'
STUDIO_PROJECT = ROOT / 'Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj'
PLAYWRIGHT_CONFIG = 'tests/acceptance/playwright.bpmn-js.config.ts'
DIRECTIONS = ('integrated', 'bpmn-js')
CAPTURES = (
    'alternate-initial-fit',
    'high-inline-binding-through-shared-inspector',
    'nested-sequence-through-shared-inspector',
    'alternate-shared-routing-and-attribution',
    'alternate-after-integrated-layout-edit',
    'integrated-saved-same-process',
    'alternate-after-real-save-reload',
    'actual-run-inspection-high',
    'actual-run-inspection-done',
)
BRANCHES = {'High': ('Edited high', 'high'), 'Done': ('Nested low', 'low-inline')}


def owned_helpers() -> ModuleType:
    """Reuse the standard runner's build, fresh-content-root and process-group ownership."""
    spec = importlib.util.spec_from_file_location('owned_ux_helpers', ROOT / 'tools/ux-acceptance/run.py')
    if spec is None or spec.loader is None:
        raise RuntimeError('Cannot load the standard owned-host helpers')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def utc_now() -> str:
    return time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())


def source_state() -> tuple[str, str]:
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    dirty = subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()
    return sha, dirty


def assert_source(expected_sha: str) -> None:
    sha, dirty = source_state()
    if sha != expected_sha or dirty:
        raise ValueError('Source changed during prototype proof; rerun the exact clean committed revision')


def journey_title(direction: str) -> str:
    return f'owned-host prototype from {direction}: same process edits, publication, both runtime branches and inspection'


def test_rows(suites):
    for suite in suites:
        for spec in suite.get('specs', []):
            for test in spec.get('tests', []):
                yield test.get('projectName'), spec.get('title'), test
        yield from test_rows(suite.get('suites', []))


def attachment_bytes(attachment: dict, output: Path) -> bytes:
    """Accept only this run's nonempty files or the JSON reporter's inline base64 bodies."""
    if attachment.get('path'):
        path = Path(attachment['path']).resolve()
        if not path.is_relative_to(output.resolve()) or not path.is_file() or not path.stat().st_size:
            raise ValueError(f'Missing or unsafe artifact: {attachment.get("name")}')
        return path.read_bytes()
    body = attachment.get('body')
    if not isinstance(body, str) or not body:
        raise ValueError(f'Missing artifact body: {attachment.get("name")}')
    value = base64.b64decode(body, validate=True)
    if not value:
        raise ValueError(f'Empty artifact: {attachment.get("name")}')
    return value


def validate_receipt(receipt: dict, attachments: dict, output: Path, sha: str, direction: str, *, branches=None, passed_state='passed-prototype') -> None:
    branches = BRANCHES if branches is None else branches
    if (receipt.get('source') != sha or receipt.get('firstEditor') != direction
            or receipt.get('state') != passed_state or receipt.get('browserErrors') != []):
        raise ValueError(f'{direction}: receipt does not establish a clean source-bound prototype pass')
    for key in ('definitionId', 'draftId'):
        if not isinstance(receipt.get(key), str) or not receipt[key]:
            raise ValueError(f'{direction}: receipt is missing {key}')
    publication = receipt.get('publication', {})
    for key in ('artifactId', 'sourceReferenceId', 'definitionVersionId', 'version'):
        if not isinstance(publication.get(key), str) or not publication[key]:
            raise ValueError(f'{direction}: receipt is missing publication {key}')
    runs = receipt.get('runs', [])
    if len(runs) != len(branches) or {run.get('route') for run in runs} != set(branches):
        raise ValueError(f'{direction}: both runtime branches must execute exactly once')
    execution_ids = set()
    for run in runs:
        route = run['route']
        expected, producer = branches[route]
        if (run.get('state') != 'passed' or run.get('expected') != expected
                or run.get('actual') != expected or run.get('producer') != producer):
            raise ValueError(f'{direction} / {route}: runtime result receipt is incomplete')
        for key in ('workflowExecutionId', 'activityExecutionId'):
            if not isinstance(run.get(key), str) or not run[key]:
                raise ValueError(f'{direction} / {route}: missing {key}')
        if run['workflowExecutionId'] in execution_ids:
            raise ValueError(f'{direction}: runtime branches reused one execution identity')
        execution_ids.add(run['workflowExecutionId'])
        evidence = json.loads(attachment_bytes(attachments[f'runtime-{route}.json'], output))
        instance = evidence.get('instance', {})
        identity = instance.get('instance', {})
        inspection = evidence.get('inspection', {})
        occurrences = [activity for activity in instance.get('activities', []) if activity.get('authoredActivityId') == producer]
        if (identity.get('status') not in ('Completed', 'Finished') or instance.get('incidents') != []
                or identity.get('definitionId') != receipt['definitionId']
                or identity.get('definitionVersionId') != publication['definitionVersionId']
                or identity.get('sourceReferenceId') != publication['sourceReferenceId']
                or len(occurrences) != 1 or occurrences[0].get('activityExecutionId') != run['activityExecutionId']
                or inspection.get('authoredActivityId') != producer or inspection.get('status') != 'Completed'):
            raise ValueError(f'{direction} / {route}: actual runtime inspection does not match the publication receipt')
        output_value = instance.get('outputs', {}).get('result', {})
        actual = output_value.get('value')
        if isinstance(actual, dict):
            actual = actual.get('preview') if actual.get('preview') is not None else actual.get('value')
        if output_value.get('isRedacted') is not False or actual != expected:
            raise ValueError(f'{direction} / {route}: recorded workflow output differs from the claimed bound result')


def verdict(playwright: dict, output: Path, sha: str) -> list[dict]:
    """A zero exit code and aggregate test count cannot establish the intended journeys or artifacts."""
    projects = playwright.get('config', {}).get('projects', [])
    names = [project.get('name') for project in projects]
    if not names or any(not isinstance(name, str) or not name for name in names) or len(set(names)) != len(names):
        raise ValueError('The Playwright report must identify its configured projects uniquely')
    if any(project.get('retries') != 0 or project.get('repeatEach') != 1 for project in projects):
        raise ValueError('Prototype journeys require no retries and one execution per configured project')
    expected = {(project, journey_title(direction)): direction for project in names for direction in DIRECTIONS}
    rows = list(test_rows(playwright.get('suites', [])))
    actual = [(project, title) for project, title, _ in rows]
    if len(actual) != len(expected) or set(actual) != set(expected):
        raise ValueError(f'Incomplete prototype inventory: expected {sorted(expected)}, got {actual}')
    stats = playwright.get('stats', {})
    if playwright.get('errors') or stats.get('expected') != len(expected) or any(stats.get(key) != 0 for key in ('skipped', 'unexpected', 'flaky')):
        raise ValueError('Playwright reported failed, skipped, flaky, incomplete or runner-error evidence')
    required = {'trace', 'source', 'prototype-receipt.json', 'edited-process.bpmn'}
    required |= {f'runtime-{route}.json' for route in BRANCHES}
    required |= {f'{capture}.{suffix}' for capture in CAPTURES for suffix in ('png', 'aria.yml')}
    accepted = []
    for project, title, test in rows:
        results = test.get('results', [])
        if (test.get('expectedStatus') != 'passed' or test.get('status') != 'expected' or len(results) != 1
                or results[0].get('status') != 'passed' or results[0].get('retry') != 0
                or results[0].get('error') or results[0].get('errors')):
            raise ValueError(f'{project} / {title}: not one successful execution without retries or errors')
        items = results[0].get('attachments', [])
        attachments = {item.get('name'): item for item in items}
        if len(attachments) != len(items) or not required <= attachments.keys():
            raise ValueError(f'{project} / {title}: duplicated or missing artifacts {sorted(required - attachments.keys())}')
        for item in items:
            if item.get('path') or item.get('name') in required:
                attachment_bytes(item, output)
        if attachment_bytes(attachments['source'], output).decode('utf-8') != sha:
            raise ValueError(f'{project} / {title}: source attachment does not match this checkout')
        receipt = json.loads(attachment_bytes(attachments['prototype-receipt.json'], output))
        direction = expected[(project, title)]
        validate_receipt(receipt, attachments, output, sha, direction)
        accepted.append({'project': project, 'firstEditor': direction, 'definitionId': receipt['definitionId'],
                         'draftId': receipt['draftId'], 'runtimeBranches': len(receipt['runs'])})
    html = output / 'html/index.html'
    if not html.is_file() or not html.stat().st_size:
        raise ValueError('The downloadable Playwright HTML report is missing')
    return accepted


def configure_origins(backend: Path, studio_url: str) -> None:
    settings_path = backend / 'appsettings.json'
    settings = json.loads(settings_path.read_text(encoding='utf-8-sig'))
    settings['Cors']['AllowedOrigins'] = [studio_url]
    settings_path.write_text(json.dumps(settings), encoding='utf-8')
    shells_path = backend / 'shells.json'
    shells = json.loads(shells_path.read_text(encoding='utf-8-sig'))
    identity = shells['CShells']['Shells']['default']['Features']['FoundationIdentityAspNetCoreIdentity']
    identity['AllowedReturnUrlOrigins'] = [studio_url]
    shells_path.write_text(json.dumps(shells), encoding='utf-8')


def run(output: Path, *, playwright_config=PLAYWRIGHT_CONFIG, verdict_fn=None, kind='composed-prototype') -> None:
    output = output.resolve()
    if output.is_relative_to(ROOT):
        raise ValueError('Use a new evidence directory outside the checkout')
    output.mkdir(parents=True, exist_ok=False)
    report = {'sourceSha': None, 'headSha': None, 'dirty': None,
              'kind': kind, 'status': 'failed', 'stage': 'prepare', 'startedAt': utc_now(),
              'visualApproval': 'requires independent inspection; successful journeys alone do not complete production acceptance'}
    processes = []
    try:
        sha, dirty = source_state()
        report.update(sourceSha=sha, headSha=os.environ.get('UX_PR_HEAD_SHA', sha), dirty=bool(dirty))
        if dirty:
            raise ValueError('Commit prototype source before exact-source proof')
        helpers = owned_helpers()
        report['stage'] = 'build'
        # Calling dotnet on PATH retains the shared-machine build-slot wrapper and its queue.
        helpers.command(['dotnet', 'build', str(BACKEND_PROJECT), '-p:RestoreLockedMode=true', '-v:q', '-nologo'], output / 'backend-build.log')
        helpers.command(['pnpm', 'build'], output / 'frontend-build.log', cwd=ROOT / 'Studio')
        helpers.command(['dotnet', 'build', str(STUDIO_PROJECT), '-p:RestoreLockedMode=true', '-v:q', '-nologo'], output / 'studio-build.log')
        assert_source(sha)
        with tempfile.TemporaryDirectory(prefix='elsa-3597-hosts-') as temporary:
            data = Path(temporary)
            backend, studio = data / 'workbench', data / 'studio'
            helpers.prepare_host(BACKEND_PROJECT, backend)
            helpers.prepare_host(STUDIO_PROJECT, studio)
            backend_port, studio_port = helpers.free_port(), helpers.free_port()
            while studio_port == backend_port:
                studio_port = helpers.free_port()
            backend_url, studio_url = f'http://localhost:{backend_port}', f'http://localhost:{studio_port}'
            configure_origins(backend, studio_url)
            private_home = data / 'home'
            private_home.mkdir()
            env = dict(helpers.clean_environment(), HOME=str(private_home), DOTNET_CLI_HOME=str(private_home),
                       **{'Logging__LogLevel__Microsoft.Hosting.Lifetime': 'Information'})
            report.update(stage='host-start', studioUrl=studio_url, backendUrl=backend_url)
            with helpers.host(backend, 'Elsa.Workbench.dll', backend_url, '/health/ready', output / 'backend.log', env) as backend_process:
                processes.append(('workbench', backend_process))
                with helpers.host(studio, 'Elsa.Studio.Web.dll', studio_url, '/studio-runtime.js', output / 'studio.log',
                                  dict(env, Studio__BackendBaseUrl=backend_url)) as studio_process:
                    processes.append(('studio', studio_process))
                    report['stage'] = 'journeys'
                    # Keep developer credentials/provider settings out of browser workers as well as hosts.
                    browser_env = dict(helpers.clean_environment(), UX_ACCEPTANCE_OWNED='1', ELSA_STUDIO_URL=studio_url,
                                       ELSA_WORKBENCH_URL=backend_url, UX_BACKEND_URL=backend_url,
                                       UX_ACCEPTANCE_OUT=str(output), UX_SOURCE_SHA=sha)
                    if 'PLAYWRIGHT_BROWSERS_PATH' in os.environ:
                        browser_env['PLAYWRIGHT_BROWSERS_PATH'] = os.environ['PLAYWRIGHT_BROWSERS_PATH']
                    try:
                        helpers.command(['pnpm', 'exec', 'playwright', 'test', '--config', playwright_config],
                                        output / 'journeys.log', cwd=ROOT / 'Studio', env=browser_env)
                    finally:
                        result_path = output / 'playwright.json'
                        if result_path.is_file():
                            try:
                                report['playwrightStats'] = json.loads(result_path.read_text(encoding='utf-8')).get('stats')
                            except (OSError, ValueError) as error:
                                report['playwrightReportError'] = str(error)
                    report['stage'] = 'evidence-verification'
                    assert_source(sha)
                    playwright = json.loads((output / 'playwright.json').read_text(encoding='utf-8'))
                    report['journeys'] = (verdict_fn or verdict)(playwright, output, sha)
                    report['executed'] = len(report['journeys'])
        assert_source(sha)
        report.update(stage='complete', status='passed')
    except (InterruptedError, KeyboardInterrupt) as error:
        report.update(status='cancelled', error=f'{type(error).__name__}: {error}')
        raise
    except Exception as error:
        report['error'] = f'{type(error).__name__}: {error}'
        raise
    finally:
        report['ownedHosts'] = [{'role': role, 'pid': process.pid, 'exitCode': process.poll()} for role, process in processes]
        report['finishedAt'] = utc_now()
        (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        print(json.dumps(report, indent=2), flush=True)


def terminate(signum, _frame) -> None:
    # Raise through the owned context managers. Ignore a repeated signal while they drain child groups.
    signal.signal(signum, signal.SIG_IGN)
    raise InterruptedError(f'{signal.Signals(signum).name}; owned hosts will be stopped')


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path, help='New evidence directory outside the checkout and disposable stores')
    args = parser.parse_args()
    signal.signal(signal.SIGTERM, terminate)
    signal.signal(signal.SIGINT, terminate)
    run(args.output)


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Prepare, build, or explicitly execute an owned actual-Copilot Dashboard witness.

The default only materializes a dedicated host. It does not inspect credentials,
start a host, or call a provider. The native provider-disabled runner is separate.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import zipfile
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
HOST_NAME = 'Dashboard.LiveQualification.Host'
MODEL_ENVIRONMENT = 'ELSA_DASHBOARD_LIVE_COPILOT_TOKEN'
WORKFLOW = 'live Workflow question clarification refinement and native refresh'
ACME = 'live Acme business dimension clarification refinement and viewer results'
TITLES = {WORKFLOW, ACME}
COMMON_CAPTURES = ('live-reference', 'live-clarification', 'live-proposal-preview', 'live-refined-proposal',
                   'live-refined-preview', 'live-applied-draft', 'live-saved', 'live-reopened', 'live-refreshed',
                   'live-saved-light-desktop', 'live-saved-dark-desktop', 'live-saved-light-narrow', 'live-saved-dark-narrow')
CAPTURES = {WORKFLOW: (*COMMON_CAPTURES, 'live-drill'),
            ACME: (*COMMON_CAPTURES, 'live-share', 'live-shared-reader', 'live-reader-no-category')}
CAPTURES[WORKFLOW] += ('live-question-share', 'live-dashboard-arranged', 'live-dashboard-share',
                       'live-dashboard-reader', 'live-dashboard-reader-drill')
# Every actual result gets visible periods/values; bar presentations also earn chart evidence.
for title in TITLES:
    result_captures = [f'{prefix}-{part}' for prefix in ('live-proposal-preview', 'live-refined-preview',
                       'live-saved-light-desktop', 'live-saved-dark-desktop', 'live-saved-light-narrow', 'live-saved-dark-narrow')
                       for part in ('periods', 'values', 'values-end')]
    result_captures += [f'{prefix}-chart' for prefix in ('live-refined-preview', 'live-saved-light-desktop',
                       'live-saved-dark-desktop', 'live-saved-light-narrow', 'live-saved-dark-narrow')]
    if title == ACME:
        result_captures += [f'live-shared-reader-{part}' for part in ('periods', 'chart', 'values', 'values-end')]
    CAPTURES[title] = (*CAPTURES[title], *result_captures)
JSON_ARTIFACTS = {'live-oracle.json', 'live-counters.json', 'live-readback.json'}
RESTART_TITLE = 'live authored dashboard survives real host restart with provider disabled'
RESTART_CAPTURES = ('live-reference', 'restart-dashboard-owner', 'restart-question-periods', 'restart-question-chart',
                    'restart-question-values', 'restart-question-values-end', 'restart-owner-drill',
                    'restart-dashboard-reader', 'restart-reader-drill')


class LiveUnavailable(RuntimeError):
    """Requested actual provider preflight failed; preserve unavailable and exit nonzero."""


def native_helpers():
    spec = importlib.util.spec_from_file_location('dashboard_native_acceptance', ROOT / 'tools/ux-acceptance/run.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def source_state():
    return {'sha': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
            'dirty': subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()}


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError('Reviewed host instrumentation hook changed; update the explicit template before running')
    return source.replace(old, new, 1)


def reviewed_host_template():
    """A stale host template is a failure, never a dynamically invented dependency graph."""
    template = (HERE / 'live/Host.csproj.in').read_text()
    tree = ET.fromstring(template)
    for group in tree:
        for node in group.iter('ProjectReference'):
            if node.get('Condition') or group.get('Condition'):
                raise ValueError('The dedicated host requires unconditional reviewed dependencies')
    base = ROOT / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj'
    expected = {(base.parent / item.attrib['Include'].replace('\\', '/')).resolve()
                for item in ET.parse(base).iter('ProjectReference')}
    expected.add(ROOT / 'Extensions/Examples/src/Acme.Processing/Module/Acme.Processing.csproj')
    actual = {Path(item.attrib['Include'].replace('__SOURCE_ROOT__', str(ROOT))).resolve()
              for item in tree.iter('ProjectReference')}
    if actual != expected:
        raise ValueError('Dedicated host references drifted from reviewed native composition plus Acme')
    packages = lambda root: {(item.get('Include'), item.get('PrivateAssets', '')) for item in root.iter('PackageReference')}
    if packages(tree) != packages(ET.parse(base)):
        raise ValueError('Dedicated host package declarations drifted from native host')
    return template.replace('__SOURCE_ROOT__', escape(str(ROOT)))


def materialize(output):
    """All generated host sources, locks, obj and output stay in the explicitly owned directory."""
    host = output / 'host'
    host.mkdir()
    project = host / f'{HOST_NAME}.csproj'
    project.write_text(reviewed_host_template())
    program = (ROOT / 'Essentials/apps/Elsa.Workbench/Program.cs').read_text()
    hook = 'builder.Services.AddHostedService(services => services.GetRequiredService<DashboardQualificationBootstrap>());'
    program = replace_once(program, hook, hook + '\nvar liveDashboardCounters = new DashboardLiveCounters();\nbuilder.Services.AddSingleton(liveDashboardCounters);\nvar dashboardStartClock = new DashboardQualificationStartClock();\nbuilder.Services.AddSingleton(dashboardStartClock);')
    hook = '            typeof(WeaverGitHubCopilotFeature).Assembly,'
    program = replace_once(program, hook, hook + '\n            typeof(Acme.Processing.ProcessingFeature).Assembly,\n            typeof(DashboardLiveQualificationFeature).Assembly,')
    hook = '// CShells host-control remains server-to-server management-key traffic.'
    program = replace_once(program, hook, 'DashboardLiveQualificationRoutes.Map(app, liveDashboardCounters);\nDashboardQualificationStartClock.Map(app, dashboardStartClock);\n\n' + hook)
    (host / 'Program.cs').write_text(program)
    (host / 'Directory.Build.props').write_text(f'<Project><Import Project="{escape(str(ROOT / "Directory.Build.props"))}" /></Project>\n')
    (host / 'Directory.Packages.props').write_text(f'<Project><Import Project="{escape(str(ROOT / "Directory.Packages.props"))}" /></Project>\n')
    shutil.copy2(ROOT / 'NuGet.config', host / 'NuGet.Config')
    tests = output / 'observer-tests'
    tests.mkdir()
    (tests / 'Dashboard.Live.Observer.Tests.csproj').write_text((HERE / 'live/Observer.Tests.csproj.in').read_text().replace('__SOURCE_ROOT__', escape(str(ROOT))))
    for name in ('Directory.Build.props', 'Directory.Packages.props', 'NuGet.Config'):
        shutil.copy2(host / name, tests / name)
    locks = output / 'locks'
    locks.mkdir()
    # This hook follows each project's ordinary Directory.Build.props; it does not replace domain settings.
    owned = output / 'Owned.AfterCommon.props'
    owned.write_text('<Project><PropertyGroup>\n'
                     f'  <NuGetLockFilePath>{escape(str(locks))}/$(MSBuildProjectName).packages.lock.json</NuGetLockFilePath>\n'
                     '</PropertyGroup></Project>\n')
    seeded = {}
    for lock in ROOT.rglob('packages.lock.json'):
        if any(part in {'node_modules', 'obj', 'bin', '.git'} for part in lock.relative_to(ROOT).parts):
            continue
        projects = list(lock.parent.glob('*.csproj'))
        if len(projects) != 1:
            continue
        target = locks / (projects[0].stem + '.packages.lock.json')
        if target.exists():
            raise ValueError('Project-name collision prevents isolated lock ownership')
        shutil.copy2(lock, target)
        seeded[str(target)] = sha(target)
    inputs = {'hostTemplate': sha(HERE / 'live/Host.csproj.in'),
              'observer': sha(HERE / 'live/DashboardLiveQualification.cs'),
              'startClock': sha(HERE / 'live/DashboardQualificationStartClock.cs'),
              'nativeProgram': sha(ROOT / 'Essentials/apps/Elsa.Workbench/Program.cs'),
              'generatedProgram': sha(host / 'Program.cs')}
    (output / 'source-inputs.json').write_text(json.dumps(inputs, indent=2) + '\n')
    return project, owned, seeded


def build_options(output, hook):
    return [f'-p:ArtifactsPath={output / "artifacts"}', '-p:UseArtifactsOutput=true',
            f'-p:CustomAfterMicrosoftCommonProps={hook}', '-m:2', '-nr:false', '-v:q', '-nologo']


def build(output, project, hook, seeded, native):
    options = build_options(output, hook)
    studio = ROOT / 'Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj'
    for label, target in [('backend', project), ('studio', studio)]:
        command(native, ['dotnet', 'restore', str(target), *options], output / f'{label}-restore.log', cwd=ROOT, env=native.clean_environment())
    changed = [Path(path).name for path, digest in seeded.items() if sha(Path(path)) != digest]
    if changed:
        raise ValueError('Owned restore changed an existing reviewed lock: ' + ', '.join(sorted(changed)))
    for label, target in [('backend', project), ('studio', studio)]:
        command(native, ['dotnet', 'restore', str(target), '--locked-mode', *options], output / f'{label}-locked-restore.log', cwd=ROOT, env=native.clean_environment())
    command(native, ['dotnet', 'build', str(project), '--no-restore', *options], output / 'backend-build.log', cwd=ROOT, env=native.clean_environment())
    observer_tests = output / 'observer-tests/Dashboard.Live.Observer.Tests.csproj'
    command(native, ['dotnet', 'restore', str(observer_tests), *options], output / 'observer-restore.log', cwd=ROOT, env=native.clean_environment())
    command(native, ['dotnet', 'restore', str(observer_tests), '--locked-mode', *options], output / 'observer-locked-restore.log', cwd=ROOT, env=native.clean_environment())
    command(native, ['dotnet', 'test', str(observer_tests), '--no-restore', *options], output / 'observer-tests.log', cwd=ROOT, env=native.clean_environment())
    if (ROOT / 'Studio/node_modules').is_symlink():
        raise ValueError('Live host builds require this checkout\'s own frozen frontend installation, not another checkout\'s toolchain symlink')
    command(native, ['pnpm', 'install', '--frozen-lockfile'], output / 'frontend-install.log', cwd=ROOT / 'Studio', env=native.clean_environment())
    command(native, ['pnpm', 'build'], output / 'frontend-build.log', cwd=ROOT / 'Studio', env=native.clean_environment())
    command(native, ['dotnet', 'build', str(studio), '--no-restore', *options], output / 'studio-build.log', cwd=ROOT, env=native.clean_environment())


def command(native, args, log, **options):
    receipt = log.parent / 'commands.json'
    rows = json.loads(receipt.read_text()) if receipt.is_file() else []
    row = {'arguments': [str(item) for item in args], 'log': str(log), 'status': 'running'}
    rows.append(row)
    receipt.write_text(json.dumps(rows, indent=2) + '\n')
    try:
        native.command(args, log, **options)
        row['status'] = 'passed'
    except BaseException:
        row['status'] = 'failed'
        raise
    finally:
        receipt.write_text(json.dumps(rows, indent=2) + '\n')


def credential_environment(mode, restored, name, native):
    # This is the only credential access in the runner. Preparation/build never enter it.
    if mode != 'live' or not restored:
        raise ValueError('Live execution requires explicit restored account access; no credential was inspected')
    if not re.fullmatch(r'[A-Z][A-Z0-9_]{0,127}', name or ''):
        raise ValueError('Provide the approved credential environment-variable name, never a token/file argument')
    token = os.environ.get(name)
    if not token or token.isspace():
        raise ValueError('The explicit credential environment variable is unavailable')
    return dict(native.clean_environment(), **{MODEL_ENVIRONMENT: token})


def profile(settings, shells, studio_url, model, native):
    native.configure_dashboard_qualification(settings, shells)
    settings['Cors']['AllowedOrigins'] = [studio_url]
    settings['Logging'] = {'LogLevel': {'Default': 'Warning', 'Microsoft.Hosting.Lifetime': 'Information'}}
    features = shells['CShells']['Shells']['default']['Features']
    features['FoundationIdentityAspNetCoreIdentity']['AllowedReturnUrlOrigins'] = [studio_url]
    actual = {'Enabled': True, 'UseLoggedInUser': False, 'GitHubTokenEnvironmentVariable': MODEL_ENVIRONMENT,
              'Model': model, 'BaseDirectory': '.elsa/live-copilot', 'WorkingDirectory': 'model-workspace'}
    # The derived observer feature calls the same normal provider registration. Equal options avoid order defaults.
    features['WeaverGitHubCopilot'] = dict(actual)
    features['DashboardLiveQualification'] = dict(actual)
    features['DashboardQualificationStartClock'] = {}
    features['AcmeProcessing'] = {}
    grants = settings['Qualification']['Principals'][0]['exactDirectPermissions']
    grants.extend(['acme/processing:read', 'acme/processing:create', 'acme/processing/category:read', 'acme/processing-questions:read'])
    settings['Qualification']['GrantProfiles']['dashboard-live-reader'] = list(grants)
    settings['Qualification']['GrantProfiles']['dashboard-live-no-category'] = [grant for grant in grants if grant != 'acme/processing/category:read']


def copy_runtime(built, destination):
    if not built.is_dir():
        raise ValueError('Expected owned artifacts output is missing')
    shutil.copytree(built, destination, ignore=shutil.ignore_patterns('appsettings*.json', 'shells*.json', '*.db*', '*.sqlite*', '.elsa', '.nuplane', 'packages', 'nuplane.lock.json'))


def http_json(opener, base, path, method='GET', body=None, bearer=None):
    headers = {'Content-Type': 'application/json'}
    if bearer:
        headers['Authorization'] = 'Bearer ' + bearer
    request = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
    try:
        with opener.open(request, timeout=180) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        # Do not echo arbitrary provider response bodies or request headers into evidence.
        return error.code, {}


def live_preflight(base):
    import http.cookiejar
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    status, _ = http_json(opener, base, '/_elsa/identity/login', 'POST', {'username': 'admin', 'password': 'Password123!'})
    if status != 200:
        raise ValueError('Normal owned Identity login failed before provider preflight')
    status, token = http_json(opener, base, '/_elsa/identity/token')
    bearer = token.get('accessToken') if status == 200 else None
    if not bearer:
        raise ValueError('Normal owned Identity token exchange failed before provider preflight')
    status, envelope = http_json(opener, base, '/_elsa/weaver/bootstrap', bearer=bearer)
    return classify_preflight(status, envelope)


def classify_preflight(status, envelope):
    data = envelope.get('data') or {}
    provider = data.get('provider') or {}
    available = status == 200 and data.get('enabled') is True and provider.get('isAvailable') is True
    metadata = provider.get('metadata') or {}
    return {'available': available, 'httpStatus': status, 'provider': 'github-copilot',
            'status': 'available' if available else 'unavailable',
            'sdkPackage': metadata.get('sdkPackage'), 'sdkVersion': metadata.get('sdkVersion'),
            'declaredModels': metadata.get('models', '').split(',') if available else [],
            'attempts': 1, 'automaticRetry': False}


def trace_context_evidence(path, minimum_contexts=2, owner_drill=False):
    """PW 1.61.1 globally records manual contexts; verify their actual actions, not configuration alone."""
    contexts = []
    with zipfile.ZipFile(path) as archive:
        for name in archive.namelist():
            if not name.endswith('.trace') or name == 'test.trace':
                continue
            snapshots = 0
            roles = set()
            drills = 0
            for line in archive.read(name).splitlines():
                row = json.loads(line)
                snapshots += row.get('type') == 'frame-snapshot'
                if row.get('type') != 'before':
                    continue
                parameters = row.get('params') or {}
                if row.get('method') == 'fill' and 'username' in parameters.get('selector', '').lower() and parameters.get('value') in {'admin', 'dashboard-reader'}:
                    roles.add(parameters['value'])
                if row.get('method') == 'click' and 'View matching records for result 1' in parameters.get('selector', ''):
                    drills += 1
            if snapshots:
                contexts.append({'name': name, 'roles': sorted(roles), 'snapshots': snapshots, 'drills': drills})
    if len(contexts) < minimum_contexts or not any('admin' in item['roles'] for item in contexts) or not any('dashboard-reader' in item['roles'] and item['drills'] for item in contexts):
        raise ValueError('Actual owner and independent reader actions, drill and DOM recordings are required')
    if owner_drill and not any('admin' in item['roles'] and item['drills'] for item in contexts):
        raise ValueError('The authored Workflow journey must retain its owner drill recording')
    if minimum_contexts > 1 and not any('dashboard-reader' in item['roles'] and 'admin' not in item['roles'] and item['drills'] for item in contexts):
        raise ValueError('The independent first-lifetime reader must have its own recorded context')
    return contexts


def verdict(report, output, expected_sha=None):
    rows = []
    def visit(suites):
        for suite in suites:
            for spec in suite.get('specs', []):
                rows.extend((spec.get('title'), test) for test in spec.get('tests', []))
            visit(suite.get('suites', []))
    visit(report.get('suites', []))
    if len(rows) != len(TITLES) or {title for title, _ in rows} != TITLES or report.get('errors'):
        raise ValueError('Both live lifecycle journeys must run exactly once without runner errors')
    for title, test in rows:
        required = JSON_ARTIFACTS | {'trace', 'source'} | {f'{capture}.{suffix}' for capture in CAPTURES[title] for suffix in ('png', 'aria.yml')}
        if title == ACME:
            required.add('live-authority.json')
        else:
            required |= {'live-query-refinement.json', 'live-retained-definitions.json'}
        results = test.get('results', [])
        if test.get('projectName') != 'live-desktop' or test.get('expectedStatus') != 'passed' or test.get('status') != 'expected' or len(results) != 1 or results[0].get('status') != 'passed':
            raise ValueError(title + ': missing, skipped, retried or failed live execution')
        attachments = results[0].get('attachments', [])
        if not required <= {item.get('name') for item in attachments}:
            raise ValueError(title + ': required UI, numeric, persistence or SDK evidence is missing')
        for item in attachments:
            if item.get('name') not in required:
                continue
            path = Path(item.get('path', '')).resolve()
            if not path.is_relative_to(output.resolve()) or not path.is_file() or not path.stat().st_size:
                raise ValueError(title + ': artifact is missing, empty or outside owned evidence')
            if item.get('name') == 'source' and expected_sha is not None and path.read_text().strip() != expected_sha:
                raise ValueError(title + ': browser source attachment differs from the committed runner revision')
        trace_context_evidence(next(Path(item['path']) for item in attachments if item.get('name') == 'trace'), owner_drill=title == WORKFLOW)
        boundary = next(Path(item['path']) for item in attachments if item.get('name') == 'live-counters.json')
        evidence = json.loads(boundary.read_text())
        before = evidence.get('turns', {}).get('before', {})
        clarified = evidence.get('turns', {}).get('afterClarification', {})
        refine_before = evidence.get('refinement', {}).get('before', {})
        refined = evidence.get('refinement', {}).get('after', {})
        after = evidence.get('after', {})
        count = lambda snapshot, key: snapshot.get('counts', {}).get(key, 0)
        delta = lambda end, start, key: count(end, key) - count(start, key)
        authored_sends = delta(refined, before, 'session.send')
        phases = evidence.get('nativePhases') or []
        required_phases = {'initial-native-preview', 'refined-native-preview', 'use-unsaved-draft', 'explicit-save', 'reopen-saved-definition', 'reopened-native-refresh', 'saved-native-appearances'}
        required_phases |= {'native-new-run-and-refresh', 'native-saved-drill', 'native-share', 'native-dashboard-arrange',
                            'native-dashboard-share', 'native-independent-viewer-refresh'} if title == WORKFLOW else {'native-share', 'native-independent-viewer-refresh', 'native-reauthenticated-field-revocation'}
        if title == ACME:
            authority = json.loads(next(Path(item['path']) for item in attachments if item.get('name') == 'live-authority.json').read_text())
            if authority.get('readerReauthenticated') is not True or authority.get('executeStatus') != 403:
                raise ValueError(title + ': current authenticated field-denial evidence is missing')
        else:
            refinement = json.loads(next(Path(item['path']) for item in attachments if item.get('name') == 'live-query-refinement.json').read_text())
            retained = json.loads(next(Path(item['path']) for item in attachments if item.get('name') == 'live-retained-definitions.json').read_text())
            if not all(refinement.get(key) is True for key in ('groupingChanged', 'filtersChanged', 'actualSdkRefinement')):
                raise ValueError('Actual model grouping/filter refinement is required; presentation-only changes cannot qualify')
            if not retained.get('questionId') or not retained.get('dashboardId') or len(set(retained.get('tileIds') or [])) != 2 or retained.get('deferredToOwnedRestart') is not True:
                raise ValueError('The same live-authored question and arranged dashboard must be retained for restart')
        categories = all(snapshot.get('category') == 'actual-sdk-forwarding' and snapshot.get('privacyFailures') == 0
                         and count(snapshot, 'event.Error') == 0 for snapshot in [before, clarified, refine_before, refined, after])
        authoring = (delta(clarified, before, 'session.send') >= 2 and delta(clarified, before, 'event.Completed') >= 2
                     and delta(clarified, before, 'tool.return.dashboard_request_clarification') >= 1
                     and delta(clarified, before, 'tool.return.dashboard_preview_question') >= 1
                     and count(refine_before, 'session.send') == count(clarified, 'session.send')
                     and delta(refined, refine_before, 'session.send') >= 1 and delta(refined, refine_before, 'event.Completed') >= 1
                     and delta(refined, refine_before, 'tool.return.dashboard_preview_question') >= 1)
        native_clean = (required_phases <= {phase.get('phase') for phase in phases}
                        and count(after, 'session.send') == count(refined, 'session.send')
                        and all(phase.get('delta') == 0 and phase.get('before') == phase.get('after')
                                and phase.get('before') == count(clarified if phase.get('phase') == 'initial-native-preview' else refined, 'session.send') for phase in phases))
        if not categories or authored_sends < 3 or not authoring or not native_clean:
            raise ValueError(title + ': actual SDK authoring/native/privacy boundary was not observed')
    return len(rows)


def restart_verdict(report, output, expected_sha, lifetimes):
    rows = []
    def visit(suites):
        for suite in suites:
            rows.extend((spec.get('title'), test) for spec in suite.get('specs', []) for test in spec.get('tests', []))
            visit(suite.get('suites', []))
    visit(report.get('suites', []))
    if len(rows) != 1 or rows[0][0] != RESTART_TITLE or report.get('errors'):
        raise ValueError('The real restarted-host journey must run exactly once')
    test = rows[0][1]
    results = test.get('results') or []
    if test.get('projectName') != 'live-desktop' or test.get('expectedStatus') != 'passed' or test.get('status') != 'expected' or len(results) != 1 or results[0].get('status') != 'passed':
        raise ValueError('A missing, retried, skipped or failed restart cannot qualify persistence')
    attachments = results[0].get('attachments') or []
    required = {'source', 'trace', 'restart-proof.json'} | {f'{name}.{suffix}' for name in RESTART_CAPTURES for suffix in ('png', 'aria.yml')}
    if not required <= {item.get('name') for item in attachments}:
        raise ValueError('Restart needs actual UI, trace, source, authority and persistence evidence')
    paths = {}
    for item in attachments:
        if item.get('name') not in required:
            continue
        path = Path(item.get('path', '')).resolve()
        if not path.is_relative_to(output.resolve()) or not path.is_file() or not path.stat().st_size:
            raise ValueError('A restart artifact is absent, empty or outside owned evidence')
        paths[item['name']] = path
    if paths['source'].read_text().strip() != expected_sha:
        raise ValueError('Restart source differs from the actual committed run')
    trace_context_evidence(paths['trace'], minimum_contexts=1, owner_drill=True)
    proof = json.loads(paths['restart-proof.json'].read_text())
    flags = ('providerDisabled', 'sameQuestion', 'sameDashboard', 'sameTileOrderAndSizes')
    if not all(proof.get(key) is True for key in flags) or proof.get('sdkSendsBefore') != 0 or proof.get('sdkSendsAfter') != 0:
        raise ValueError('Restart must preserve the authored definitions and use no provider turns')
    if len(lifetimes) != 2 or any(item.get('backendExit') != 0 or item.get('studioExit') != 0 for item in lifetimes) or lifetimes[0].get('backendPid') == lifetimes[1].get('backendPid') or lifetimes[0].get('studioPid') == lifetimes[1].get('studioPid') or lifetimes[1].get('sameOwnedStores') is not True:
        raise ValueError('A reload or recreated store cannot substitute for two stopped owned-host lifetimes')
    return 1


def execute_live(output, options, native, report):
    env = credential_environment(options.mode, options.account_access_restored, options.credential_environment_variable, native)
    with tempfile.TemporaryDirectory(prefix='elsa-dashboard-live-state-') as temporary:
        data = Path(temporary)
        backend, studio = data / 'backend', data / 'studio'
        copy_runtime(output / 'artifacts/bin' / HOST_NAME / 'debug', backend)
        copy_runtime(output / 'artifacts/bin/Elsa.Studio.Web/debug', studio)
        backend_source = ROOT / 'Essentials/apps/Elsa.Workbench'
        studio_source = ROOT / 'Studio/src/apps/Elsa.Studio.Web'
        for target, source in [(backend, backend_source), (studio, studio_source)]:
            for name in ('appsettings.json', 'shells.json'):
                shutil.copy2(source / name, target / name)
        backend_url = f'http://localhost:{native.free_port()}'
        studio_url = f'http://localhost:{native.free_port()}'
        settings = json.loads((backend / 'appsettings.json').read_text(encoding='utf-8-sig'))
        shells = json.loads((backend / 'shells.json').read_text(encoding='utf-8-sig'))
        profile(settings, shells, studio_url, options.model, native)
        (backend / 'appsettings.json').write_text(json.dumps(settings))
        (backend / 'shells.json').write_text(json.dumps(shells))
        home = data / 'home'
        home.mkdir()
        (backend / 'model-workspace').mkdir()
        env.update(HOME=str(home), DOTNET_CLI_HOME=str(home))
        report.update(stage='provider-preflight', hostProfile='dedicated live source host; normal IAM; actual SDK; owned private stores',
                      sdkBoundary='all forwarded create/resume session.SendAsync calls; physical model HTTP requests are not exposed')
        identities = (backend.stat().st_ino, studio.stat().st_ino)
        studio_env = dict(native.clean_environment(), HOME=str(home), DOTNET_CLI_HOME=str(home), Studio__BackendBaseUrl=backend_url)
        browser_env = dict(native.clean_environment(), UX_DASHBOARD_LIVE_OWNED='1', UX_ACCEPTANCE_OWNED='1',
                           ELSA_STUDIO_URL=studio_url, UX_BACKEND_URL=backend_url, ELSA_WORKBENCH_URL=backend_url,
                           UX_ACCEPTANCE_OUT=str(output), UX_SOURCE_SHA=report['sourceSha'])
        browser_command = ['pnpm', 'exec', 'playwright', 'test', '--config', 'tests/acceptance/playwright.live-dashboards.config.ts']
        with native.host(backend, 'Elsa.Workbench.dll', backend_url, '/__qualification/ready', output / 'backend.log', env) as first_backend:
            preflight = live_preflight(backend_url)
            report['preflight'] = preflight
            if not preflight['available']:
                report.update(status='unavailable', stage='provider-preflight', error='Actual provider preflight unavailable; no automatic retry or browser/model turn attempted')
                raise LiveUnavailable('Actual provider unavailable; no automatic retry')
            if options.model not in preflight['declaredModels']:
                raise ValueError('Requested pinned model is absent from actual provider diagnostics; no turn attempted')
            with native.host(studio, 'Elsa.Studio.Web.dll', studio_url, '/studio-runtime.js', output / 'studio.log',
                             studio_env) as first_studio:
                report['stage'] = 'actual-live-journeys'
                command(native, browser_command, output / 'journeys.log', cwd=ROOT / 'Studio', env=browser_env)
                report['executed'] = verdict(json.loads((output / 'playwright.json').read_text()), output, report['sourceSha'])
        lifetimes = [{'phase': 'authoring', 'backendPid': first_backend.pid, 'studioPid': first_studio.pid,
                      'backendExit': first_backend.poll(), 'studioExit': first_studio.poll()}]
        report['hostLifetimes'] = lifetimes
        if lifetimes[0]['backendExit'] != 0 or lifetimes[0]['studioExit'] != 0:
            raise ValueError('Both first-lifetime hosts must stop normally before restart')
        if not (output / 'workflow-retained.json').is_file():
            raise ValueError('No authored definition manifest survived the first host lifetime')
        # Preserve the original directories and databases. Only disable provider access.
        # No profile/bootstrap materialization or store recopy occurs at this boundary.
        restart_shells = json.loads((backend / 'shells.json').read_text())
        features = restart_shells['CShells']['Shells']['default']['Features']
        for name in ('WeaverGitHubCopilot', 'DashboardLiveQualification'):
            features[name]['Enabled'] = False
        (backend / 'shells.json').write_text(json.dumps(restart_shells))
        restart_env = dict(env)
        for name in {MODEL_ENVIRONMENT, options.credential_environment_variable, 'GITHUB_TOKEN', 'GH_TOKEN'}:
            restart_env.pop(name, None)
        report['stage'] = 'native-real-host-restart'
        with native.host(backend, 'Elsa.Workbench.dll', backend_url, '/__qualification/ready', output / 'backend-restart.log', restart_env) as second_backend:
            with native.host(studio, 'Elsa.Studio.Web.dll', studio_url, '/studio-runtime.js', output / 'studio-restart.log', studio_env) as second_studio:
                command(native, browser_command, output / 'restart-journey.log', cwd=ROOT / 'Studio',
                        env=dict(browser_env, UX_DASHBOARD_RESTART='1'))
        lifetimes.append({'phase': 'provider-disabled-restart', 'backendPid': second_backend.pid, 'studioPid': second_studio.pid,
                          'backendExit': second_backend.poll(), 'studioExit': second_studio.poll(),
                          'sameOwnedStores': identities == (backend.stat().st_ino, studio.stat().st_ino)})
        report['restartExecuted'] = restart_verdict(json.loads((output / 'playwright-restart.json').read_text()), output, report['sourceSha'], lifetimes)
        report.update(status='passed', stage='complete', liveQualified=True)


def run(options):
    output = options.output.resolve()
    if not options.output.is_absolute() or output.is_relative_to(ROOT) or output.exists():
        raise ValueError('Use a new absolute owned directory outside the checkout')
    if options.mode == 'live' and not options.account_access_restored:
        raise ValueError('Live execution is disabled until the owner confirms restored account access')
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._/-]{0,127}', options.model) or options.model.lower() == 'auto':
        raise ValueError('Pin an explicit provider model; auto cannot qualify a model')
    output.mkdir(parents=True, mode=0o700)
    start = source_state()
    report = {'sourceSha': start['sha'], 'sourceDirty': bool(start['dirty']), 'mode': options.mode,
              'status': 'failed', 'stage': 'prepare', 'liveQualified': False, 'requestedModel': options.model,
              'providerCalls': 'not run', 'visualApproval': 'requires independent current-commit review',
              'startedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}
    try:
        if options.mode == 'live' and start['dirty']:
            raise ValueError('Actual live qualification requires a clean committed source revision')
        project, hook, seeded = materialize(output)
        if options.mode in {'build', 'live'}:
            report['stage'] = 'isolated-build'
            build(output, project, hook, seeded, native_helpers())
        report.update(status='prepared', stage='not-run', liveQualified=False)
        if options.mode == 'live':
            report['providerCalls'] = 'actual preflight requested once; see preflight and SDK counter evidence'
            execute_live(output, options, native_helpers(), report)
        if source_state() != start:
            report.update(liveQualified=False)
            raise ValueError('Source changed during the run; evidence cannot qualify a revision')
    except LiveUnavailable:
        report.update(status='unavailable', liveQualified=False)
        raise
    except BaseException as failure:
        # Tool/provider exceptions may hold request data. Retain only a fixed category and exception type.
        report.update(status='failed', liveQualified=False, failureType=type(failure).__name__)
        raise
    finally:
        report['finishedAt'] = time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())
        report['artifacts'] = {str(path.relative_to(output)): sha(path) for path in output.rglob('*')
                               if path.is_file() and path.name not in {'report.json'} and path.suffix in {'.png', '.yml', '.json', '.zip'}
                               and not any(part in {'locks', 'artifacts', 'host'} for part in path.relative_to(output).parts)}
        (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
        print(json.dumps(report, indent=2), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path, help='New absolute private owned directory outside source')
    parser.add_argument('--mode', choices=['prepare', 'build', 'live'], default='prepare')
    parser.add_argument('--model', required=True, help='Explicit pinned model id; never auto')
    parser.add_argument('--account-access-restored', action='store_true', help='Live mode only: owner explicitly confirmed restored account access')
    parser.add_argument('--credential-environment-variable', help='Live mode only: approved environment-variable name; no file or token value')
    options = parser.parse_args()
    def terminate(_signum, _frame):
        raise InterruptedError('Owned live qualification interrupted')
    signal.signal(signal.SIGTERM, terminate)
    try:
        run(options)
    except LiveUnavailable:
        raise SystemExit(2) from None


if __name__ == '__main__':
    main()

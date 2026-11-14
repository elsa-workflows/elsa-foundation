#!/usr/bin/env python3
"""Build and inspect real source hosts, owning every process and temporary store."""
from __future__ import annotations

import argparse
import hashlib
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
import zipfile

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = {'desktop-light', 'desktop-dark', 'narrow-light', 'narrow-dark'}
CAPTURES = {
    'saved dashboard period comparison and membership': ['reference', 'comparison-excluded', 'comparison-excluded-empty', 'comparison-included', 'comparison-included-periods', 'comparison-included-values', 'comparison-included-values-end', 'comparison-saved-reopened', 'comparison-reopened-periods', 'comparison-reopened-values', 'comparison-reopened-values-end', 'comparison-current-drill'],
    'phone authoring and settings content priority': ['reference', 'phone-authoring-entry', 'phone-inspector', 'phone-inspector-maximized', 'phone-activities', 'authoring-768', 'authoring-1280', 'phone-theme-entry', 'phone-theme-assets', 'theme-desktop-dirty', 'theme-tablet-dirty', 'phone-theme-dirty', 'phone-theme-short', 'phone-theme-reopened', 'phone-settings-navigation'],
    'settings tabs and preserved edits': ['reference', 'modules-studio', 'modules-server', 'modules-manifest', 'modules-diagnostics', 'feeds-studio', 'feeds-server', 'features-studio', 'features-server', 'features-error', 'theme-overview', 'theme-tokens', 'theme-mode-last', 'theme-assets', 'theme-preview', 'theme-edited', 'modules-unavailable-content', 'modules-overview-content', 'modules-manifest-content', 'modules-diagnostics-content', 'feeds-operations-content', 'feeds-retention-content', 'feeds-ledger-content', 'feeds-unavailable-content', 'features-studio-list-content', 'features-studio-inspector-content', 'features-server-list-content', 'features-server-inspector-content', 'theme-overview-content', 'theme-tokens-content', 'theme-mode-last-content', 'theme-preview-content', 'theme-preview-validation'],
    'structured logs readability and recovery': ['reference', 'readability-1440', 'readability-1280', 'readability-1024', 'readability-390', 'readability-wrap', 'readability-compact', 'readability-empty', 'readability-reset', 'readability-dock'],
    'structured logs table accessibility': ['reference', 'logs-page', 'logs-selected', 'logs-empty', 'logs-dock'],
    'activity definition identity survives reopening': ['reference', 'activity-created', 'activity-reloaded', 'activity-reopened'],
    'tabbed Secrets authoring and metadata': ['reference', 'secret-section', 'first-invalid-field', 'storage-section', 'details-section', 'persisted-list', 'persisted-metadata', 'metadata-390', 'storage-390', 'configuration-reference', 'configuration-metadata'],
    'business decisions authoring and explanations': ['reference', 'decisions-list', 'decisions-search', 'decision-create', 'decision-table', 'decision-case-editor', 'decision-case-expectation', 'decision-cases', 'decision-regression', 'decision-published', 'decision-explanation', 'decision-clone', 'decision-conflict', 'decision-history', 'decisions-no-matches', 'decision-imported', 'decision-table-320', 'decision-table-320-scrolled', 'decision-picker', 'decision-picker-new-tab', 'decision-picker-saved-tab', 'decision-picker-selected-toast', 'decision-picker-returned', 'decision-saved-toast'],
    'feature filtering and settings dialogs': ['reference', 'features-server-no-matches', 'features-server-populated', 'features-studio-no-matches', 'features-studio-populated', 'feature-settings-dialog', 'feature-impact-dialog'],
    'collections and connection creation': ['reference', 'no-matches', 'connections-list', 'secret-create', 'secret-list', 'secret-detail', 'secret-duplicate', 'invalid-name', 'completed', 'secret-created-toast', 'secret-dialog-over-toast', 'primary-action-with-toasts', 'toasts-above-expanded-dock', 'toasts-over-maximized-dock', 'notification-history'],
    'publication blockers explain how to correct agent settings': ['reference', 'blocked', 'instructions-focused', 'model-blocked', 'model-focused', 'completed'],
    'save template toast': ['reference', 'save-form', 'saved-toast', 'template-list', 'reopened-template'],
    'workflow template metadata defaults': ['reference', 'template-defaults', 'template-edits', 'template-catalog-entry', 'template-persisted'],
    'user tasks inbox and response': ['reference', 'tasks-empty', 'tasks-list', 'task-detail', 'task-actions', 'task-history', 'task-forms', 'task-form-versions', 'task-form-editor', 'task-form-actions', 'completed', 'tasks-return', 'task-not-found', 'task-tab-return'],
    'executable designer parity': ['reference', 'definition-sequence', 'executable-sequence', 'nested-sequence', 'draft-sequence', 'empty-sequence', 'compensation', 'flowchart', 'bpmn'],
    'choose data for activity outputs': ['reference', 'choose-data-entry', 'chooser-open', 'output-reference', 'not-selectable', 'replace-chooser', 'chooser-320', 'capability-control'],
    'choose nested activity data': ['reference', 'customer-sources', 'customer-members', 'customer-reference', 'customer-code-reopened', 'customer-flowchart', 'customer-held', 'customer-loading', 'customer-retry', 'customer-320'],
    'choose declared and scoped activity data': ['reference', 'source-groups', 'variable-literal', 'variable-empty', 'variable-reference', 'outer-variable-reference', 'declared-reference', 'unsupported-request', 'request-repaired', 'no-request-proposal', 'source-320'],
    'agent authoring and navigation': ['reference', 'agents-list', 'agent-create', 'agent-designer', 'agent-model', 'advanced-settings', 'inputs', 'outputs', 'input-general', 'input-presentation', 'input-advanced', 'output-general', 'output-presentation', 'output-advanced', 'completed'],
    'user task authoring and workflow result': ['reference', 'form-authoring', 'published-form', 'task-authoring', 'form-selection', 'json-output-definitions', 'renamed-output', 'removed-output', 'output-picker', 'frozen-task-form', 'run-outcomes-initial', 'run-outcomes', 'run-outcomes-readable', 'run-output-reached', 'workflow-result', 'task-fault-causes', 'task-fault-details', 'prefill-fault'],
    'compact runtime Inputs retain exact captured values and accessible technical details': ['reference', 'compact-values', 'technical-details', 'nested-values', 'details-closed', 'technical-details-320', 'compact-values-320'],
    'native dashboard questions and sharing': ['reference', 'dashboard-native-reference', 'questions-empty', 'question-controls', 'question-validation', 'question-preview', 'question-drill', 'question-drill-values', 'question-saved', 'question-refreshed', 'question-editor-preview-appearance-1', 'question-editor-preview-appearance-2', 'question-saved-refresh-drill', 'question-saved-refresh-drill-values', 'question-history', 'question-copy', 'question-records', 'question-navigation-cancelled', 'question-retained-tab', 'question-navigation-confirmed', 'dashboard-layout', 'dashboard-reopened', 'dashboard-multi-tile', 'dashboards-collection-appearance-1', 'dashboards-collection-appearance-2', 'dashboard-and-dependency-dialogs-appearance-1-dashboard', 'dashboard-and-dependency-dialogs-appearance-1-share-review', 'dashboard-and-dependency-dialogs-appearance-1-copy-review', 'dashboard-and-dependency-dialogs-appearance-2-dashboard', 'dashboard-and-dependency-dialogs-appearance-2-share-review', 'dashboard-and-dependency-dialogs-appearance-2-copy-review', 'dashboard-refresh-cancelled', 'dashboard-refresh-retried', 'dashboard-tile-settings', 'dashboard-drill', 'dashboard-drill-values', 'dashboard-copy-dependency-review', 'dashboard-copy-independent-question', 'dashboard-copy', 'question-share', 'dashboard-share-dependency-review', 'dashboard-share', 'question-shared-search', 'shared-question', 'shared-dashboard', 'question-source-unavailable', 'question-repair-blank', 'question-repair-reopened', 'question-revoke', 'question-access-lost', 'dashboard-unavailable-tiles', 'question-new-reuse-confirmation', 'questions-collection-appearance-1', 'questions-collection-appearance-2', 'question-conflict', 'question-conflict-recovered'],
    'existing home widgets and layout defaults': ['reference', 'home-widgets', 'home-module-picker-1', 'home-module-picker-2', 'home-module-run-health', 'home-module-portfolio', 'home-module-settings', 'home-module-settings-reopened', 'home-layout-edit', 'home-layout-reopened', 'home-populated-appearance-1', 'home-populated-appearance-2', 'home-widget-picker', 'home-server-default', 'home-existing-aliases'],
    'execution navigation keeps keyboard focus and exact evidence': ['reference', 'standalone-exact', 'standalone-menu', 'completed-while-observation-paused', 'attached-first-boundary', 'retained-middle-occurrence', 'restored-exact-occurrence'],
    'timeline selection retains its tab and exact occurrence': ['reference', 'timeline-populated', 'timeline-click', 'timeline-keyboard', 'activity-details', 'timeline-return'],
}
NATIVE_RESULT_CAPTURES = ('provenance', 'coverage-periods', 'series-1', 'series-2', 'series-3', 'exact-values', 'exact-values-end')
CAPTURES['native dashboard questions and sharing'] += (
    [f'question-preview-{section}' for section in NATIVE_RESULT_CAPTURES]
    + [f'question-editor-preview-appearance-{appearance}-{section}' for appearance in (1, 2)
       for section in ('parameters', 'parameters-end', 'presentation', 'presentation-end', *NATIVE_RESULT_CAPTURES)]
    + [f'dashboard-and-dependency-dialogs-appearance-{appearance}-tile-{tile}-{section}'
       for appearance in (1, 2) for tile in (1, 2, 3, 4) for section in ('header', *NATIVE_RESULT_CAPTURES)]
    + [f'dashboard-and-dependency-dialogs-appearance-{appearance}-{dialog}-end'
       for appearance in (1, 2) for dialog in ('share-review', 'copy-review')]
    + ['dashboard-share-dependency-review-end', 'dashboard-copy-dependency-review-end', 'dashboard-copy-question-form',
       'dashboard-long-name', 'dashboard-long-name-share', 'dashboard-long-question-picker', 'dashboard-list-tile', 'dashboard-list-values', 'dashboard-metric-tile', 'dashboard-metric-values', 'dashboard-metric-values-end']
)
JOURNEYS = set(CAPTURES)
MEMBER_JOURNEYS = {'choose nested activity data', 'choose declared and scoped activity data'}
DATA_JOURNEYS = MEMBER_JOURNEYS | {'choose data for activity outputs'}
COMPOSITIONS = {'normal', 'minimal', 'default-off'}


def selected_journeys(journeys=None, composition='normal'):
    if composition != 'normal' and journeys is None:
        raise ValueError('Nonnormal composition requires explicit journeys')
    if journeys is not None and not isinstance(journeys, str) and len(journeys) != len(set(journeys)):
        raise ValueError('Duplicate journey selections')
    selected = JOURNEYS if journeys is None else {journeys} if isinstance(journeys, str) else set(journeys)
    if not selected or not selected <= JOURNEYS or composition not in COMPOSITIONS:
        raise ValueError('Unknown or empty journey/composition inventory')
    if composition != 'normal' and not selected <= DATA_JOURNEYS:
        raise ValueError('Minimal/default-off composition requires explicit activity-data journeys')
    if composition == 'default-off' and selected != {'choose data for activity outputs'}:
        raise ValueError('Default-off requires the whole-output control; member journeys cannot be skipped')
    return selected


def configure_activity_data(backend_shells, studio_shells, composition, journeys):
    """Configure private copies only; default-off observes the committed default without forcing a false flag."""
    if not journeys & DATA_JOURNEYS:
        return
    backend = backend_shells['CShells']['Shells']['default']['Features']
    studio = studio_shells['CShells']['Shells']['Default']['Features']
    if composition != 'default-off':
        backend.setdefault('WorkflowDesignValidations', {})['MemberReads'] = True
    if composition == 'minimal':
        for key in ('HttpJavaScript', 'JavaScriptActivities', 'JavaScriptEndpoints', 'JavaScriptExpressions',
                    'JavaScriptJintEngine', 'JavaScriptWorkflowsDesign', 'Liquid'):
            backend.pop(key, None)
        for key in ('JavaScriptExpressionEditorStudio', 'LiquidExpressionEditorStudio'):
            studio.pop(key, None)
ADDITIONAL_ARTIFACTS = {
    **{journey: {'activity-data-proof.json'} for journey in DATA_JOURNEYS},
    'choose nested activity data': {'activity-data-proof.json', 'customer-catalog.json'},
    'saved dashboard period comparison and membership': {'native-weaver-boundary.json', 'dashboard-comparison-oracle.json'},
    'native dashboard questions and sharing': {'native-weaver-boundary.json', 'reader-weaver-boundary.json', 'dashboard-native-oracle.json', 'dashboard-native-presentations.json', 'question-editor-preview-appearance-matrix.json', 'dashboards-collection-appearance-matrix.json', 'dashboard-and-dependency-dialogs-appearance-matrix.json', 'questions-collection-appearance-matrix.json'},
    'existing home widgets and layout defaults': {'native-weaver-boundary.json', 'home-populated-appearance-matrix.json', 'home-default-baseline.json', 'home-module-widgets.json', 'home-preferences-restored.json'},
}


def configure_dashboard_qualification(settings, shells):
    """Only the temporary QA host gets native password principals and predefined CAS grant profiles."""
    # The QA bootstrap owns explicit catalog preparation and activation/readiness.
    settings.setdefault('Elsa', {}).setdefault('Readiness', {})['WarmDefaultShell'] = False
    native = ['api-capabilities:read', 'studio/questions:read', 'studio/questions:create', 'studio/questions:edit',
              'studio/dashboards:read', 'studio/dashboards:create', 'studio/dashboards:edit',
              'workflows/dashboard-queries:read', 'workflows/activity-executions:inspect',
              'workflows/runtime:read', 'workflows/design:read']
    no_source = [grant for grant in native if grant not in
                 {'workflows/dashboard-queries:read', 'workflows/activity-executions:inspect'}]
    settings['Qualification'] = {
        'Principals': [{'subject': 'dashboard-qa-reader', 'tenant': 'default',
                        'username': 'dashboard-reader', 'password': 'Password123!', 'exactDirectPermissions': native}],
        'GrantProfiles': {'dashboard-native-reader': native, 'dashboard-native-no-source': no_source},
    }
    # Keep the selected adapter composed, with inference explicitly unavailable in this native-only run.
    features = shells['CShells']['Shells']['default']['Features']
    features['WeaverGitHubCopilot']['Enabled'] = False


def signal_owned_group(process, signal_number):
    try:
        os.killpg(process.pid, signal_number)
    except ProcessLookupError:
        pass
    except PermissionError:
        # macOS may deny signalling a retired group after its leader was reaped.
        # Only tolerate this when no living process owned by us remains in that group.
        if process.poll() is None:
            raise
        # A reaped leader can leave a member visible for a brief exit transition.
        # Permit only bounded quiescence; a live owned member still fails cleanup.
        for attempt in range(10):
            members = subprocess.check_output(['ps', '-axo', 'uid=,pgid=,stat='], text=True)
            if not any(len(parts := line.split()) >= 3 and int(parts[0]) == os.getuid()
                       and int(parts[1]) == process.pid and not parts[2].startswith('Z')
                       for line in members.splitlines()):
                return
            if attempt < 9:
                time.sleep(0.05)
        raise


def stop_group(process, *, graceful_timeout=15):
    # The leader can exit before Chromium/workers, including a worker that ignores TERM.
    # Drain the group even after its leader exited, then kill any remaining owned members.
    signal_owned_group(process, signal.SIGTERM)
    try:
        process.wait(timeout=graceful_timeout)
    except subprocess.TimeoutExpired:
        pass
    finally:
        signal_owned_group(process, signal.SIGKILL)
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


def recorded_browser_contexts(path):
    contexts = set()
    with zipfile.ZipFile(path) as archive:
        for name in archive.namelist():
            if name.endswith('.trace'):
                for line in archive.read(name).splitlines():
                    event = json.loads(line)
                    if event.get('type') == 'context-options' and event.get('origin') == 'library' and event.get('contextId'):
                        contexts.add(event['contextId'])
    return contexts


def verdict(report, artifacts, journeys=None, composition="normal"):
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
        required = {'trace'} | ADDITIONAL_ARTIFACTS.get(title, set()) | {f'{capture}.{suffix}' for capture in CAPTURES[title] for suffix in ('png', 'aria.yml')}
        if not required <= names:
            raise ValueError(f'{project} / {title}: missing artifacts {sorted(required - names)}')
        for item in attachments:
            if not item.get('path') and item.get('name') not in required:
                continue
            file = Path(item.get('path', '')).resolve()
            if not file.is_relative_to(artifacts.resolve()) or not file.is_file() or not file.stat().st_size:
                raise ValueError(f'{project} / {title}: missing or unsafe artifact {item.get("name")}')
        if title in DATA_JOURNEYS:
            activity_data_verdict(title, attachments, composition)
        if title == 'native dashboard questions and sharing':
            trace = next(item['path'] for item in attachments if item.get('name') == 'trace')
            if len(recorded_browser_contexts(trace)) < 2:
                raise ValueError(f'{project} / {title}: independent owner and reader contexts are missing from the combined trace')
    return len(rows)


def activity_data_verdict(title, attachments, composition):
    files = {item.get('name'): item.get('path') for item in attachments}
    proof = json.loads(Path(files['activity-data-proof.json']).read_text())
    if proof.get('composition') != composition:
        raise ValueError('Activity data proof composition mismatch')
    analyses = [item.get('response', {}).get('analysis', {}) for item in proof.get('contexts', [])
                if item.get('response', {}).get('state') in {'supported', 'empty'}]
    if not any(item.get('memberReadsEnabled') is (composition != 'default-off') for item in analyses):
        raise ValueError('Activity data capability was not observed')
    if composition == 'minimal' and any(re.search(r'expressioneditors?\.(javascript|liquid)|javascript.expression|liquid.expression', url, re.I)
                                        for url in proof.get('scripts', [])):
        raise ValueError('Optional expression editor script requested in minimal composition')
    selections = proof.get('selections', [])
    if any(item.get('binding') != item.get('offered') or not item.get('binding') for item in selections):
        raise ValueError('Selected binding differs from actual server offer')
    runs = proof.get('runtime', [])
    if title == 'choose nested activity data':
        if len(selections) != 2 or any(item['binding'] != {'expressionType': 'ActivityResultMember', 'value': {
                'producerNodeId': 'producer', 'projectionKey': 'Customer', 'isOptional': False, 'memberPath': ['customerId']}} for item in selections):
            raise ValueError('Customer canonical binding inventory incomplete')
        if len(runs) != 2 or any(item.get('committed') != 'cust-1042' or len(item.get('producers', [])) != 1 or
                                item['producers'][0].get('status') != 'Completed' for item in runs):
            raise ValueError('Customer committed runtime/producer evidence incomplete')
    elif title == 'choose declared and scoped activity data':
        expected = [{'expressionType': 'VariableMember', 'value': {'referenceKey': key, 'declaringScopeId': scope, 'memberPath': ['a.b', '']}}
                    for key, scope in [('inner-payload', 'root'), ('outer-payload', 'workflow')]]
        expected += [{'expressionType': 'WorkflowRequestMember', 'value': {'memberKey': 'payload', 'memberPath': ['a.b', '']}}] * 2
        if [item['binding'] for item in selections] != expected:
            raise ValueError('Scoped/declared canonical binding inventory incomplete')
        if len(runs) != 3 or {item.get('committed') for item in runs} != {'inner-id', 'outer-id', 'declared-ui-id'}:
            raise ValueError('Distinct scoped/declared committed runtime evidence incomplete')
    else:
        whole_runs = [json.loads(Path(path).read_text()) for name, path in files.items() if name and name.startswith('whole-output-runtime-')]
        if len(whole_runs) != 2 or any(item.get('committed') != '00:00:01' for item in whole_runs):
            raise ValueError('Whole-output committed runtime inventory incomplete')
        if not any(source.get('binding', {}).get('expressionType') == 'ActivityResult' and
                   source.get('binding', {}).get('value', {}).get('projectionKey') == 'Customer'
                   for analysis in analyses for source in analysis.get('sources', []) if source.get('binding')):
            raise ValueError('Member-capable typed source control was not observed')
    if any(item.get('consumer', {}).get('status') != 'Completed' for item in runs):
        raise ValueError('Consumer did not complete')


def field_gallery_verdict(report, artifacts, requested_port=None):
    """Retain renderer-level evidence separately from composed product acceptance."""
    rows = []
    def walk(suites):
        for suite in suites:
            for spec in suite.get('specs', []):
                for test in spec.get('tests', []):
                    rows.append((spec.get('title'), test))
            walk(suite.get('suites', []))
    walk(report.get('suites', []))
    expected = report.get('config', {}).get('metadata', {}).get('outlinedCases')
    if not expected or len(rows) != expected or len({title for title, _ in rows}) != expected or report.get('errors'):
        raise ValueError('Incomplete Outlined field gallery inventory')
    observed_url = report.get('config', {}).get('metadata', {}).get('outlinedBaseURL')
    if requested_port is not None and observed_url is not None and observed_url != f'http://127.0.0.1:{requested_port}':
        raise ValueError('The selected frontend gallery did not use its requested owned port')
    for title, test in rows:
        results = test.get('results', [])
        if test.get('expectedStatus') != 'passed' or test.get('status') != 'expected' or len(results) != 1 or results[0].get('status') != 'passed':
            raise ValueError(f'{title}: not a single successful field-gallery execution')
        images = [item for item in results[0].get('attachments', []) if item.get('contentType') == 'image/png']
        if not images:
            raise ValueError(f'{title}: missing field-state screenshot')
        for item in images:
            file = Path(item.get('path', '')).resolve()
            if not file.is_relative_to(artifacts.resolve()) or not file.is_file() or not file.stat().st_size:
                raise ValueError(f'{title}: missing or unsafe field-state screenshot')
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
            # .NET's normal host shutdown window is 30 seconds. Leave bounded scheduling
            # room on the shared machine before killing descendants; callers still require
            # an actual zero exit code, so a forced kill never qualifies a host restart.
            stop_group(process, graceful_timeout=45)


def prepare_host(project, destination):
    output = project.parent / 'bin/Debug/net10.0'
    shutil.copytree(output, destination, ignore=shutil.ignore_patterns("*.db*", "*.sqlite*", "nuplane.lock.json", ".elsa", ".nuplane", "packages", "appsettings*.json", "shells*.json"))
    # Older local outputs may contain stores. Never seed acceptance from them.
    for file in destination.rglob('*'):
        if file.is_file() and (file.suffix in {'.db', '.db-shm', '.db-wal'} or file.name == 'nuplane.lock.json'):
            file.unlink()
    for name in ['appsettings.json', 'shells.json']:
        shutil.copy2(project.parent / name, destination / name)


def source_state(root):
    sha = subprocess.check_output(['git', '-c', 'core.fsmonitor=false', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    dirty = subprocess.check_output(['git', '-c', 'core.fsmonitor=false', 'status', '--porcelain'], cwd=root, text=True).strip()
    return sha, dirty


def supplies_pr_head(root, source_sha, head):
    if source_sha == head:
        return True
    # pull_request CI normally tests GitHub's two-parent merge, with the reviewed PR as parent2.
    result = subprocess.run(['git', '-c', 'core.fsmonitor=false', 'cat-file', '-p', source_sha],
                            cwd=root, text=True, capture_output=True)
    headers = result.stdout.partition('\n\n')[0].splitlines()
    parents = [line.removeprefix('parent ') for line in headers if line.startswith('parent ')]
    return result.returncode == 0 and len(parents) == 2 and parents[1] == head


def fingerprints(root):
    return {str(file.relative_to(root)): hashlib.sha256(file.read_bytes()).hexdigest()
            for file in sorted(root.rglob('*')) if file.is_file() and
            (file.suffix in {'.dll', '.js', '.css', '.html'} or file.name in {'appsettings.json', 'shells.json'})}


def run(output, journey=None, studio_root=None, composition='normal'):
    output.mkdir(parents=True, exist_ok=False)
    studio_root = (studio_root or ROOT).resolve()
    sha, dirty = source_state(ROOT)
    studio_sha, studio_dirty = source_state(studio_root)
    head = os.environ.get('UX_PR_HEAD_SHA', sha)
    selected = selected_journeys(journey, composition)
    fixture_built = False
    backend_project = ROOT / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj'
    report = {'sourceSha': sha, 'headSha': head, 'dirty': bool(dirty),
              'kind': 'composed-source', 'studioSourceSha': studio_sha, 'studioDirty': bool(studio_dirty), 'startedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
              'status': 'failed', 'stage': 'build', 'visualApproval': 'requires independent review',
              'journeys': sorted(selected), 'composition': composition}
    try:
        if dirty or studio_dirty:
            raise ValueError('Commit source changes before acceptance; a dirty run cannot certify a revision')
        if not (supplies_pr_head(ROOT, sha, head) or supplies_pr_head(studio_root, studio_sha, head)):
            raise ValueError('The claimed PR head supplied neither observed source tree nor a verified CI merge parent')
        studio_project = studio_root / 'Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj'
        backend_command = ['dotnet', 'build', str(backend_project), '-p:RestoreLockedMode=true', '-p:ElsaDashboardQualification=true', '-m:2', '-nr:false', '-v:q', '-nologo']
        if selected & DATA_JOURNEYS:
            fixture = ROOT / 'e2e-tests/activity-data-selection/fixtures/fixture.targets'
            backend_command.append(f'-p:CustomAfterMicrosoftCommonTargets={fixture}')
            report['fixture'] = {str(file.relative_to(ROOT)): hashlib.sha256(file.read_bytes()).hexdigest()
                                 for file in (fixture, fixture.with_name('CustomerFixture.cs'))}
            fixture_built = True
        command(backend_command, output / 'backend-build.log')
        command(['pnpm', 'build'], output / 'frontend-build.log', cwd=studio_root / 'Studio')
        report['stage'] = 'isolated-field-gallery'
        gallery = output / 'outlined-gallery'
        gallery_port = free_port()
        command(['pnpm', 'exec', 'playwright', 'test', '--config', 'tests/browser/playwright.outlined-fields.config.ts'],
                output / 'outlined-gallery.log', cwd=studio_root / 'Studio', env=dict(os.environ, OUTLINED_EVIDENCE=str(gallery), OUTLINED_PORT=str(gallery_port)))
        gallery_report = json.loads((gallery / 'playwright.json').read_text())
        gallery_url = gallery_report.get('config', {}).get('metadata', {}).get('outlinedBaseURL')
        report['isolatedFieldGallery'] = {'executed': field_gallery_verdict(gallery_report, gallery, gallery_port),
                                          'sourceSha': studio_sha, 'kind': 'isolated-shared-components', 'portOverrideConfirmed': gallery_url is not None}
        # Older selected frontend revisions keep their own strict fixed-port config. Its
        # successful renderer proof remains valid, but an ignored override is not an observed URL.
        if gallery_url is not None:
            report['isolatedFieldGallery']['url'] = gallery_url
        report['stage'] = 'build'

        command(['dotnet', 'build', str(studio_project), '-p:RestoreLockedMode=true', '-m:2', '-nr:false', '-v:q', '-nologo'], output / 'studio-build.log')
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
            shells = json.loads((backend / 'shells.json').read_text(encoding='utf-8-sig'))
            identity = shells['CShells']['Shells']['default']['Features']['FoundationIdentityAspNetCoreIdentity']
            identity['AllowedReturnUrlOrigins'] = [studio_url]
            configure_dashboard_qualification(settings, shells)
            studio_shells = json.loads((studio / 'shells.json').read_text(encoding='utf-8-sig'))
            configure_activity_data(shells, studio_shells, composition, selected)
            (studio / 'shells.json').write_text(json.dumps(studio_shells))
            report['composedFeatures'] = {'backend': sorted(shells['CShells']['Shells']['default']['Features']),
                                          'studio': sorted(studio_shells['CShells']['Shells']['Default']['Features'])}
            (backend / 'appsettings.json').write_text(json.dumps(settings))
            (backend / 'shells.json').write_text(json.dumps(shells))
            report['binaryAndConfigHashes'] = {'backend': fingerprints(backend), 'studio': fingerprints(studio)}
            report['ownedRoot'] = str(data)
            env = dict(clean_environment(), DOTNET_PROCESSOR_COUNT='2', HOME=str(data / "home"), DOTNET_CLI_HOME=str(data / "home"),
                       **{"Logging__LogLevel__Microsoft.Hosting.Lifetime": "Information"})
            (data / "home").mkdir()
            report.update(stage='host-start', studioUrl=studio_url, backendUrl=backend_url,
                          hostProfile='ElsaDashboardQualification=true; private native principals; Copilot disabled',
                          modelEvidence='Native browser Weaver message submissions only; physical SDK calls not measured')
            with host(backend, 'Elsa.Workbench.dll', backend_url, '/__qualification/ready', output / 'backend.log', env) as backend_process:
                with host(studio, 'Elsa.Studio.Web.dll', studio_url, '/studio-runtime.js', output / 'studio.log',
                          dict(env, Studio__BackendBaseUrl=backend_url)) as studio_process:
                    report['ownedPids'] = {'backend': backend_process.pid, 'studio': studio_process.pid}
                    report['stage'] = 'journeys'
                    browser_env = dict(os.environ, UX_ACCEPTANCE_OWNED='1', ELSA_STUDIO_URL=studio_url,
                                       ELSA_WORKBENCH_URL=backend_url, UX_BACKEND_URL=backend_url,
                                       UX_ACCEPTANCE_OUT=str(output), UX_SOURCE_SHA=sha, UX_STUDIO_SOURCE_SHA=studio_sha,
                                       UX_ACTIVITY_DATA_COMPOSITION=composition)
                    browser_command = ['pnpm', 'exec', 'playwright', 'test', '--config', 'tests/acceptance/playwright.config.ts']
                    if journey is not None:
                        # Playwright greps project/file/title together; anchor the exact final title.
                        browser_command.extend(['--grep', r'(?:^|\s)(?:' + '|'.join(re.escape(name) for name in sorted(selected)) + ')$'])
                    command(browser_command, output / 'journeys.log', cwd=ROOT / 'Studio', env=browser_env)
                    report['executed'] = verdict(json.loads((output / 'playwright.json').read_text()), output, selected, composition)
        report['hostExitCodes'] = {'backend': backend_process.returncode, 'studio': studio_process.returncode}
        report['ownedRootRemoved'] = not data.exists()
        if (sha, dirty) != source_state(ROOT) or (studio_sha, studio_dirty) != source_state(studio_root):
            raise ValueError('Source changed during acceptance; rerun the exact committed revision')
        if dirty or studio_dirty:
            raise ValueError('Dirty source run is preliminary evidence, not final acceptance')
        report.update(status='passed', stage='complete')
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        # TemporaryDirectory and nested host contexts have ended before removing the opt-in fixture build.
        if fixture_built:
            try:
                command(['dotnet', 'build', str(backend_project), '-p:RestoreLockedMode=true', '-m:2', '-nr:false', '-v:q', '-nologo'], output / 'fixture-cleanup-build.log')
                dll = backend_project.parent / 'bin/Debug/net10.0/Elsa.Workbench.dll'
                if b'ActivityDataSelectionProof' in dll.read_bytes():
                    raise ValueError('Default Workbench still contains the opt-in customer fixture')
                report['fixtureCleanup'] = {'status': 'passed', 'workbenchSha256': hashlib.sha256(dll.read_bytes()).hexdigest(), 'markerAbsent': True}
                if (sha, dirty) != source_state(ROOT) or (studio_sha, studio_dirty) != source_state(studio_root):
                    raise ValueError('Source changed during fixture cleanup')
            except Exception as error:
                report.update(status='failed', cleanupError=str(error))
                raise
            finally:
                report['ownedRootRemoved'] = not Path(report.get('ownedRoot', '/__never_owned__')).exists()
                report['finalSource'] = {'backend': source_state(ROOT), 'studio': source_state(studio_root)}
                (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
        report['finishedAt'] = time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())
        (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
        print(json.dumps(report, indent=2), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True, help='New evidence directory outside disposable stores')
    parser.add_argument('--studio-root', type=Path, help='Optional clean committed Studio checkout; both frontend/backend revisions are recorded and fenced')
    parser.add_argument('--journey', action='append', choices=sorted(JOURNEYS), help='Repeat for exact complete journeys across four projects; default requires every normal journey')
    parser.add_argument('--composition', choices=sorted(COMPOSITIONS), default='normal', help='Owned activity-data host composition; nonnormal modes require explicit journeys')
    args = parser.parse_args()
    def terminate(_signum, _frame):
        raise InterruptedError('Acceptance interrupted; owned hosts will be stopped')
    signal.signal(signal.SIGTERM, terminate)
    run(args.output.resolve(), args.journey, args.studio_root, args.composition)

#!/usr/bin/env python3
"""Build and inspect real source hosts, owning every process and temporary store."""
from __future__ import annotations

import argparse
import base64
from contextlib import closing, contextmanager, ExitStack
import copy
import hashlib
import json
import os
import re
from pathlib import Path
import secrets
import shutil
import signal
import socket
import sqlite3
import subprocess
import tempfile
import time
import urllib.request
from urllib.parse import urlparse
import zipfile

ROOT = Path(__file__).resolve().parents[2]
# The ordinary composed journeys run in every standard project against the combined hosts.
PROJECTS = {'desktop-light', 'desktop-dark', 'narrow-light', 'narrow-dark', 'narrow-390-light', 'narrow-390-dark'}
TOUCH_PROJECTS = {f'touch-{width}-{mode}' for width in (360, 390, 1440) for mode in ('light', 'dark')}
CAPTURES = {
    'native BPMN workflow blocks preserve reviewed XML bindings': ['reference'] + [f'workflow-bpmn-{state}' for state in ('selection', 'preview', 'nested', 'nested-edit', 'nested-reopened', 'reopened', 'import-review', 'imported', 'ungrouped')],
    'native BPMN reusable blocks preserve reviewed XML bindings': ['reference'] + [f'activity-bpmn-{state}' for state in ('selection', 'preview', 'nested', 'nested-edit', 'nested-reopened', 'reopened', 'import-review', 'imported', 'ungrouped')],
    'native BPMN workflow blocks retain authoring boundaries': ['reference'] + [f'workflow-bpmn-{state}' for state in ('inspector-toolbar', 'empty-preview', 'empty-nested', 'empty-reloaded', 'empty-ungrouped', 'refused', 'captured-input', 'readonly')],
    'native BPMN reusable blocks retain authoring boundaries': ['reference'] + [f'activity-bpmn-{state}' for state in ('inspector-toolbar', 'empty-preview', 'empty-nested', 'empty-reloaded', 'empty-ungrouped', 'refused', 'readonly')] + ['activity-bpmn-stale'],
    'activity validation explains and locates graph settings': ['reference', 'variable-blocker', 'variable-focused', 'output-blocker', 'output-focused', 'review-retained', 'review-summary', 'review-focus-return'],
    "native block validation errors and retry": ["reference", "block-validation-invalid", "block-validation-outage-control", "block-validation-retried", "block-validation-persisted"],
    'set output assigns reusable activity output': ['reference', 'set-output-empty', 'set-output-declaration', 'set-output-expression', 'set-output-removed-target', 'set-output-readonly', 'set-output-reopened', 'set-output-connections', 'set-output-conflict-review', 'set-output-conflict-repaired', 'set-output-caller-result'],
    'named blocks preserve declared expression results': ['reference'] + [f'workflow-sequence-{state}' for state in ('selection', 'preview', 'nested', 'reopened', 'created-empty', 'empty-ungrouped', 'ungrouped', 'readonly')],
    'named blocks promote to reusable activities': ['reference'] + [f'{host}-promotion-{state}' for host in ('workflow', 'activity') for state in (('review', 'created', 'published', 'replaced', 'undone') if host == 'workflow' else ('review', 'replaced', 'undone'))],
    'named blocks across graph authoring': ['reference'] + [f'{host}-{kind}-{state}' for host in ('workflow', 'activity') for kind in ('sequence', 'flowchart') for state in ('selection', 'preview', 'nested', 'reopened', 'created-empty', 'empty-ungrouped', 'ungrouped', 'readonly')] + ['workflow-flowchart-created-repeat', 'activity-flowchart-created-repeat', 'activity-flowchart-terminal-readonly', 'workflow-terminal-refusal'],
    "explicit authored activity names survive publication and execution": ["reference", "authored-definition", "authored-executable", "authored-run"],
    'saved dashboard period comparison and membership': ['reference', 'comparison-excluded', 'comparison-excluded-empty', 'comparison-included', 'comparison-included-periods', 'comparison-included-values', 'comparison-included-values-end', 'comparison-saved-reopened', 'comparison-reopened-periods', 'comparison-reopened-values', 'comparison-reopened-values-end', 'comparison-current-drill'],
    'phone authoring and settings content priority': ['reference', 'phone-authoring-entry', 'phone-inspector', 'phone-inspector-maximized', 'phone-activities', 'authoring-768', 'authoring-1280', 'phone-theme-entry', 'phone-theme-assets', 'theme-desktop-dirty', 'theme-tablet-dirty', 'phone-theme-dirty', 'phone-theme-short', 'phone-theme-reopened', 'phone-settings-navigation'],
    'settings tabs and preserved edits': ['reference', 'modules-studio', 'modules-server', 'modules-manifest', 'modules-diagnostics', 'feeds-studio', 'feeds-server', 'features-studio', 'features-server', 'features-error', 'theme-overview', 'theme-tokens', 'theme-mode-last', 'theme-assets', 'theme-preview', 'theme-edited', 'modules-unavailable-content', 'modules-overview-content', 'modules-manifest-content', 'modules-diagnostics-content', 'feeds-operations-content', 'feeds-retention-content', 'feeds-ledger-content', 'feeds-unavailable-content', 'features-studio-list-content', 'features-studio-inspector-content', 'features-server-list-content', 'features-server-inspector-content', 'theme-overview-content', 'theme-tokens-content', 'theme-mode-last-content', 'theme-preview-content', 'theme-preview-validation'],
    'structured logs readability and recovery': ['reference', 'readability-1440', 'readability-1280', 'readability-1024', 'readability-390', 'readability-wrap', 'readability-compact', 'readability-empty', 'readability-reset', 'readability-dock'],
    'structured logs table accessibility': ['reference', 'logs-page', 'logs-selected', 'logs-empty', 'logs-dock'],
    'activity definition identity survives reopening': ['reference', 'activity-created', 'activity-reloaded', 'activity-reopened'] + [f'activity-tabs-{view}' for view in ('designer', 'inputs', 'outputs', 'outcomes', 'variables')],
    'tabbed Secrets authoring and metadata': ['reference', 'secret-section', 'first-invalid-field', 'storage-section', 'details-section', 'persisted-list', 'persisted-metadata', 'metadata-390', 'storage-390', 'configuration-reference', 'configuration-metadata'],
    'business decisions authoring and explanations': ['reference', 'decisions-list', 'decisions-search', 'decision-create', 'decision-table', 'decision-case-editor', 'decision-case-expectation', 'decision-cases', 'decision-regression', 'decision-published', 'decision-explanation', 'decision-clone', 'decision-conflict', 'decision-history', 'decisions-no-matches', 'decision-imported', 'decision-table-320', 'decision-table-320-scrolled', 'decision-picker', 'decision-picker-new-tab', 'decision-picker-saved-tab', 'decision-picker-selected-toast', 'decision-picker-returned', 'decision-saved-toast'],
    'feature filtering and settings dialogs': ['reference', 'features-server-no-matches', 'features-server-populated', 'features-studio-no-matches', 'features-studio-populated', 'feature-settings-dialog', 'feature-impact-dialog'],
    'collections and connection creation': ['reference', 'no-matches', 'connections-list', 'secret-create', 'secret-list', 'secret-detail', 'secret-duplicate', 'invalid-name', 'completed', 'library-initial', 'secret-created-toast', 'secret-dialog-over-toast', 'primary-action-with-toasts', 'toasts-above-expanded-dock', 'toasts-over-maximized-dock', 'notification-history'],
    'grouped navigation and moved destinations': ['reference', 'groups', 'executables-list', 'tags-list', 'tags-create', 'tags-edit', 'library', 'workflow-authoring-settings', 'runtime-settings', 'diagnostic-views', 'search', 'features-navigation', 'completed', 'top-search', 'top-search-no-match', 'top-search-no-match-dismissed', 'top-search-query-edited', 'top-search-query-returned', 'top-search-tab-dismissed', 'top-search-shift-tab-dismissed', 'top-navigation', 'high-contrast'],
    'publication blockers explain how to correct agent settings': ['reference', 'blocked', 'instructions-focused', 'model-blocked', 'model-focused', 'completed'],
    'save template toast': ['reference', 'save-form', 'saved-toast', 'template-list', 'reopened-template'],
    'workflow template metadata defaults': ['reference', 'create-focused-description', 'template-defaults', 'template-edits', 'template-catalog-entry', 'template-persisted'],
    'user tasks inbox and response': ['reference', 'tasks-empty', 'tasks-list', 'task-detail', 'task-actions', 'task-history', 'task-forms', 'task-form-versions', 'task-form-editor', 'task-form-actions', 'completed', 'tasks-return', 'task-not-found', 'task-tab-return'],
    'executable designer parity': ['reference', 'definition-sequence', 'definition-frame', 'definition-frame-dock', 'definition-frame-retained', 'definition-frame-scroll-top', 'definition-frame-scroll-middle', 'definition-frame-dock-scroll-top', 'definition-frame-dock-scroll-middle', 'definition-frame-retained-scroll-top', 'definition-frame-retained-scroll-middle', 'executable-sequence', 'nested-sequence', 'draft-sequence', 'empty-sequence', 'compensation', 'flowchart', 'bpmn'],
    'Double activity output binds to text': ['reference', 'automatic-conversion', 'incompatible-conversion', 'recovered-conversion'],
    'choose data for activity outputs': ['reference', 'choose-data-entry', 'chooser-open', 'output-reference', 'not-selectable', 'replace-chooser', 'chooser-320', 'capability-control', 'capability-reference'],
    'choose nested activity data': ['reference', 'customer-sources', 'customer-members', 'customer-reference', 'customer-code-reopened', 'customer-flowchart', 'customer-held', 'customer-loading', 'customer-retry', 'customer-320'],
    'choose declared and scoped activity data': ['reference', 'source-groups', 'variable-literal', 'variable-empty', 'variable-reference', 'outer-variable-reference', 'declared-reference', 'unsupported-request', 'request-repaired', 'no-request-proposal', 'source-320'],
    'agent authoring and navigation': ['reference', 'agents-list', 'agent-create', 'agent-designer', 'agent-model', 'advanced-settings', 'inputs', 'outputs', 'input-general', 'input-presentation', 'input-advanced', 'output-general', 'output-presentation', 'output-advanced', 'completed'],
    # Supplementary journeys (Studio/tests/acceptance/supplements.spec.ts) run once per their own project.
    'coarse pointer grouped navigation': ['touch-groups-open', 'touch-group-collapsed', 'touch-local-views', 'touch-settings-runtime', 'touch-top-group-open', 'touch-top-search', 'touch-search-no-match', 'touch-search-outside-dismissed', 'touch-keyboard-recovery'],
    'restricted principal settings access': ['restricted-appearance', 'restricted-features-settings-denied', 'restricted-features-bookmark-denied', 'restricted-runtime-readonly', 'restricted-keyboard-recovery'],
    'activity availability load failures and recovery': ['availability-loading', 'availability-settled', 'availability-abort-error', 'availability-abort-recovered', 'availability-client-500-error', 'availability-client-500-recovered'],
    'runtime-only API composition and combined return': ['runtime-workflows-default', 'runtime-design-view-unavailable', 'runtime-design-settings-unavailable', 'runtime-integrations-absent', 'combined-integrations-return'],
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
COPY_SOURCE_PATHS = {'workflow-editor/duplicateActivities.ts', 'workflow-editor/activityReferenceRemapping.ts'}
COPY_ASSET_PREFIX = '/_content/Elsa.Studio.Workflows/studio/modules/workflows/'
COPY_JOURNEYS = {'copy activity data pairs', 'copy sequence activity data pairs', 'copy scoped and portable activity data', 'preserve external activity references on copy'}
CAPTURES.update({
    'copy activity data pairs': ['reference', *[f'{role}-{state}' for role in ('whole', 'member') for state in
        ('original', 'duplicated', 'pasted', 'paste-undo', 'paste-redo', 'copied-start', 'reopened', 'runtime')], 'copy-reference-320'],
    'copy sequence activity data pairs': ['reference', *[f'{role}-{state}' for role in ('whole', 'member') for state in
        ('original', 'duplicated', 'pasted', 'paste-undo', 'paste-redo', 'copied-source', 'reopened', 'reopened-source', 'runtime')], 'copy-reference-320'],
    'copy scoped and portable activity data': ['reference', 'scope-pasted', 'scope-paste-undo', 'scope-paste-redo', 'scope-copied-reference', 'portable-copied-preserved', 'switch-copied-condition'],
    'preserve external activity references on copy': ['reference', 'external-valid-original', 'external-valid-copy',
        'foreign-resource-clipboard-cleared', 'external-invalid-original', 'external-invalid-copy',
        'invalid-publication-refused', 'invalid-reference-320'],
})
MEMBER_JOURNEYS = {'choose nested activity data', 'choose declared and scoped activity data'}
DATA_JOURNEYS = MEMBER_JOURNEYS | COPY_JOURNEYS | {'choose data for activity outputs'}
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
    'Double activity output binds to text': {'numeric-binding-proof.json'},
    'set output assigns reusable activity output': {'set-output-proof.json'},
    **{journey: {'activity-data-proof.json', 'activity-data-copy-proof.json'} for journey in COPY_JOURNEYS},
    **{journey: {'activity-data-proof.json'} for journey in DATA_JOURNEYS - COPY_JOURNEYS},
    'choose nested activity data': {'activity-data-proof.json', 'customer-catalog.json'},
    'saved dashboard period comparison and membership': {'native-weaver-boundary.json', 'dashboard-comparison-oracle.json'},
    'native dashboard questions and sharing': {'native-weaver-boundary.json', 'reader-weaver-boundary.json', 'dashboard-native-oracle.json', 'dashboard-native-presentations.json', 'question-editor-preview-appearance-matrix.json', 'dashboards-collection-appearance-matrix.json', 'dashboard-and-dependency-dialogs-appearance-matrix.json', 'questions-collection-appearance-matrix.json'},
    'existing home widgets and layout defaults': {'native-weaver-boundary.json', 'home-populated-appearance-matrix.json', 'home-default-baseline.json', 'home-module-widgets.json', 'home-preferences-restored.json'},
}

# Structured receipts each supplementary journey must retain beside its screenshots, accessibility snapshots and trace.
EVIDENCE = {
    'coarse pointer grouped navigation': ['touch-device.json', 'touch-evidence.json'],
    'restricted principal settings access': ['restricted-session.json', 'restricted-capabilities.json', 'identity-capabilities.json', 'runtime-settings.json', 'restricted-network.json'],
    'activity availability load failures and recovery': ['availability-requests.json', 'client-500-receipt.json'],
    'runtime-only API composition and combined return': ['subset-capabilities.json', 'subset-observation.json', 'combined-capabilities.json'],
}
JOURNEYS = set(CAPTURES)
SUPPLEMENT_PROJECTS = {
    'coarse pointer grouped navigation': TOUCH_PROJECTS,
    'restricted principal settings access': {'restricted-390-light'},
    'activity availability load failures and recovery': {'availability-390-light'},
    'runtime-only API composition and combined return': {'runtime-api-subset-1440-light'},
}
JOURNEY_PROJECTS = {journey: SUPPLEMENT_PROJECTS.get(journey, PROJECTS) for journey in JOURNEYS}

# API subset compositions remove the opposite plane's API root and its configured reverse-dependency closure
# from a private copy of the combined shells.json (DependsOn closure over the current feature declarations).
# Every listed feature must still be configured; a rename fails setup instead of silently widening a subset.
# A design-only composition is deferred to #4160: without the runtime API the Workbench does not boot today
# (runtime features resolve the undeclared runtime core; dropping publishing then hits #4158).
SUBSET_COMPOSITIONS = {
    'runtime-api-subset': {
        'journeys': {'runtime-only API composition and combined return'},
        'remove': ['ActivitiesDesignApi', 'ActivitiesDispatchWorkflowDesign', 'ActivitiesGraphDesign', 'AgentApi', 'AgentDesign',
                   'ConnectionsApi', 'SecretsApi', 'WorkflowsDashboard', 'WorkflowsDesignApi'],
    },
}
IDENTITY_SEED_FEATURE = 'FoundationIdentityAspNetCoreIdentityEntityFrameworkCore'
RESTRICTED_JOURNEY = 'restricted principal settings access'
RESTRICTED_USER = 'ux-restricted-reader'
RESTRICTED_SEED_ROLE = 'ux-restricted-seed'
RESTRICTED_PERMISSIONS = ('api-capabilities:read', 'workflows/execution-evidence:read', 'workflows/runtime:read')


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
    # Keep stage timing in the Actions log even if the outer job is interrupted.
    # Emit only the log basename: arguments/environment may contain private fixture values.
    started = time.monotonic()
    print(json.dumps({'event': 'ux-command-started', 'log': log.name}), flush=True)
    with log.open('w') as output:
        process = subprocess.Popen(args, cwd=cwd, env=env, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            result = process.wait()
            print(json.dumps({'event': 'ux-command-finished', 'log': log.name,
                              'exitCode': result, 'elapsedSeconds': round(time.monotonic() - started, 3)}), flush=True)
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
    expected = {(project, journey) for journey in journeys for project in JOURNEY_PROJECTS[journey]}
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
        required = {'trace'} | ADDITIONAL_ARTIFACTS.get(title, set()) | {f'{capture}.{suffix}' for capture in CAPTURES[title] for suffix in ('png', 'aria.yml')} | set(EVIDENCE.get(title, []))
        if not required <= names:
            raise ValueError(f'{project} / {title}: missing artifacts {sorted(required - names)}')
        for item in attachments:
            if not item.get('path') and item.get('name') not in required:
                continue
            file = Path(item.get('path', '')).resolve()
            if not file.is_relative_to(artifacts.resolve()) or not file.is_file() or not file.stat().st_size:
                raise ValueError(f'{project} / {title}: missing or unsafe artifact {item.get("name")}')
        if title in COPY_JOURNEYS:
            activity_copy_verdict(title, attachments, composition)
        elif title in DATA_JOURNEYS:
            activity_data_verdict(title, attachments, composition)
        if title == 'native dashboard questions and sharing':
            trace = next(item['path'] for item in attachments if item.get('name') == 'trace')
            if len(recorded_browser_contexts(trace)) < 2:
                raise ValueError(f'{project} / {title}: independent owner and reader contexts are missing from the combined trace')
    return len(rows)


def activity_copy_verdict(title, attachments, composition):
    files = {item.get('name'): item.get('path') for item in attachments}
    proof = json.loads(Path(files['activity-data-copy-proof.json']).read_text())
    if proof.get('composition') != composition or not proof.get('sourceSha') or not proof.get('studioSha'):
        raise ValueError('Copy proof lacks exact source/composition identity')
    receipts, runs, boundaries = proof.get('receipts', []), proof.get('runtime', []), proof.get('boundaries', [])
    owners = proof.get('copyImplementationAssets', {})
    if not owners or {source for owner in owners.values() for source in owner.get('sources', [])} != COPY_SOURCE_PATHS:
        raise ValueError('Copy implementation chunk/source ownership is missing')
    origin = urlparse(proof.get('studioOrigin', ''))
    for path, owner in owners.items():
        if not path.startswith(COPY_ASSET_PREFIX) or proof.get('builtAssets', {}).get(path.removeprefix(COPY_ASSET_PREFIX)) != owner.get('sha256'):
            raise ValueError('Copy implementation chunk is not the normal built asset')
        if not any(urlparse(item.get('url', '')).path == path and
                   (urlparse(item.get('url', '')).scheme, urlparse(item.get('url', '')).netloc) == (origin.scheme, origin.netloc) and
                   item.get('sha256') == owner.get('sha256') for item in proof.get('servedAssets', [])):
            raise ValueError('Browser did not fetch the current copy implementation bytes at their owned URL')
    expected = {'copy activity data pairs': {'duplicate-whole', 'paste-whole', 'duplicate-member', 'paste-member'},
                'copy sequence activity data pairs': {'duplicate-whole', 'paste-whole', 'duplicate-member', 'paste-member'},
                'copy scoped and portable activity data': {'duplicate-sequence-scope-portable', 'paste-sequence-scope-portable'},
                'preserve external activity references on copy': {'consumer-only-valid', 'consumer-only-invalid'}}[title]
    if len(receipts) != len(expected) or {item.get('operation') for item in receipts} != expected:
        raise ValueError('Copy operation inventory incomplete')
    if any(not item.get('before') or not item.get('after') or not item.get('consumerId') for item in receipts):
        raise ValueError('Missing before/after copy identity')
    expected_runs = 2 if title in {'copy activity data pairs', 'copy sequence activity data pairs'} else 0 if composition == 'minimal' and title == 'copy scoped and portable activity data' else 1
    if len(runs) != expected_runs or any(item.get('committed') != item.get('expected') for item in runs):
        raise ValueError('Committed copy runtime inventory incomplete')
    for run in runs:
        ids = {execution['authoredActivityId']: execution['status'] for execution in run.get('executions', [])}
        if ids.get(run['consumerId']) != 'Completed' or run.get('producerId') and ids.get(run['producerId']) != 'Completed':
            raise ValueError('Copied producer/consumer did not complete')
        if title == 'copy activity data pairs' and (run.get('originalNeverRuns') is not True or 'producer' in ids):
            raise ValueError('Original producer ran; copied-start oracle did not prove the remap')
    if title == 'copy sequence activity data pairs':
        validate_sequence_pair_copy(proof)
    if title == 'preserve external activity references on copy':
        expected_boundaries = {'published-store-positive-control', 'resource-local', 'invalid-publication-no-write'}
        if len(boundaries) != 3 or {item.get('kind') for item in boundaries} != expected_boundaries:
            raise ValueError('Published positive, resource-local and invalid publication controls missing')
        published = next(item for item in boundaries if item['kind'] == 'published-store-positive-control')
        local = next(item for item in boundaries if item['kind'] == 'resource-local')
        refused = next(item for item in boundaries if item['kind'] == 'invalid-publication-no-write')
        valid_copy = next(item for item in receipts if item['operation'] == 'consumer-only-valid')
        invalid_copy = next(item for item in receipts if item['operation'] == 'consumer-only-invalid')
        definition_id = valid_copy.get('definitionId')
        if not definition_id or published.get('definitionId') != definition_id or runs[0].get('definitionId') != definition_id \
                or local.get('source') != definition_id or not published.get('rootNodeId') \
                or published['rootNodeId'] != valid_copy['before'].get('nodeId'):
            raise ValueError('Published store control does not belong to the copied source and its completed run')
        if not refused.get('definitionId') or refused['definitionId'] != invalid_copy.get('definitionId') \
                or refused['definitionId'] == definition_id:
            raise ValueError('Invalid publication control does not belong to the separate invalid copy')
        validate_publication_stores(published.get('stores'), require_published=True)
        validate_publication_stores(refused.get('before'))
        validate_publication_stores(refused.get('after'))
        if refused.get('before') != refused.get('after') or len(refused.get('before', {})) != 4:
            raise ValueError('Publication boundary stores changed or were not read')
    if composition == 'minimal':
        preserved = next((item for item in boundaries if item.get('kind') == 'missing-language'), None)
        if title == 'copy scoped and portable activity data' and (not preserved or not preserved.get('bodyPreservedWithRemappedIdentity')):
            raise ValueError('Missing-language body/identity preservation proof missing')
        observed = json.loads(Path(files['activity-data-proof.json']).read_text())
        if any(re.search(r'expressioneditors?\.(javascript|liquid)|javascript.expression|liquid.expression', url, re.I) for url in observed.get('scripts', [])):
            raise ValueError('Optional editor script requested in minimal copy proof')


def validate_sequence_pair_copy(proof):
    if proof.get('mode') != 'sequence-pairs':
        raise ValueError('Sequence pair proof came from a different designer mode')
    receipts = {item['operation']: item for item in proof['receipts']}
    navigations = proof.get('navigations', [])
    if len(navigations) != 4 or {(item.get('role'), item.get('stage')) for item in navigations} != \
            {(role, stage) for role in ('whole', 'member') for stage in ('copied', 'reopened')}:
        raise ValueError('Sequence whole/member copied and reopened source navigation is incomplete')
    for role, expression_type, projection, expected in [('whole', 'ActivityResult', 'Result', '00:00:01'),
                                                       ('member', 'ActivityResultMember', 'Customer', 'cust-1042')]:
        for operation in ('duplicate', 'paste'):
            receipt = receipts[operation + '-' + role]
            structure = receipt['after'].get('rootActivity', {}).get('structure', {})
            activities = structure.get('payload', {}).get('activities', [])
            producer = next((node for node in activities if node.get('nodeId') == receipt.get('producerId')), None)
            consumer = next((node for node in activities if node.get('nodeId') == receipt.get('consumerId')), None)
            if structure.get('kind') != 'elsa.sequence.structure' or not producer or not consumer:
                raise ValueError('Sequence copy receipt lacks its actual producer/consumer pair')
            bindings = [item.get('value', {}) for item in consumer.get('inputs', []) if item.get('referenceKey') == 'text']
            binding = bindings[0] if len(bindings) == 1 else {}
            source = binding.get('value', {})
            if binding.get('expressionType') != expression_type or source.get('producerNodeId') != receipt['producerId'] \
                    or source.get('projectionKey') != projection or source.get('isOptional') is not False \
                    or (role == 'member' and source.get('memberPath') != ['customerId']):
                raise ValueError('Sequence copied whole/member binding lost its intended producer or literal selection')
        pasted = receipts['paste-' + role]
        for navigation in (item for item in navigations if item['role'] == role):
            if navigation.get('definitionId') != pasted.get('definitionId') or navigation.get('consumerId') != pasted['consumerId'] \
                    or navigation.get('producerId') != pasted['producerId'] or navigation.get('selectedNodeId') != pasted['producerId']:
                raise ValueError('Sequence source navigation did not select the copied producer')
        runs = [item for item in proof['runtime'] if item.get('consumerId') == pasted['consumerId']]
        if len(runs) != 1 or runs[0].get('definitionId') != pasted.get('definitionId') \
                or runs[0].get('producerId') != pasted['producerId'] or runs[0].get('expected') != expected \
                or runs[0].get('committed') != expected:
            raise ValueError('Sequence committed runtime does not belong to its copied whole/member source')


def validate_publication_stores(stores, require_published=False):
    # These are the ownership columns read by the WAL-aware publication_snapshot.py oracle.
    owner_columns = {'elsa_runtime_workflow_executable': 'ContentJson',
                     'elsa_runtime_workflow_executable_source_reference': 'DefinitionId',
                     'elsa_publication_records': 'WorkflowDefinitionId',
                     'elsa_runtime_workflow_activation_slot': 'WorkflowDefinitionId'}
    if not isinstance(stores, dict) or set(stores) != set(owner_columns):
        raise ValueError('Publication control is missing an owning store/table')
    for table, owner_column in owner_columns.items():
        value = stores[table]
        if not isinstance(value, dict):
            raise ValueError('Publication control has malformed store evidence')
        store, columns, digest, count = (value.get(key) for key in ('store', 'columns', 'sha256', 'count'))
        if not isinstance(store, str) or not store or Path(store).is_absolute() or '..' in Path(store).parts \
                or not isinstance(columns, list) or not columns or any(not isinstance(column, str) or not column for column in columns) \
                or len(set(columns)) != len(columns) or owner_column not in columns \
                or not isinstance(digest, str) or not re.fullmatch(r'[0-9a-f]{64}', digest) \
                or type(count) is not int or count < (1 if require_published else 0):
            raise ValueError('Publication control has invalid schema, fingerprint or owned-row count')


def validate_activity_admission(selection):
    before = selection.get('before', {})
    admission = selection.get('admission', {})
    request, response = admission.get('request', {}), admission.get('response', {})
    same = ('definitionId', 'nodeId', 'inputKey', 'documentRevision', 'snapshot')
    query = request.get('query', {})
    if not before.get('documentRevision') or not before.get('snapshot') or any(request.get(key) != before.get(key) for key in same) or \
            response.get('state') not in {'supported', 'empty'} or response.get('documentRevision') != before['documentRevision'] or \
            not query.get('source') or query.get('memberPath') != selection['binding'].get('value', {}).get('memberPath') or \
            not any(source.get('binding') == selection['offered'] for source in response.get('analysis', {}).get('sources', [])):
        raise ValueError('Selection lacks its supported pre-edit admission; post-edit provenance is not admission')


def validate_minimal_composition(observation, title):
    features = observation.get('features', [])
    by_id = {feature['id']: feature for feature in features}
    if not features or len(by_id) != len(features):
        raise ValueError('Missing or ambiguous live minimal feature catalog')
    for required in ('WorkflowsPublishingApi', 'WorkflowDesignValidations'):
        if not by_id.get(required, {}).get('enabled') or not by_id.get(required, {}).get('runtimeAvailable'):
            raise ValueError('Missing rooted live minimal feature')
    reached = set()
    def visit(key):
        if key in reached:
            return
        feature = by_id.get(key, {})
        if not feature.get('runtimeAvailable') or not isinstance(feature.get('dependencies'), list):
            raise ValueError('Unresolved required minimal feature graph')
        reached.add(key)
        for dependency in feature['dependencies']:
            if not isinstance(dependency.get('id'), str) or type(dependency.get('optional')) is not bool:
                raise ValueError('Malformed required minimal feature graph')
            if not dependency['optional']:
                visit(dependency['id'])
    for feature in features:
        if feature.get('enabled'):
            visit(feature['id'])
    optional = {'HttpJavaScript', 'JavaScriptActivities', 'JavaScriptEndpoints', 'JavaScriptExpressions',
                'JavaScriptJintEngine', 'JavaScriptWorkflowsDesign', 'Liquid'}
    if reached & optional or sorted(reached) != observation.get('mandatoryClosure'):
        raise ValueError('Minimal resolved graph retains optional backend features or mismatches its closure')
    syntaxes, choices = observation.get('expressionTypes', []), observation.get('syntaxChoices', [])
    native_syntax = {'choose nested activity data': 'Activity output member',
                     'choose declared and scoped activity data': 'Variable member',
                     'choose data for activity outputs': 'Activity output…'}.get(title)
    if native_syntax is None or 'Literal' not in syntaxes or 'Literal' not in choices or native_syntax not in choices or \
            any(re.search('JavaScript|Liquid', name, re.I) for name in [*syntaxes, *choices]):
        raise ValueError('Minimal backend descriptors or installed Studio menu are missing/optional')


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
    if composition == 'minimal':
        validate_minimal_composition(proof.get('compositionObservation') or {}, title)
    selections = proof.get('selections', [])
    if any(item.get('binding') != item.get('offered') or not item.get('binding') for item in selections):
        raise ValueError('Selected binding differs from actual server offer')
    for selection in selections:
        validate_activity_admission(selection)
    runs = proof.get('runtime', [])
    if title == 'choose nested activity data':
        cancelled = [item for item in proof.get('transportControls', []) if item.get('kind') == 'cancelled']
        if len(cancelled) != 1 or not re.search('ERR_ABORTED|cancelled|canceled|aborted', cancelled[0].get('failure', ''), re.I) or \
                cancelled[0].get('request', {}).get('nodeId') != 'consumer' or cancelled[0].get('request', {}).get('inputKey') != 'text':
            raise ValueError('Actual browser cancellation evidence is missing')
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
               'DOTNET_CLI_HOME', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'LANG', 'LC_ALL',
               'DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE', 'DOTNET_USE_POLLING_FILE_WATCHER'}
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


def compose_shells(shells, studio_url, remove=(), seed=None):
    """A private shells.json: local origin routing, an optional API subset and an optional first-boot identity seed."""
    shells = copy.deepcopy(shells)
    features = shells['CShells']['Shells']['default']['Features']
    missing = [name for name in remove if name not in features]
    if missing:
        raise ValueError(f'Subset composition names features the combined shells.json no longer configures: {missing}')
    for name in remove:
        del features[name]
    features['FoundationIdentityAspNetCoreIdentity']['AllowedReturnUrlOrigins'] = [studio_url]
    if seed:
        # The real seeder creates this account (password store, IAM user, role, tenant membership) on first boot.
        features[IDENTITY_SEED_FEATURE].update(SeedAdminUserName=seed['user'], SeedAdminPassword=seed['password'],
                                               SeedAdminEmail=f"{seed['user']}@example.invalid", SeedAdminRoleName=seed['role'])
    return shells


def write_overlay(project, destination, studio_url, remove=(), seed=None):
    # Preserve normal module composition; only origin routing (and an explicit subset/seed) changes in private copies.
    settings = json.loads((project.parent / 'appsettings.json').read_text(encoding='utf-8-sig'))
    settings['Cors']['AllowedOrigins'] = [studio_url]
    shells = json.loads((project.parent / 'shells.json').read_text(encoding='utf-8-sig'))
    shells = compose_shells(shells, studio_url, remove, seed)
    if not remove and not seed:
        configure_dashboard_qualification(settings, shells)
    (destination / 'appsettings.json').write_text(json.dumps(settings))
    (destination / 'shells.json').write_text(json.dumps(shells))


def encode_identity_text(value):
    """Every Identity IAM text column is stored as base64 UTF-16LE (IdentityEntityFrameworkUtf16Codec), never as plain text."""
    return base64.b64encode(value.encode('utf-16-le')).decode('ascii')


def decode_identity_text(text):
    return base64.b64decode(text, validate=True).decode('utf-16-le')


def identity_set_payload(values):
    """IdentityApplicationSetCodec: an ordinal-sorted JSON array of base64 UTF-16LE values."""
    encoded = [encode_identity_text(value) for value in sorted(set(values))]
    return json.dumps(encoded, separators=(',', ':')).replace('+', '\\u002B')


def encode_identity_set(values):
    """A set column's stored value: the set payload, itself stored through the text-column codec."""
    return encode_identity_text(identity_set_payload(values))


def decode_identity_set(text):
    return sorted(decode_identity_text(value) for value in json.loads(decode_identity_text(text)))


def identity_store(root):
    stores = []
    for file in sorted(root.rglob('*.db')):
        try:
            # A plain connection: a read-only one cannot open a WAL store whose -shm sidecar is absent.
            with closing(sqlite3.connect(file)) as db:
                if db.execute("select 1 from sqlite_master where type = 'table' and name = 'identity_users'").fetchone():
                    stores.append(file)
        except sqlite3.DatabaseError:
            continue
    if len(stores) != 1:
        raise RuntimeError(f'Expected exactly one owned identity store under {root}, found {stores}')
    return stores[0]


def restrict_seeded_reader(root, user=RESTRICTED_USER, role=RESTRICTED_SEED_ROLE, permissions=RESTRICTED_PERMISSIONS):
    """Narrow the freshly seeded reader to exactly `permissions` and no roles, in the owned store, while no host runs.

    The account, password hash, IAM user, role and membership rows were written by the real Workbench seeder; this
    changes only their grant columns. The real claims projection then derives the session from them on sign-in.
    """
    store = identity_store(root)
    def one(db, sql, args, what):
        rows = db.execute(sql, args).fetchall()
        if len(rows) != 1:
            raise RuntimeError(f'Expected exactly one {what}, found {len(rows)}')
        return rows[0]
    with closing(sqlite3.connect(store, timeout=30)) as db:
        with db:
            if db.execute('select count(*) from identity_users').fetchone()[0] != 1:
                raise RuntimeError('The restricted reader must be the only account in a freshly seeded store')
            # Lookups compare stored (encoded) values; tenant and subject are passed back exactly as stored.
            key, tenant, subject, role_ids, direct = one(db, 'select Id, TenantId, UserId, RoleIdsJson, DirectPermissionsJson from identity_users where UserName = ?', (encode_identity_text(user),), 'seeded reader')
            role_key, role_id = one(db, 'select Id, RoleId from identity_roles where TenantId = ? and Name = ?', (tenant, encode_identity_text(role)), 'seed role')
            member_key, member_roles, member_permissions = one(db, 'select Id, RoleIdsJson, DirectPermissionsJson from identity_tenant_memberships where TenantId = ? and UserId = ?', (tenant, subject), 'tenant membership')
            before = {'userRoleIds': decode_identity_set(role_ids), 'userDirectPermissions': decode_identity_set(direct),
                      'membershipRoleIds': decode_identity_set(member_roles), 'membershipDirectPermissions': decode_identity_set(member_permissions)}
            role_id = decode_identity_text(role_id)
            if before != {'userRoleIds': [role_id], 'userDirectPermissions': [], 'membershipRoleIds': [role_id], 'membershipDirectPermissions': []}:
                raise RuntimeError(f'The seeded reader does not have the seeder shape: {before}')
            db.execute('update identity_users set RoleIdsJson = ?, DirectPermissionsJson = ? where Id = ?', (encode_identity_set([]), encode_identity_set(permissions), key))
            db.execute('update identity_tenant_memberships set RoleIdsJson = ?, DirectPermissionsJson = ? where Id = ?', (encode_identity_set([]), encode_identity_set([]), member_key))
            # The framework UserRole link the seeder materialized stays, but its role can no longer grant anything.
            db.execute('update identity_roles set PermissionsJson = ? where Id = ?', (encode_identity_set([]), role_key))
        _, _, _, role_ids, direct = one(db, 'select Id, TenantId, UserId, RoleIdsJson, DirectPermissionsJson from identity_users where Id = ?', (key,), 'seeded reader')
        _, member_roles, member_permissions = one(db, 'select Id, RoleIdsJson, DirectPermissionsJson from identity_tenant_memberships where Id = ?', (member_key,), 'tenant membership')
        (role_permissions,) = one(db, 'select PermissionsJson from identity_roles where Id = ?', (role_key,), 'seed role')
    after = {'userRoleIds': decode_identity_set(role_ids), 'userDirectPermissions': decode_identity_set(direct),
             'membershipRoleIds': decode_identity_set(member_roles), 'membershipDirectPermissions': decode_identity_set(member_permissions),
             'seedRolePermissions': decode_identity_set(role_permissions)}
    if after != {'userRoleIds': [], 'userDirectPermissions': sorted(permissions), 'membershipRoleIds': [], 'membershipDirectPermissions': [], 'seedRolePermissions': []}:
        raise RuntimeError(f'Restricted grants did not persist: {after}')
    return {'tenant': decode_identity_text(tenant), 'subject': decode_identity_text(subject), 'userName': user, 'seedRole': role, 'before': before, 'after': after,
            'authority': 'real Workbench first-boot seeder, then grant columns narrowed in the owned store before the acceptance boot',
            # The generated password stays out of this receipt and report.json; Playwright traces still record the sign-in.
            'passwordInReceipt': False}


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


def copy_implementation_assets(source, normal, diagnostics):
    """Map original reviewed sources to normal output chunks through a byte-identical hidden-map build."""
    owners, found = {}, set()
    for path in diagnostics.rglob('*.js.map'):
        mapping = json.loads(path.read_text())
        owned = [(index, expected) for index, item in enumerate(mapping.get('sources', []))
                 for expected in COPY_SOURCE_PATHS if item.replace('\\', '/').endswith('/' + expected)]
        if not owned:
            continue
        for index, expected in owned:
            contents = mapping.get('sourcesContent', [])
            if index >= len(contents) or contents[index] != (source / expected).read_text():
                raise ValueError(f'Diagnostic map does not contain the current reviewed source: {expected}')
        compiled = path.with_suffix('')
        relative = compiled.relative_to(diagnostics)
        current = normal / relative
        if not current.is_file() or current.read_bytes() != compiled.read_bytes():
            raise ValueError(f'Diagnostic owning chunk differs from the normal compiled bytes: {relative}')
        sources = sorted(expected for _, expected in owned)
        found.update(sources)
        owners[COPY_ASSET_PREFIX + relative.as_posix()] = {
            'sha256': hashlib.sha256(current.read_bytes()).hexdigest(), 'sources': sources,
            'sourceMapSha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'diagnosticMap': str(path.relative_to(diagnostics))}
    if found != COPY_SOURCE_PATHS:
        raise ValueError('Diagnostic maps do not identify both reviewed copy implementation sources')
    return owners


def run(output, journey=None, studio_root=None, composition='normal'):
    output.mkdir(parents=True, exist_ok=False)
    studio_root = (studio_root or ROOT).resolve()
    sha, dirty = source_state(ROOT)
    studio_sha, studio_dirty = source_state(studio_root)
    head = os.environ.get('UX_PR_HEAD_SHA', sha)
    selected = selected_journeys(journey, composition)
    fixture_built = False
    owned_processes = {}
    backend_project = ROOT / 'apps/Elsa.Workbench/Elsa.Workbench.csproj'
    report = {'sourceSha': sha, 'headSha': head, 'dirty': bool(dirty),
              'kind': 'composed-source', 'studioSourceSha': studio_sha, 'studioDirty': bool(studio_dirty), 'startedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
              'status': 'failed', 'stage': 'build', 'visualApproval': 'requires independent review',
              'journeys': sorted(selected), 'composition': composition, 'ownedPids': {}}
    try:
        if dirty or studio_dirty:
            raise ValueError('Commit source changes before acceptance; a dirty run cannot certify a revision')
        if not (supplies_pr_head(ROOT, sha, head) or supplies_pr_head(studio_root, studio_sha, head)):
            raise ValueError('The claimed PR head supplied neither observed source tree nor a verified CI merge parent')
        studio_project = studio_root / 'Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj'
        backend_command = ['dotnet', 'build', str(backend_project), '-p:RestoreLockedMode=true', '-p:ElsaDashboardQualification=true', '-m:1', '-nr:false', '-v:q', '-nologo']
        if selected & DATA_JOURNEYS:
            fixture = ROOT / 'e2e-tests/activity-data-selection/fixtures/fixture.targets'
            backend_command.append(f'-p:CustomAfterMicrosoftCommonTargets={fixture}')
            report['fixture'] = {str(file.relative_to(ROOT)): hashlib.sha256(file.read_bytes()).hexdigest()
                                 for file in (fixture, fixture.with_name('CustomerFixture.cs'))}
            fixture_built = True
        command(backend_command, output / 'backend-build.log')
        command(['pnpm', 'build'], output / 'frontend-build.log', cwd=studio_root / 'Studio')
        workflow_module = studio_root / 'Studio/src/essentials/Elsa.Studio.Workflows'
        workflow_assets = fingerprints(workflow_module / 'wwwroot/studio/modules/workflows')
        report['workflowSourceAssets'] = workflow_assets
        copy_assets = {}
        if selected & COPY_JOURNEYS:
            diagnostics = output / 'copy-asset-diagnostics'
            command(['pnpm', '--filter', '@elsa-workflows/studio-workflows', 'exec', 'vite', 'build', '--sourcemap', 'hidden', '--outDir', str(diagnostics)],
                    output / 'copy-asset-diagnostics.log', cwd=studio_root / 'Studio')
            copy_assets = copy_implementation_assets(workflow_module / 'Client/src', workflow_module / 'wwwroot/studio/modules/workflows', diagnostics)
            report['copyImplementationAssets'] = copy_assets
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

        command(['dotnet', 'build', str(studio_project), '-p:RestoreLockedMode=true', '-m:1', '-nr:false', '-v:q', '-nologo'], output / 'studio-build.log')
        with tempfile.TemporaryDirectory(prefix='elsa-ux-acceptance-') as temporary:
            data = Path(temporary)
            report['ownedRoot'] = str(data)
            env = dict(clean_environment(), DOTNET_PROCESSOR_COUNT='2', HOME=str(data / "home"), DOTNET_CLI_HOME=str(data / "home"),
                       **{"Logging__LogLevel__Microsoft.Hosting.Lifetime": "Information"})
            (data / "home").mkdir()
            # The combined composition serves every ordinary journey; subsets get their own owned hosts and stores.
            compositions = [('combined', ())] + [(name, tuple(item['remove'])) for name, item in SUBSET_COMPOSITIONS.items() if item['journeys'] & selected]
            hosts = []

            def configure_owned_host(item, seed=None):
                write_overlay(backend_project, item['backend'], item['studioUrl'], item['removedFeatures'], seed)
                # Activity-data modes belong only to the combined host. Never re-add Design to an API subset.
                if item['name'] == 'combined':
                    backend_shells = json.loads((item['backend'] / 'shells.json').read_text(encoding='utf-8-sig'))
                    studio_shells = json.loads((item['studio'] / 'shells.json').read_text(encoding='utf-8-sig'))
                    configure_activity_data(backend_shells, studio_shells, composition, selected)
                    (item['backend'] / 'shells.json').write_text(json.dumps(backend_shells))
                    (item['studio'] / 'shells.json').write_text(json.dumps(studio_shells))

            for name, remove in compositions:
                prefix = '' if name == 'combined' else f'{name}-'
                backend, studio = data / f'{prefix}workbench', data / f'{prefix}studio'
                prepare_host(backend_project, backend)
                prepare_host(studio_project, studio)
                backend_url = f'http://localhost:{free_port()}'
                studio_url = f'http://localhost:{free_port()}'
                item = {'name': name, 'prefix': prefix, 'backend': backend, 'studio': studio, 'backendUrl': backend_url, 'studioUrl': studio_url, 'removedFeatures': list(remove)}
                configure_owned_host(item)
                hosts.append(item)
            combined = hosts[0]
            report.update(stage='host-start', studioUrl=combined['studioUrl'], backendUrl=combined['backendUrl'],
                          hostProfile='ElsaDashboardQualification=true; private native principals; Copilot disabled',
                          modelEvidence='Native browser Weaver message submissions only; physical SDK calls not measured',
                          compositions={item['name']: {key: item[key] for key in ('backendUrl', 'studioUrl', 'removedFeatures')} for item in hosts})
            browser_env = dict(os.environ, UX_ACCEPTANCE_OWNED='1', ELSA_STUDIO_URL=combined['studioUrl'],
                               ELSA_WORKBENCH_URL=combined['backendUrl'], UX_BACKEND_URL=combined['backendUrl'],
                               UX_ACCEPTANCE_OUT=str(output), UX_SOURCE_SHA=sha, UX_STUDIO_SOURCE_SHA=studio_sha,
                               UX_ACTIVITY_DATA_COMPOSITION=composition, UX_ACCEPTANCE_OWNED_ROOT=str(data),
                               UX_WORKFLOW_ASSET_SHA256S=json.dumps(workflow_assets), UX_COPY_IMPLEMENTATION_ASSETS=json.dumps(copy_assets))
            for item in hosts[1:]:
                variable = item['name'].split('-')[0].upper()
                browser_env.update({f'UX_{variable}_STUDIO_URL': item['studioUrl'], f'UX_{variable}_BACKEND_URL': item['backendUrl']})
            if RESTRICTED_JOURNEY in selected:
                # Seed only the real reader, stop, narrow its grants, then restore the combined acceptance overlay.
                report['stage'] = 'restricted-identity'
                password = f'Ux-{secrets.token_urlsafe(18)}-9a'
                seed = {'user': RESTRICTED_USER, 'password': password, 'role': RESTRICTED_SEED_ROLE}
                configure_owned_host(combined, seed=seed)
                with host(combined['backend'], 'Elsa.Workbench.dll', combined['backendUrl'], '/health/ready', output / 'backend-restricted-seed.log', env) as seed_process:
                    owned_processes['restrictedSeedBackend'] = seed_process
                    report['ownedPids']['restrictedSeedBackend'] = seed_process.pid
                receipt = restrict_seeded_reader(combined['backend'])
                (output / 'restricted-identity.json').write_text(json.dumps(receipt, indent=2) + '\n')
                report['restrictedIdentity'] = receipt
                configure_owned_host(combined)
                browser_env.update(UX_RESTRICTED_USER=RESTRICTED_USER, UX_RESTRICTED_PASSWORD=password, UX_RESTRICTED_SUBJECT=receipt['subject'])
                report['stage'] = 'host-start'
            # Capture the actual final private configurations after reader seeding has been replaced.
            for item in hosts:
                backend_shells = json.loads((item['backend'] / 'shells.json').read_text(encoding='utf-8-sig'))
                studio_shells = json.loads((item['studio'] / 'shells.json').read_text(encoding='utf-8-sig'))
                details = report['compositions'][item['name']]
                details['features'] = {'backend': sorted(backend_shells['CShells']['Shells']['default']['Features']),
                                       'studio': sorted(studio_shells['CShells']['Shells']['Default']['Features'])}
                details['binaryAndConfigHashes'] = {'backend': fingerprints(item['backend']), 'studio': fingerprints(item['studio'])}
            # Preserve main's combined-host receipt keys while retaining each subset's independent provenance.
            report['composedFeatures'] = report['compositions']['combined']['features']
            report['binaryAndConfigHashes'] = report['compositions']['combined']['binaryAndConfigHashes']
            backend_env = env
            if selected == {'Double activity output binds to text'}:
                # Exercise a worker whose decimal separator is a comma.
                backend_env = dict(env, LANG='nl_NL.UTF-8', LC_ALL='nl_NL.UTF-8')
                report['backendLocale'] = backend_env['LC_ALL']
            with ExitStack() as owned:
                for item in hosts:
                    prefix = '' if item['name'] == 'combined' else f"{item['name']}-"
                    backend_process = owned.enter_context(host(item['backend'], 'Elsa.Workbench.dll', item['backendUrl'], '/__qualification/ready' if item['name'] == 'combined' else '/health/ready', output / f"{item['prefix']}backend.log", backend_env))
                    owned_processes[f'{prefix}backend'] = backend_process
                    report['ownedPids'][f'{prefix}backend'] = backend_process.pid
                    studio_process = owned.enter_context(host(item['studio'], 'Elsa.Studio.Web.dll', item['studioUrl'], '/studio-runtime.js', output / f"{item['prefix']}studio.log",
                                             dict(env, Studio__BackendBaseUrl=item['backendUrl'])))
                    owned_processes[f'{prefix}studio'] = studio_process
                    report['ownedPids'][f'{prefix}studio'] = studio_process.pid
                report['stage'] = 'journeys'
                browser_command = ['pnpm', 'exec', 'playwright', 'test', '--config', 'tests/acceptance/playwright.config.ts']
                if journey is not None:
                    # Playwright greps project/file/title together; anchor the exact final title.
                    browser_command.extend(['--grep', r'(?:^|\s)(?:' + '|'.join(re.escape(name) for name in sorted(selected)) + ')$'])
                command(browser_command, output / 'journeys.log', cwd=studio_root / 'Studio', env=browser_env)
                report['executed'] = verdict(json.loads((output / 'playwright.json').read_text()), output, selected, composition)
        report['hostExitCodes'] = {key: process.returncode for key, process in owned_processes.items()}
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
        report['hostExitCodes'] = {key: process.returncode for key, process in owned_processes.items()}
        if 'ownedRoot' in report:
            report['ownedRootRemoved'] = not Path(report['ownedRoot']).exists()
        # TemporaryDirectory and nested host contexts have ended before removing the opt-in fixture build.
        if fixture_built:
            try:
                command(['dotnet', 'build', str(backend_project), '-p:RestoreLockedMode=true', '-p:ElsaDashboardQualification=false', '-m:1', '-nr:false', '-v:q', '-nologo'], output / 'fixture-cleanup-build.log')
                dll = backend_project.parent / 'bin/Debug/net10.0/Elsa.Workbench.dll'
                markers = ('DashboardQualificationBootstrap', 'QualificationPrincipals', 'DashboardQualificationServiceExclusions', 'ActivityDataSelectionProof')
                marker_absence = {marker: marker.encode() not in dll.read_bytes() and marker.encode('utf-16-le') not in dll.read_bytes() for marker in markers}
                if not all(marker_absence.values()):
                    raise ValueError('Default Workbench still contains an opt-in customer fixture marker')
                report['fixtureCleanup'] = {'status': 'passed', 'workbenchSha256': hashlib.sha256(dll.read_bytes()).hexdigest(), 'markerAbsent': True, 'markerAbsence': marker_absence}
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
    parser.add_argument('--journey', action='append', choices=sorted(JOURNEYS), help='Repeat for exact complete journeys across every project that owns them; default requires every normal journey')
    parser.add_argument('--composition', choices=sorted(COMPOSITIONS), default='normal', help='Owned activity-data host composition; nonnormal modes require explicit journeys')
    args = parser.parse_args()
    def terminate(_signum, _frame):
        raise InterruptedError('Acceptance interrupted; owned hosts will be stopped')
    signal.signal(signal.SIGTERM, terminate)
    run(args.output.resolve(), args.journey, args.studio_root, args.composition)

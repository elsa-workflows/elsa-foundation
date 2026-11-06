#!/usr/bin/env python3
"""Build and inspect real source hosts, owning every process and temporary store."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import socket
import subprocess
import tempfile
import threading
import time
import secrets
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = {'desktop-light', 'desktop-dark', 'narrow-light', 'narrow-dark'}
TRIGGER_PROJECTS = PROJECTS | {'compact-light', 'desktop-dim', 'desktop-high-contrast'}
CAPTURES = {
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
TRIGGER_CAPTURES = {
    'standalone trigger management': ['triggers-reference', 'triggers-empty', 'triggers-populated', 'triggers-no-matches', 'trigger-create', 'trigger-validation', 'trigger-details', 'trigger-no-subscribers'],
    'standalone trigger associations and inputs': ['trigger-subscriptions', 'workflow-live-triggers', 'subscription-mapping', 'mapping-preview', 'mapping-errors', 'target-incompatible', 'subscription-conflict'],
    'standalone event and schedule starts': ['event-delivery', 'schedule-next-firing', 'shared-change-review', 'trigger-paused', 'schedule-delivery', 'subscriber-runs'],
    'standalone HTTP modes': ['http-workflow-response-mode', 'http-response-owner-error', 'http-fanout-mode', 'http-admitted', 'http-policy-refused', 'http-partially-admitted', 'http-zero-accepted', 'http-rejected', 'http-route-conflict', 'http-transport-outcome',
        'http-embedded-owner-conflict', 'http-standalone-owner-conflict', 'http-timeout-retained', 'http-client-disconnected'],
    'standalone delivery lifecycle and recovery': ['delivery-admission', 'delivery-subscriber-outcomes', 'delivery-captured-version', 'target-unpublished', 'pending-cancel-review', 'pending-cancelled', 'delivery-dispatch-committed', 'cancel-dispatch-committed', 'delivery-partial-failure', 'delivery-recovered', 'delivery-concurrent-claims', 'delivery-concurrent-peer-started'],
    'standalone genuine BPMN inputs': ['bpmn-worker-input-mapping', 'bpmn-worker-input-validation', 'bpmn-worker-input-reopened', 'bpmn-declared-input-run', 'bpmn-worker-input-consumed'],
    'standalone access and availability': ['triggers-read-only', 'triggers-denied', 'triggers-other-tenant', 'trigger-source-owned',
        'trigger-backend-scope-switch', 'triggers-scope-response-isolated', 'trigger-module-unavailable', 'trigger-capability-unavailable', 'trigger-contract-incompatible',
        'trigger-optional-editor-installed', 'trigger-optional-editor-unavailable', 'triggers-loading',
        'triggers-refresh-error', 'triggers-load-error', 'triggers-recovered'],
}


class IncompleteAcceptanceError(ValueError):
    """Ordinary paths ran, but their evidence cannot establish the complete feature contract."""


def inventory(suite):
    existing = {(project, journey) for project in PROJECTS for journey in JOURNEYS}
    triggers = {(project, journey) for project in TRIGGER_PROJECTS for journey in TRIGGER_CAPTURES}
    return existing if suite == 'existing' else triggers if suite == 'standalone-triggers' else existing | triggers


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


def verdict(report, artifacts, suite='existing', source_sha=None):
    """An exit code alone cannot certify that every intended journey actually ran."""
    rows = []
    def walk(suites):
        for suite in suites:
            for spec in suite.get('specs', []):
                for test in spec.get('tests', []):
                    rows.append((test.get('projectName'), spec.get('title'), test))
            walk(suite.get('suites', []))
    walk(report.get('suites', []))
    expected = inventory(suite)
    actual = [(project, title) for project, title, _ in rows]
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError(f'Incomplete acceptance inventory: expected {sorted(expected)}, got {actual}')
    if report.get('errors'):
        raise ValueError('Playwright reported runner errors')
    incomplete = []
    for project, title, test in rows:
        results = test.get('results', [])
        if test.get('expectedStatus') != 'passed' or test.get('status') != 'expected' or len(results) != 1 or results[0].get('status') != 'passed':
            raise ValueError(f'{project} / {title}: not a single successful execution')
        attachments = results[0].get('attachments', [])
        names = {item.get('name') for item in attachments}
        captures = TRIGGER_CAPTURES[title] if title in TRIGGER_CAPTURES else CAPTURES[title]
        required = {'trace'} | {f'{capture}.{suffix}' for capture in captures for suffix in ('png', 'aria.yml')}
        if title in TRIGGER_CAPTURES:
            required.add('standalone-evidence.json')
            evidence_attachment = next((item for item in attachments if item.get('name') == 'standalone-evidence.json'), None)
            if not evidence_attachment or not evidence_attachment.get('path'):
                raise ValueError(f'{project} / {title}: missing request/result and coverage evidence')
            evidence_path = Path(evidence_attachment['path']).resolve()
            if not evidence_path.is_relative_to(artifacts.resolve()) or not evidence_path.is_file():
                raise ValueError(f'{project} / {title}: unsafe coverage evidence')
            evidence = json.loads(evidence_path.read_text())
            if evidence.get('kind') != 'composed-source' or evidence.get('journey') != title or evidence.get('project') != project:
                raise ValueError(f'{project} / {title}: coverage identity differs from the executed journey')
            if not evidence.get('sourceSha') or source_sha is not None and evidence.get('sourceSha') != source_sha:
                raise ValueError(f'{project} / {title}: coverage source identity differs')
            if not evidence.get('requests') or not evidence.get('captures') or not evidence.get('observed'):
                raise ValueError(f'{project} / {title}: no real requests or inspected captures were retained')
            if not isinstance(evidence.get('unverified'), list):
                raise ValueError(f'{project} / {title}: explicit uncovered-state inventory is missing')
            observed_captures = {item.get('name') for item in evidence['captures']}
            if len(observed_captures) != len(evidence['captures']):
                raise ValueError(f'{project} / {title}: duplicate capture identities')
            for item in evidence['captures']:
                expected_width = 320 if project == 'compact-light' else 360 if project.startswith('narrow-') else 1440
                expected_mode = project.removeprefix('desktop-').removeprefix('narrow-').removeprefix('compact-')
                identity_boundary = item.get('name') == 'triggers-denied' and item.get('appearanceScope') == 'identity-login' and item.get('palette') == 'identity-login' and item.get('mode') == 'host-controlled'
                if item.get('viewport', {}).get('width') != expected_width or not identity_boundary and (item.get('mode') != expected_mode or not item.get('palette')):
                    raise ValueError(f'{project} / {title}: missing or incorrect palette/mode/viewport for {item.get("name")}')
                if not {f'{item["name"]}.png', f'{item["name"]}.aria.yml'} <= names:
                    raise ValueError(f'{project} / {title}: capture metadata has no screenshot/snapshot pair')
            missing_capture_facts = set(captures) - observed_captures
            if missing_capture_facts:
                incomplete.append(f'{project} / {title}: unobserved required states {sorted(missing_capture_facts)}')
            for gap in evidence.get('unverified', []):
                if not gap.get('state') or not gap.get('reason'):
                    raise ValueError(f'{project} / {title}: an unexplained coverage exclusion was recorded')
                incomplete.append(f'{project} / {title}: {gap["state"]}: {gap["reason"]}')
            if not required <= names:
                incomplete.append(f'{project} / {title}: missing observed captures {sorted(required - names)}')
        elif not required <= names:
            raise ValueError(f'{project} / {title}: missing artifacts {sorted(required - names)}')
        for item in attachments:
            if not item.get('path') and item.get('name') not in required:
                continue
            file = Path(item.get('path', '')).resolve()
            if not file.is_relative_to(artifacts.resolve()) or not file.is_file() or not file.stat().st_size:
                raise ValueError(f'{project} / {title}: missing or unsafe artifact {item.get("name")}')
    if incomplete:
        raise IncompleteAcceptanceError('Incomplete standalone trigger acceptance:\n' + '\n'.join(incomplete))
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


class OwnedBackend:
    """A file mailbox controls only this runner's process and private feature configuration."""
    def __init__(self, directory, url, output, env, setup_dll=None, scope_peer=None):
        self.directory, self.url, self.output, self.env = directory, url, output, env
        self.original = (directory / 'shells.json').read_text()
        self.mailbox = directory.parent / 'host-control'
        self.mailbox.mkdir()
        self.token = secrets.token_hex(24)
        self.setup_dll, self.source_owned_id = setup_dll, None
        self.process = self.context = None
        self.generation = 0
        self.receipts = []
        self.studio_process = self.studio_context = None
        self.studio_generation = 0
        self.boundary_trigger_id = None
        self.fault_trigger_id = self.fault_mode = None
        self.scope_peer = scope_peer
        self.peer_process = self.peer_context = None
        self.peer_generation = 0
        self.concurrent_process = self.concurrent_context = None
        self.concurrent_directory = self.directory.parent / 'workbench-concurrent-node'
        self.concurrent_url = None
        self.concurrent_generation = 0
        self.closing = threading.Event()
        self.thread = threading.Thread(target=self.serve, name='owned-ux-backend', daemon=True)

    def start(self):
        if self.process is not None:
            raise ValueError('The owned backend is already running')
        self.generation += 1
        self.context = host(self.directory, 'Elsa.Workbench.dll', self.url, '/health/ready',
                            self.output / f'backend-{self.generation}.log', self.env)
        self.process = self.context.__enter__()

    def stop(self):
        if self.process is not None:
            # TERM cannot drain a suspended process until it is continued.
            try:
                os.killpg(self.process.pid, signal.SIGCONT)
            except ProcessLookupError:
                pass
            self.context.__exit__(None, None, None)
            self.process = self.context = None

    def kill(self):
        if self.process is None:
            raise ValueError('The owned backend is not running')
        try:
            os.killpg(self.process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        self.process.wait(timeout=10)
        self.stop()

    def configure_boundary(self, node, *, blocking):
        shells = json.loads(self.original)
        shells['CShells']['Shells']['default']['Features']['AcceptanceTriggerDispatchBoundary'] = {
            'GateDirectory': 'dispatch-boundary', 'OwnerToken': self.token, 'NodeIdentity': node,
            'SourceRevision': self.env['UX_SOURCE_SHA'], 'BlockAfterFirstSeal': blocking,
            'TriggerId': self.boundary_trigger_id}
        (self.directory / 'shells.json').write_text(json.dumps(shells))
        (self.directory / '.ux-acceptance-owner.json').write_text(json.dumps({'ownerToken': self.token, 'dispatchFixture': True}))

    def boundary_marker(self, node):
        path = self.directory / 'dispatch-boundary' / f'dispatch-{node}.json'
        return json.loads(path.read_text()) if path.exists() else None

    def fault_marker(self):
        path = self.directory / 'trigger-fault' / 'fault-fault-A.json'
        marker = json.loads(path.read_text()) if path.exists() else None
        if marker and (marker.get('processId') != self.process.pid or marker.get('sourceRevision') != self.env['UX_SOURCE_SHA'] or marker.get('triggerId') != self.fault_trigger_id):
            raise ValueError('The fault marker does not belong to this exact owned process/source/trigger')
        return marker

    def start_studio(self):
        self.studio_generation += 1
        self.studio_context = host(self.studio_directory, 'Elsa.Studio.Web.dll', self.studio_url, '/studio-runtime.js',
                                   self.output / f'studio-{self.studio_generation}.log', self.studio_env)
        self.studio_process = self.studio_context.__enter__()

    def stop_studio(self):
        if self.studio_process is not None:
            self.studio_context.__exit__(None, None, None)
            self.studio_process = self.studio_context = None

    def stop_scope_peer(self):
        if self.peer_process is not None:
            self.peer_context.__exit__(None, None, None)
            self.peer_process = self.peer_context = None

    def stop_concurrent_node(self):
        if self.concurrent_process is not None:
            self.concurrent_context.__exit__(None, None, None)
            self.concurrent_process = self.concurrent_context = None

    def inspect_concurrent_node(self):
        marker = self.concurrent_directory / 'dispatch-boundary' / 'dispatch-B.json'
        return {'primaryProcessId': self.process.pid if self.process else None,
            'peerProcessId': self.concurrent_process.pid if self.concurrent_process else None,
            'primaryAlive': self.process is not None and self.process.poll() is None,
            'peerAlive': self.concurrent_process is not None and self.concurrent_process.poll() is None,
            'peerUrl': self.concurrent_url, 'sourceSha': self.env['UX_SOURCE_SHA'], 'runtimeAndDesignStoresShared': True,
            'primaryBoundary': self.boundary_marker('A'), 'peerBoundary': json.loads(marker.read_text()) if marker.exists() else None}

    def start_concurrent_node(self):
        if self.concurrent_process is not None or self.process is None or self.boundary_trigger_id is None:
            raise ValueError('Concurrent qualification requires one running owned primary and its selected source boundary')
        directory = self.concurrent_directory
        if not directory.exists():
            prepare_host(ROOT / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj', directory)
        settings = json.loads((self.directory / 'appsettings.json').read_text())
        connection = f'Data Source={self.directory / "elsa.db"}'
        settings['ConnectionStrings']['Elsa'] = connection
        (directory / 'appsettings.json').write_text(json.dumps(settings))
        shells = json.loads(self.original)
        features = shells['CShells']['Shells']['default']['Features']
        for name in ('WorkflowsRuntimeEntityFrameworkCore', 'WorkflowsDesignEntityFrameworkCore', 'ActivitiesDesignEntityFrameworkCore',
                     'WorkflowsPublishingEntityFrameworkCore', 'IdentityIamEntityFrameworkCore',
                     'FoundationIdentityAspNetCoreIdentityEntityFrameworkCore', 'IdentityProviderConfigurationEntityFrameworkCore'):
            if name in features:
                features[name]['ConnectionString'] = connection
        features['AcceptanceTriggerDispatchBoundary'] = {'GateDirectory': 'dispatch-boundary', 'OwnerToken': self.token,
            'NodeIdentity': 'B', 'SourceRevision': self.env['UX_SOURCE_SHA'], 'BlockAfterFirstSeal': False, 'TriggerId': self.boundary_trigger_id}
        (directory / 'shells.json').write_text(json.dumps(shells))
        (directory / '.ux-acceptance-owner.json').write_text(json.dumps({'ownerToken': self.token, 'dispatchFixture': True}))
        gates = directory / 'dispatch-boundary'
        gates.mkdir(exist_ok=True)
        for name in ('dispatch-B.json', 'release-B.json'):
            (gates / name).unlink(missing_ok=True)
        shutil.copytree(self.directory / 'packages', directory / 'packages', dirs_exist_ok=True)
        self.concurrent_url = f'http://localhost:{free_port()}'
        self.concurrent_generation += 1
        self.concurrent_context = host(directory, 'Elsa.Workbench.dll', self.concurrent_url, '/health/ready',
            self.output / f'concurrent-node-{self.concurrent_generation}.log', self.env)
        self.concurrent_process = self.concurrent_context.__enter__()

    @contextmanager
    def studio(self, directory, url, env):
        self.studio_directory, self.studio_url, self.studio_env = directory, url, env
        self.studio_original = (directory / 'shells.json').read_text()
        self.studio_settings_original = (directory / 'appsettings.json').read_text()
        self.start_studio()
        try:
            yield
        finally:
            self.stop_studio()

    def execute(self, request):
        if request.get('token') != self.token:
            raise ValueError('The host-control request does not belong to this runner')
        action = request.get('action')
        before = self.process.pid if self.process else None
        setup = None
        boundary = None
        recovery = None
        peer = None
        concurrent = None
        fault = None
        if action in ('pause', 'resume'):
            if self.process is None or self.process.poll() is not None:
                raise ValueError('The owned backend is not running')
            os.killpg(self.process.pid, signal.SIGSTOP if action == 'pause' else signal.SIGCONT)
        elif action == 'stop':
            self.stop()
        elif action == 'arm-trigger-fault':
            trigger_id, mode = request.get('triggerId'), request.get('faultMode')
            if not isinstance(trigger_id, str) or not trigger_id or len(trigger_id) > 128 or mode not in ('SourceWriteRefused', 'DispatchShed', 'ResponseWriteFailed'):
                raise ValueError('The fault requires one exact real trigger and a supported controlled port failure')
            request_path = request.get('requestPath')
            if mode == 'ResponseWriteFailed' and (not isinstance(request_path, str) or not request_path.startswith('/workflows/http/') or '?' in request_path or len(request_path) > 2048):
                raise ValueError('Response-write fault requires the exact anonymous owned source route')
            subscription_id = request.get('subscriptionId')
            if subscription_id is not None and (not isinstance(subscription_id, str) or not subscription_id or len(subscription_id) > 128):
                raise ValueError('The optional shed recipient must be one exact real subscription')
            self.stop()
            self.fault_trigger_id, self.fault_mode = trigger_id, mode
            directory = self.directory / 'trigger-fault'
            directory.mkdir(exist_ok=True)
            for name in ('fault-fault-A.json', 'release-fault-A.json'):
                (directory / name).unlink(missing_ok=True)
            shells = json.loads(self.original)
            shells['CShells']['Shells']['default']['Features']['AcceptanceTriggerFaultBoundary'] = {
                'GateDirectory': 'trigger-fault', 'OwnerToken': self.token, 'NodeIdentity': 'fault-A',
                'SourceRevision': self.env['UX_SOURCE_SHA'], 'TriggerId': trigger_id, 'FaultMode': mode, 'SubscriptionId': subscription_id,
                'RequestPath': request_path}
            (self.directory / 'shells.json').write_text(json.dumps(shells))
            (self.directory / '.ux-acceptance-owner.json').write_text(json.dumps({'ownerToken': self.token, 'faultFixture': True}))
            self.start()
        elif action == 'inspect-trigger-fault':
            fault = self.fault_marker()
        elif action == 'release-trigger-fault':
            fault = self.fault_marker()
            if fault is None:
                raise ValueError('A real controlled public-port fault must have occurred before release')
            path = self.directory / 'trigger-fault' / 'release-fault-A.json'
            temporary = path.with_suffix('.tmp')
            temporary.write_text(json.dumps({'ownerToken': self.token, 'bootId': fault['bootId'], 'triggerId': self.fault_trigger_id}))
            temporary.replace(path)
            if self.fault_mode == 'DispatchShed':
                # The accepted source retains its real source-claim lease after the failed enqueue attempt.
                # Outlast any last renewal naturally; the browser keeps its original 45s recovery assertions.
                deadline = time.monotonic() + 61
                while time.monotonic() < deadline:
                    time.sleep(max(.001, min(.25, deadline - time.monotonic())))
        elif action == 'start-scope-peer':
            if self.scope_peer is None:
                raise ValueError('This source run has no owned peer backend composition')
            if self.peer_process is None:
                peer_directory, peer_url = self.scope_peer
                self.peer_generation += 1
                self.peer_context = host(peer_directory, 'Elsa.Workbench.dll', peer_url, '/health/ready', self.output / f'scope-peer-{self.peer_generation}.log', self.env)
                self.peer_process = self.peer_context.__enter__()
            peer = {'url': self.scope_peer[1], 'processId': self.peer_process.pid, 'runtimeAndDesignStoresIsolated': True,
                'identityStoreSharedWithOwnedPrimary': True, 'sourceSha': self.env['UX_SOURCE_SHA']}
        elif action == 'stop-scope-peer':
            self.stop_scope_peer()
        elif action == 'start-concurrent-node':
            self.start_concurrent_node()
            concurrent = self.inspect_concurrent_node()
        elif action == 'inspect-concurrent-node':
            concurrent = self.inspect_concurrent_node()
        elif action == 'stop-concurrent-node':
            concurrent = self.inspect_concurrent_node()
            self.stop_concurrent_node()
        elif action == 'release-dispatch-boundary':
            boundary = self.boundary_marker('A')
            if not boundary or self.process is None or boundary.get('processId') != self.process.pid:
                raise ValueError('The exact live owned boundary must exist before release')
            path = self.directory / 'dispatch-boundary' / 'release-A.json'
            temporary = path.with_suffix('.tmp')
            temporary.write_text(json.dumps({'ownerToken': self.token, 'bootId': boundary['bootId'], 'deliveryId': boundary['deliveryId'],
                'recipientId': boundary['recipientId'], 'sealFingerprint': boundary['sealFingerprint']}))
            temporary.replace(path)
        elif action == 'arm-dispatch-boundary':
            trigger_id = request.get('triggerId')
            if not isinstance(trigger_id, str) or not trigger_id or len(trigger_id) > 128:
                raise ValueError('A real owned trigger identity is required for the dispatch boundary')
            self.stop()
            self.boundary_trigger_id = trigger_id
            directory = self.directory / 'dispatch-boundary'
            directory.mkdir(exist_ok=True)
            for name in ('dispatch-A.json', 'dispatch-B.json', 'release-A.json', 'release-B.json'):
                (directory / name).unlink(missing_ok=True)
            (self.mailbox / 'peer-ready.json').unlink(missing_ok=True)
            self.configure_boundary('A', blocking=True)
            self.start()
        elif action == 'await-dispatch-boundary':
            deadline = time.monotonic() + 45
            while not (boundary := self.boundary_marker('A')):
                if time.monotonic() >= deadline:
                    raise TimeoutError('The real protected dispatch boundary was not observed')
                time.sleep(.1)
            if boundary.get('processId') != self.process.pid or boundary.get('sourceRevision') != self.env['UX_SOURCE_SHA']:
                raise ValueError('The dispatch marker does not belong to the running source process')
        elif action == 'recover-on-peer':
            boundary = self.boundary_marker('A')
            if not boundary or self.process is None or boundary.get('processId') != self.process.pid:
                raise ValueError('Recovery requires the exact live owned process that established the dispatch seal')
            self.kill()
            killed_at = datetime.now(timezone.utc).isoformat()
            # A may renew its real lease while blocked. Waiting its complete production visibility interval
            # after death outlasts every such renewal without changing a clock, claim or stored lease.
            deadline = time.monotonic() + 61
            self.configure_boundary('B', blocking=False)
            self.start()
            ready = {'previousProcessId': before, 'processId': self.process.pid, 'previousBootId': boundary['bootId'],
                'nodeIdentity': 'B', 'sourceSha': self.env['UX_SOURCE_SHA'], 'state': 'AwaitingOriginalLeaseExpiry',
                'killedAt': killed_at, 'storePreserved': True}
            temporary = self.mailbox / 'peer-ready.tmp'
            temporary.write_text(json.dumps(ready))
            temporary.replace(self.mailbox / 'peer-ready.json')
            while time.monotonic() < deadline:
                time.sleep(max(.001, min(.25, deadline - time.monotonic())))
            recovery = {'previousNodeIdentity': 'A', 'nodeIdentity': 'B', 'storeDirectory': str(self.directory),
                'killedAt': killed_at, 'stopSignal': 'SIGKILL', 'naturalLeaseWaitSeconds': 61,
                'leaseMutated': False, 'clockChanged': False, 'previousBoundary': boundary, 'peerBoundary': self.boundary_marker('B')}
        elif action == 'inspect-dispatch-boundary':
            boundary = {'previous': self.boundary_marker('A'), 'peer': self.boundary_marker('B')}
        elif action in ('setup-access', 'cleanup-access'):
            if self.setup_dll is None:
                raise ValueError('The acceptance setup executable was not built')
            self.stop()
            marker = self.directory / '.ux-acceptance-owner.json'
            marker.write_text(json.dumps({'ownerToken': self.token, 'backendStopped': True}))
            try:
                completed = subprocess.run(['dotnet', str(self.setup_dll)], cwd=self.directory, env=self.env,
                    input=json.dumps({'contentRoot': str(self.directory), 'ownerToken': self.token,
                                      'action': 'seed' if action == 'setup-access' else 'cleanup', 'resourceId': self.source_owned_id}),
                    text=True, capture_output=True, timeout=60)
                if completed.returncode:
                    (self.output / 'access-setup-error.log').write_text(completed.stderr)
                    raise RuntimeError('The real access setup failed; see access-setup-error.log')
                setup = json.loads(completed.stdout)
                self.source_owned_id = setup['sourceOwnedTriggerId'] if action == 'setup-access' else None
            finally:
                marker.unlink(missing_ok=True)
                self.start()
        elif action in ('without-optional-editor', 'restore-studio-features', 'independent-backend-auth'):
            if self.studio_process is None:
                raise ValueError('The runner-owned Studio is not running')
            previous_studio = self.studio_process.pid
            self.stop_studio()
            shells = json.loads(self.studio_original)
            if action == 'without-optional-editor':
                shells['CShells']['Shells']['Default']['Features'].pop('AcceptanceTriggerEditor', None)
            (self.studio_directory / 'shells.json').write_text(json.dumps(shells))
            settings = json.loads(self.studio_settings_original)
            if action == 'independent-backend-auth':
                settings['Studio']['Auth']['Enabled'] = False
            (self.studio_directory / 'appsettings.json').write_text(json.dumps(settings))
            self.start_studio()
            setup = {'previousStudioProcessId': previous_studio, 'studioProcessId': self.studio_process.pid}
        elif action in ('start', 'restart', 'without-schedule-module', 'without-trigger-capability', 'unsupported-trigger-contract', 'restore-features'):
            self.stop()
            if action in ('without-schedule-module', 'without-trigger-capability', 'unsupported-trigger-contract', 'restore-features'):
                shells = json.loads(self.original)
                features = shells['CShells']['Shells']['default']['Features']
                if action == 'without-schedule-module':
                    for name in ('StandaloneScheduleTriggerAuthoring', 'StandaloneScheduleTriggers'):
                        features.pop(name, None)
                elif action in ('without-trigger-capability', 'unsupported-trigger-contract'):
                    features.pop('StandaloneTriggersApi', None)
                    if action == 'unsupported-trigger-contract':
                        features['AcceptanceUnsupportedStandaloneContract'] = {}
                (self.directory / 'shells.json').write_text(json.dumps(shells))
            self.start()
        else:
            raise ValueError('Unsupported owned-host operation')
        receipt = {'action': action, 'previousProcessId': before, 'processId': self.process.pid if self.process else None,
                   'generation': self.generation, 'sourceSha': self.env.get('UX_SOURCE_SHA'),
                   'completedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()), 'storePreserved': True}
        if setup is not None:
            receipt['setup'] = setup
        if boundary is not None:
            receipt['boundary'] = boundary
        if recovery is not None:
            receipt['recovery'] = recovery
        if peer is not None:
            receipt['scopePeer'] = peer
        if concurrent is not None:
            receipt['concurrent'] = concurrent
        if fault is not None:
            receipt['fault'] = fault
        self.receipts.append(receipt)
        (self.output / 'owned-host-lifecycle.json').write_text(json.dumps(self.receipts, indent=2))
        return receipt

    def serve(self):
        while not self.closing.wait(.05):
            for path in sorted(self.mailbox.glob('*.request.json')):
                try:
                    result = {'ok': True, 'receipt': self.execute(json.loads(path.read_text()))}
                except Exception as error:
                    result = {'ok': False, 'error': str(error)}
                response = path.with_name(path.name.replace('.request.json', '.response.json'))
                temporary = response.with_suffix('.tmp')
                temporary.write_text(json.dumps(result))
                temporary.replace(response)
                path.unlink()

    def __enter__(self):
        self.start()
        self.thread.start()
        return self

    def __exit__(self, *_):
        self.closing.set()
        self.thread.join(timeout=195)
        if self.thread.is_alive():
            raise RuntimeError('Owned backend lifecycle operation failed to finish')
        self.stop_scope_peer()
        self.stop_concurrent_node()
        self.stop()


def run(output, suite='all'):
    output.mkdir(parents=True, exist_ok=False)
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    dirty = subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()
    report = {'sourceSha': sha, 'headSha': os.environ.get('UX_PR_HEAD_SHA', sha), 'dirty': bool(dirty),
              'kind': 'composed-source', 'startedAt': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
              'status': 'failed', 'stage': 'build', 'suite': suite,
              'expectedExecutions': len(inventory(suite)), 'visualApproval': 'requires independent review'}
    try:
        if dirty:
            raise ValueError('Commit source changes before acceptance; a dirty run cannot certify a revision')
        backend_project = ROOT / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj'
        studio_project = ROOT / 'Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj'
        command(['dotnet', 'build', str(backend_project), '-p:RestoreLockedMode=true', '-v:q', '-nologo'], output / 'backend-build.log')
        command(['pnpm', 'build'], output / 'frontend-build.log', cwd=ROOT / 'Studio')
        command(['dotnet', 'build', str(studio_project), '-p:RestoreLockedMode=true', '-v:q', '-nologo'], output / 'studio-build.log')
        setup_dll = None
        if suite != 'existing':
            setup_project = ROOT / 'tools/ux-acceptance/setup/Elsa.UxAcceptance.Setup.csproj'
            command(['dotnet', 'build', str(setup_project), '-p:RestoreLockedMode=true', '-v:q', '-nologo'], output / 'access-setup-build.log')
            setup_dll = setup_project.parent / 'bin/Debug/net10.0/Elsa.UxAcceptance.Setup.dll'
            command(['pnpm', 'exec', 'tsc', '--project', 'tests/acceptance/extensions/TriggerEditor/tsconfig.json'],
                    output / 'extension-consumer-types.log', cwd=ROOT / 'Studio')
            extension_feed = output / 'extension-feed'
            extension_feed.mkdir()
            for name, package in [('TriggerAuthoring', 'Acme.TriggerAuthoring'), ('TriggerEditor', 'Acme.TriggerEditor')]:
                project = ROOT / f'Studio/tests/acceptance/extensions/{name}/{package}.csproj'
                command(['dotnet', 'pack', str(project), '-p:RestoreLockedMode=true', '-o', str(extension_feed), '-v:q', '-nologo'], output / f'{name}-pack.log')
            report['extensionConsumer'] = {'category': 'independent-source-consumer', 'publishedPackageCompatibility': False,
                'sourceSha': sha, 'archives': [{'file': path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
                                             for path in sorted(extension_feed.glob('*.nupkg'))]}
        with tempfile.TemporaryDirectory(prefix='elsa-ux-acceptance-') as temporary:
            data = Path(temporary)
            backend, studio = data / 'workbench', data / 'studio'
            prepare_host(backend_project, backend)
            prepare_host(studio_project, studio)
            if suite != 'existing':
                for directory, package in [(backend, 'Acme.TriggerAuthoring'), (studio, 'Acme.TriggerEditor')]:
                    (directory / 'packages').mkdir()
                    candidates = list(extension_feed.glob(f'{package}.*.nupkg'))
                    if len(candidates) != 1:
                        raise ValueError(f'Expected one independent consumer archive for {package}')
                    shutil.copy2(candidates[0], directory / 'packages' / candidates[0].name)
            backend_url = f'http://localhost:{free_port()}'
            studio_url = f'http://localhost:{free_port()}'
            peer_url = f'http://localhost:{free_port()}' if suite != 'existing' else None
            # Configure ordinary source features and local origins only in the owned private copies.
            settings = json.loads((backend / 'appsettings.json').read_text(encoding='utf-8-sig'))
            settings['Cors']['AllowedOrigins'] = [studio_url]
            (backend / 'appsettings.json').write_text(json.dumps(settings))
            shells = json.loads((backend / 'shells.json').read_text(encoding='utf-8-sig'))
            identity = shells['CShells']['Shells']['default']['Features']['FoundationIdentityAspNetCoreIdentity']
            identity['AllowedReturnUrlOrigins'] = [studio_url]
            if suite != 'existing':
                features = shells['CShells']['Shells']['default']['Features']
                for name in ('StandaloneTriggersApi', 'StandaloneEventTriggerAuthoring', 'StandaloneHttpTriggerAuthoring',
                             'StandaloneScheduleTriggerAuthoring', 'StandaloneScheduleTriggers',
                             'ActivitiesBpmnHosting', 'ActivitiesBpmnHostingApi', 'ActivitiesBpmnDirectPublishing', 'AcceptanceTriggerAuthoring'):
                    features[name] = {}
                # Qualify the actual single-node host here; causal multi-node delivery has a separate provider witness.
                for name in ('WorkflowsRuntimeDistributed', 'WorkflowsRuntimeDistributedEntityFrameworkCorePersistence',
                             'WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence'):
                    features.pop(name, None)
                studio_settings = json.loads((studio / 'appsettings.json').read_text(encoding='utf-8-sig'))
                studio_settings['Studio']['Backends'] = {'DefaultId': 'legacy', 'AuthenticationProfiles': {'owned-session': {'Enabled': True}}, 'Profiles': [
                    {'Id': 'legacy', 'Name': 'Default backend', 'BaseUrl': backend_url, 'Environment': 'Development', 'AuthenticationProfile': 'owned-session'},
                    {'Id': 'acceptance-alternate', 'Name': 'Owned alternate connection', 'BaseUrl': peer_url, 'Environment': 'Test', 'AuthenticationProfile': 'owned-session'}]}
                (studio / 'appsettings.json').write_text(json.dumps(studio_settings))
                studio_shells = json.loads((studio / 'shells.json').read_text(encoding='utf-8-sig'))
                studio_shells['CShells']['Shells']['Default']['Features']['AcceptanceTriggerEditor'] = {}
                (studio / 'shells.json').write_text(json.dumps(studio_shells))
            (backend / 'shells.json').write_text(json.dumps(shells))
            scope_peer = None
            if suite != 'existing':
                peer_directory = data / 'workbench-scope-peer'
                prepare_host(backend_project, peer_directory)
                shutil.copy2(backend / 'appsettings.json', peer_directory / 'appsettings.json')
                peer_shells = json.loads(json.dumps(shells))
                peer_features = peer_shells['CShells']['Shells']['default']['Features']
                # Ordinary Identity configuration gives both owned servers the same genuine account/session
                # authority. Their Runtime, Design, Publishing and source resource stores remain separate.
                for name in ('IdentityIamEntityFrameworkCore', 'FoundationIdentityAspNetCoreIdentityEntityFrameworkCore'):
                    peer_features[name]['ConnectionString'] = f'Data Source={backend / "elsa.db"}'
                (peer_directory / 'shells.json').write_text(json.dumps(peer_shells))
                shutil.copytree(backend / 'packages', peer_directory / 'packages')
                scope_peer = (peer_directory, peer_url)
            env = dict(clean_environment(), HOME=str(data / "home"), DOTNET_CLI_HOME=str(data / "home"),
                       UX_SOURCE_SHA=sha, **{"Logging__LogLevel__Microsoft.Hosting.Lifetime": "Information"})
            (data / "home").mkdir()
            report.update(stage='host-start', studioUrl=studio_url, backendUrl=backend_url, scopePeerUrl=peer_url)
            with OwnedBackend(backend, backend_url, output, env, setup_dll, scope_peer) as owned_backend:
                with owned_backend.studio(studio, studio_url, dict(env, Studio__BackendBaseUrl=backend_url)):
                    report['stage'] = 'journeys'
                    browser_env = dict(os.environ, UX_ACCEPTANCE_OWNED='1', ELSA_STUDIO_URL=studio_url,
                                       ELSA_WORKBENCH_URL=backend_url, UX_BACKEND_URL=backend_url,
                                       UX_HOST_CONTROL=str(owned_backend.mailbox), UX_HOST_CONTROL_TOKEN=owned_backend.token,
                                       UX_ACCEPTANCE_OUT=str(output), UX_SOURCE_SHA=sha, UX_ACCEPTANCE_SUITE=suite)
                    command(['pnpm', 'exec', 'playwright', 'test', '--config', 'tests/acceptance/playwright.config.ts'],
                            output / 'journeys.log', cwd=ROOT / 'Studio', env=browser_env)
                    playwright_report = json.loads((output / 'playwright.json').read_text())
        end_sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
        end_dirty = subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()
        if sha != end_sha or dirty != end_dirty:
            raise ValueError('Source changed during acceptance; rerun the exact committed revision')
        if dirty:
            raise ValueError('Dirty source run is preliminary evidence, not final acceptance')
        report['executed'] = verdict(playwright_report, output, suite, sha)
        report.update(status='passed', stage='complete')
    except IncompleteAcceptanceError as error:
        report.update(status='incomplete', error=str(error))
        raise
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
    parser.add_argument('--suite', choices=['all', 'existing', 'standalone-triggers'], default='all',
                        help='Finite required inventory; a selected feature suite never certifies the other journeys')
    args = parser.parse_args()
    def terminate(_signum, _frame):
        raise InterruptedError('Acceptance interrupted; owned hosts will be stopped')
    signal.signal(signal.SIGTERM, terminate)
    run(args.output.resolve(), args.suite)

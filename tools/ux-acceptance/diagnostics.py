"""Sequential normal-host diagnostics, with an explicitly bounded offline native-port client."""
from __future__ import annotations

from contextlib import ExitStack
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import tempfile
import time
import uuid
from xml.sax.saxutils import escape

JOURNEY = 'diagnose exact executions, long history, retention and restricted access'
PROJECTS = {'diagnostics-desktop-light', 'diagnostics-320-dark'}
CAPTURES = ['repeated-execution', 'parallel-execution', 'completed-run', 'long-exact-bookmark',
            'long-new-events', 'long-unloaded-range', 'metadata-capture', 'partial-history',
            'purged-exact-event', 'expired-native-bookmark', 'metadata-reader', 'payload-denied',
            'foreign-exact-event', 'denied-run', 'disabled-history', 'historical-fault', 'resolved-incident', 'loading-history']


def validate_owner(root, nonce, source_sha):
    root = Path(root)
    if root.is_symlink() or root.resolve() != root or not root.parent.name.startswith('elsa-history-diagnostics-'):
        raise ValueError('Preparation requires the controller-created private diagnostics root')
    marker = json.loads((root / 'owned-history.json').read_text())
    if (marker.get('schemaVersion') != 1 or marker.get('nonce') != nonce or marker.get('root') != str(root)
            or marker.get('sourceSha') != source_sha or marker.get('controllerProcessId') != os.getpid()):
        raise ValueError('Preparation ownership does not match this live controller')
    database = root / 'elsa.db'
    if database.is_symlink() or not database.is_file() or any(file.is_symlink() for file in root.glob('*.db*')):
        raise ValueError('The owned normal host must create its own unlinked store')
    return marker


def store_hashes(root):
    return {file.name: hashlib.sha256(file.read_bytes()).hexdigest()
            for file in Path(root).glob('*.db*') if file.is_file()}


def build_fixture_tool(source_root, workbench_output, destination, evidence, command):
    """SDK-only temporary consumer of the just-built normal Workbench closure; no product project/package."""
    destination.mkdir()
    shutil.copy2(source_root / 'global.json', destination / 'global.json')
    shutil.copy2(source_root / 'tools/ux-acceptance/HistoryDiagnosticsFixture.cs', destination / 'Program.cs')
    references = sorted(workbench_output.glob('*.dll'))
    if not references or not (workbench_output / 'Elsa.Workbench.dll').is_file():
        raise ValueError('The fixture requires the freshly built normal Workbench binary closure')
    body = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" />'
    body += ''.join(f'<Reference Include="{escape(file.stem)}"><HintPath>{escape(str(file))}</HintPath></Reference>' for file in references)
    body += '</ItemGroup></Project>'
    project = destination / 'HistoryDiagnosticsFixture.csproj'
    project.write_text(body)
    (evidence / 'fixture-source.json').write_text(json.dumps({
        'source': 'tools/ux-acceptance/HistoryDiagnosticsFixture.cs',
        'sourceSha256': hashlib.sha256((destination / 'Program.cs').read_bytes()).hexdigest(),
        'projectReferences': [], 'packageReferences': [],
        'normalBinaryReferences': [{'name': file.name, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest()} for file in references]
    }, indent=2) + '\n')
    shutil.copy2(destination / 'Program.cs', evidence / 'fixture-source.cs')
    # There are zero PackageReferences; every product dependency is the exact normal binary above.
    command(['dotnet', 'build', str(project), '-v:q', '-nologo'], evidence / 'fixture-build.log', cwd=destination)
    output = destination / 'bin/Debug/net10.0'
    shutil.copy2(output / 'HistoryDiagnosticsFixture.dll', evidence / 'fixture-consumer.dll')
    machine = {'aarch64': 'arm64', 'x86_64': 'x64'}.get(platform.machine(), platform.machine())
    system = {'Darwin': 'osx', 'Linux': 'linux', 'Windows': 'win'}.get(platform.system())
    native = workbench_output / 'runtimes' / f'{system}-{machine}' / 'native'
    if native.is_dir():
        for file in native.iterdir():
            if file.is_file(): shutil.copy2(file, output / file.name)
    return output / 'HistoryDiagnosticsFixture.dll'


class DiagnosticsSupervisor:
    def __init__(self, *, source_root, output, data, source_sha, tool, command, host, prepare_host, clean_environment, free_port):
        self.source_root, self.output, self.data, self.source_sha, self.tool = source_root, output, data, source_sha, tool
        self.command, self.host, self.prepare_host, self.clean_environment = command, host, prepare_host, clean_environment
        self.backend_url, self.studio_url = f'http://localhost:{free_port()}', f'http://localhost:{free_port()}'
        self.requests = data / 'requests'; self.requests.mkdir()
        self.stack = ExitStack(); self.project = None; self.phase = None; self.completed = {}; self.started = set(); self.prepared = set()
        self.backend = self.studio = self.home = self.process = self.accounts = self.nonce = None

    def __enter__(self):
        return self

    def __exit__(self, *exception):
        return self.stack.__exit__(*exception)

    def _start(self, project, profile):
        self.stack.close(); self.stack = ExitStack()
        self.project, self.phase = project, profile
        prefix = f'{project}-{profile}'
        self.backend, self.studio, self.home = self.data / f'workbench-{prefix}', self.data / f'studio-{prefix}', self.data / f'home-{prefix}'
        self.home.mkdir()
        self.prepare_host(self.source_root / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj', self.backend)
        self.prepare_host(self.source_root / 'Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj', self.studio)
        settings = json.loads((self.backend / 'appsettings.json').read_text(encoding='utf-8-sig'))
        settings['Cors']['AllowedOrigins'] = [self.studio_url]
        (self.backend / 'appsettings.json').write_text(json.dumps(settings))
        shells = json.loads((self.backend / 'shells.json').read_text(encoding='utf-8-sig'))
        features = shells['CShells']['Shells']['default']['Features']
        features['FoundationIdentityAspNetCoreIdentity']['AllowedReturnUrlOrigins'] = [self.studio_url]
        features['WorkflowsRuntimeHistory']['Enabled'] = profile == 'enabled'
        (self.backend / 'shells.json').write_text(json.dumps(shells))
        self.env = dict(self.clean_environment(), HOME=str(self.home), DOTNET_CLI_HOME=str(self.home),
                        **{'Logging__LogLevel__Microsoft.Hosting.Lifetime': 'Information'})
        self.process = self.stack.enter_context(self.host(self.backend, 'Elsa.Workbench.dll', self.backend_url, '/health/ready', self.output / f'{prefix}-backend.log', self.env))
        self.stack.enter_context(self.host(self.studio, 'Elsa.Studio.Web.dll', self.studio_url, '/studio-runtime.js', self.output / f'{prefix}-studio.log', dict(self.env, Studio__BackendBaseUrl=self.backend_url)))

    def _restart(self, project):
        # Same content, ports, environment and stores. No replacement root/config while restoring captured facts.
        self.process = self.stack.enter_context(self.host(self.backend, 'Elsa.Workbench.dll', self.backend_url, '/health/ready', self.output / f'{project}-restarted-backend.log', self.env))
        self.stack.enter_context(self.host(self.studio, 'Elsa.Studio.Web.dll', self.studio_url, '/studio-runtime.js', self.output / f'{project}-restarted-studio.log', dict(self.env, Studio__BackendBaseUrl=self.backend_url)))

    def supervise(self):
        file = self.requests / 'request.json'
        if not file.exists(): return
        if file.stat().st_size > 4096: raise ValueError('Diagnostics request exceeded its private bound')
        request = json.loads(file.read_text())
        identity, project, operation = request.get('id', ''), request.get('project'), request.get('operation')
        if not re.fullmatch(r'[a-f0-9-]{36}', identity) or project not in PROJECTS or operation not in {'start', 'prepare', 'disabled'}:
            raise ValueError('Unknown diagnostics operation/project')
        if identity in self.completed:
            return
        if operation == 'start':
            if project in self.started: raise ValueError('Each diagnostics project can create only one enabled store')
            self._start(project, 'enabled'); self.started.add(project)
            self.nonce = uuid.uuid4().hex
            suffix = self.nonce[:8]
            self.accounts = [dict(userName=f'history-probe-{suffix}-{profile}', tenantId='default', profile=profile,
                                  password='Password123!', permissions=['api-capabilities:read', 'workflows/runtime:read'] +
                                  (['workflows/activity-executions:inspect'] if profile != 'denied' else []) +
                                  (['workflows/activity-executions:inspect-values'] if profile == 'payload-denied' else []))
                             for profile in ('metadata', 'payload-denied', 'denied')]
            receipt = {'operation': operation, 'project': project, 'sourceSha': self.source_sha,
                       'backendUrl': self.backend_url, 'studioUrl': self.studio_url}
        elif operation == 'prepare':
            if self.project != project or self.phase != 'enabled' or project in self.prepared:
                raise ValueError('Only one same-store preparation/restart per diagnostics project is allowed')
            run, sequence, template = request.get('completedRun'), request.get('throughSequence'), request.get('expiryTemplateArtifactId')
            if not isinstance(run, str) or not run or not isinstance(sequence, int) or sequence <= 0 or not isinstance(template, str) or not template:
                raise ValueError('Preparation requires exact normal API fixture identities')
            old_pid = self.process.pid
            self.stack.close(); self.stack = ExitStack()
            owner = dict(schemaVersion=1, nonce=self.nonce, controllerProcessId=os.getpid(), sourceSha=self.source_sha,
                         root=str(self.backend), backendPid=old_pid)
            (self.backend / 'owned-history.json').write_text(json.dumps(owner))
            validate_owner(self.backend, self.nonce, self.source_sha)
            setup_file = self.backend / 'owned-history-request.json'
            setup_file.write_text(json.dumps(dict(nonce=self.nonce, sourceSha=self.source_sha, project=project,
                                                  completedRun=run, throughSequence=sequence, expiryTemplateArtifactId=template, accounts=self.accounts)))
            try:
                before = store_hashes(self.backend)
                rejected = False
                try:
                    self.command(['dotnet', str(self.tool), str(self.backend), 'wrong-owner'], self.output / f'{project}-ownership-refusal.log', cwd=self.backend, env=self.env)
                except subprocess.CalledProcessError:
                    rejected = True
                unchanged = before == store_hashes(self.backend)
                if not rejected or not unchanged: raise ValueError('Invalid fixture ownership was not refused before store writes')
                log = self.output / f'{project}-native-preparation.log'
                self.command(['dotnet', str(self.tool), str(self.backend), self.nonce], log, cwd=self.backend, env=self.env)
                facts = json.loads(log.read_text().splitlines()[-1])
                if facts.get('sourceSha') != self.source_sha or facts.get('historyRowsInjected') is not False:
                    raise ValueError('Native preparation did not return its exact-source port receipt')
                receipt = {**facts, 'operation': operation, 'oldPid': old_pid,
                           'invalidOwnershipRejected': rejected, 'invalidOwnershipChangedStores': not unchanged}
            finally:
                setup_file.unlink(missing_ok=True)  # Private demo credentials never enter public attachments.
            self._restart(project); self.prepared.add(project)
            receipt['newPid'] = self.process.pid
        else:
            if self.project != project or self.phase != 'enabled' or project not in self.prepared:
                raise ValueError('Disabled inspection follows completed enabled-store diagnostics only')
            previous = self.backend
            self._start(project, 'disabled')
            if self.backend == previous: raise ValueError('Disabled new admissions must use a different owned store')
            receipt = {'operation': operation, 'project': project, 'sourceSha': self.source_sha,
                       'captureEnabledAtAdmission': False, 'separateStore': True,
                       'backendUrl': self.backend_url, 'studioUrl': self.studio_url}
        receipt_file = self.output / f'{project}-{operation}-receipt.json'
        receipt_file.write_text(json.dumps(receipt, indent=2) + '\n')
        ack = {**receipt, 'id': identity, 'status': 'ready', 'receiptFile': str(receipt_file),
               'backendUrl': self.backend_url, 'studioUrl': self.studio_url}
        if operation == 'prepare': ack['accounts'] = self.accounts
        temporary = self.requests / 'ack.tmp'; temporary.write_text(json.dumps(ack)); temporary.replace(self.requests / 'ack.json')
        self.completed[identity] = receipt


def verdict(report, output):
    rows = []
    def walk(suites):
        for suite in suites:
            for spec in suite.get('specs', []):
                rows.extend((test.get('projectName'), spec.get('title'), test) for test in spec.get('tests', []))
            walk(suite.get('suites', []))
    walk(report.get('suites', []))
    if len(rows) != 2 or {(project, title) for project, title, _ in rows} != {(project, JOURNEY) for project in PROJECTS} or report.get('errors'):
        raise ValueError('Incomplete diagnostics inventory')
    required = {'trace', 'native-preparation', 'diagnostic-facts', 'disabled-profile'} | {f'{capture}.{suffix}' for capture in CAPTURES for suffix in ('png', 'aria.yml')}
    for project, _, test in rows:
        results = test.get('results', [])
        if test.get('expectedStatus') != 'passed' or test.get('status') != 'expected' or len(results) != 1 or results[0].get('status') != 'passed':
            raise ValueError(f'{project}: diagnostics was not one successful execution')
        attachments = results[0].get('attachments', [])
        if not required <= {item.get('name') for item in attachments}: raise ValueError(f'{project}: missing diagnostics artifacts')
        for item in attachments:
            if not item.get('path') and item.get('name') not in required: continue
            file = Path(item.get('path', '')).resolve()
            if not file.is_relative_to(output.resolve()) or not file.is_file() or not file.stat().st_size:
                raise ValueError(f'{project}: missing/unsafe diagnostics artifact')
    return len(rows)


def run_diagnostics(output, *, source_root, source_sha, tool, command, host, prepare_host, clean_environment, free_port):
    if not output.is_dir() or not tool.is_file(): raise ValueError('Build the exact-source fixture consumer before browser acceptance')
    with tempfile.TemporaryDirectory(prefix='elsa-history-diagnostics-') as temporary:
        data = Path(temporary).resolve()
        with DiagnosticsSupervisor(source_root=source_root, output=output, data=data, source_sha=source_sha, tool=tool,
                                   command=command, host=host, prepare_host=prepare_host, clean_environment=clean_environment, free_port=free_port) as supervisor:
            env = dict(os.environ, UX_ACCEPTANCE_OWNED='1', UX_SOURCE_SHA=source_sha, UX_ACCEPTANCE_OUT=str(output),
                       ELSA_STUDIO_URL=supervisor.studio_url, UX_ACCEPTANCE_DIAGNOSTICS_DIR=str(supervisor.requests))
            command(['pnpm', 'exec', 'playwright', 'test', '--config', 'tests/acceptance/playwright.diagnostics.config.ts'],
                    output / 'journeys.log', cwd=source_root / 'Studio', env=env, supervise=supervisor.supervise)
            return verdict(json.loads((output / 'playwright.json').read_text()), output)

#!/usr/bin/env python3
"""Drive the actual host APIs. Authentication values are used only in memory."""
import argparse
import datetime
import http.cookiejar
import json
import pathlib
import shutil
import ssl
import subprocess
import time
import urllib.error
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        return None


class Proof:
    def __init__(self, root):
        self.root = root
        self.host = json.loads((root / 'active-host.json').read_text())
        self.env = dict(x.split('=', 1) for x in (root / 'private.env').read_text().splitlines())
        # A self-signed certificate is scoped to this disposable loopback host.
        self.client = urllib.request.build_opener(NoRedirect(), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()), urllib.request.HTTPSHandler(context=ssl._create_unverified_context()))

    def save(self, name, data):
        (self.root / 'receipts' / (name + '.json')).write_text(json.dumps(data, indent=2) + '\n')

    def request(self, path, data=None, management=False):
        headers = {'Content-Type': 'application/json', 'Accept': 'application/json'}
        if management:
            headers['X-Elsa-Module-Management-Key'] = self.env['Elsa__ModuleManagement__ApiKey']
        request = urllib.request.Request(self.host['url'] + path, None if data is None else json.dumps(data).encode(), headers)
        try:
            response = self.client.open(request, timeout=180)
        except urllib.error.HTTPError as failure:
            response = failure
        body = response.read().decode()
        try:
            body = json.loads(body)
        except ValueError:
            pass
        return dict(status=response.code, body=body, redirectLocation=response.headers.get('Location'))

    def ready(self):
        deadline = time.monotonic() + 180
        while time.monotonic() < deadline:
            try:
                result = self.request('/health/ready')
                self.save('readiness', result)
                if result['status'] == 200:
                    print(json.dumps(result))
                    return
            except (OSError, urllib.error.URLError):
                pass
            time.sleep(2)
        raise RuntimeError('Host did not become ready; inspect its retained logs')

    def login(self):
        password = self.env['CShells__Shells__default__Features__FoundationIdentityAspNetCoreIdentityEntityFrameworkCore__SeedAdminPassword']
        result = self.request('/_elsa/identity/login', dict(username='fr2-admin', password=password))
        if result['status'] != 200:
            raise RuntimeError(f"Login refused: {result['status']} {result['body']}")
        self.save('authentication', dict(status=result['status'], user='fr2-admin', production=True, https=True))

    def reconcile(self, name):
        result = self.request('/_module-management/reconcile', {}, True)
        self.save(name, result)
        print(json.dumps(result)[:1200])
        return result

    def reload(self, name):
        result = self.request('/_module-management/reload', {}, True)
        self.save(name, result)
        print(json.dumps(result)[:1200])
        return result

    def wait_witness(self, path, receipt):
        deadline = time.monotonic() + 60
        while True:
            result = self.request(path)
            self.save(receipt, result)
            if result['status'] == 200:
                return result
            if time.monotonic() >= deadline:
                raise RuntimeError(f'Witness did not become available: {path}: {result}')
            time.sleep(5)
            self.reload(receipt + '-reload')

    def directory(self):
        shutil.copyfile(self.root / 'fixtures/Fr2Proof.Directory.1.0.0.nupkg', self.root / 'packages/Fr2Proof.Directory.1.0.0.nupkg')
        config = json.loads((self.root / 'config/shells.json').read_text())
        config['CShells']['Shells']['default']['Features']['Fr2Directory'] = {}
        (self.root / 'config/shells.json').write_text(json.dumps(config, indent=2) + '\n')
        time.sleep(5)
        self.reconcile('directory-reconcile')
        self.reload('directory-reload')
        result = self.wait_witness('/fr2/directory', 'directory-witness')
        assert result['status'] == 200, result
        print('Directory package activation passed')

    def live(self):
        closure = json.loads((self.root / 'package-closure.json').read_text())
        package = next(x for x in closure if x['id'] == 'Elsa.Workflows.Design.Reconciliation.Json')
        manifest = dict(SchemaVersion='1.0', GeneratedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(), Packages=[dict(Id=package['id'], Version=package['version'], SourceHint='elsa-4')])
        (self.root / 'config/desired.json').write_text(json.dumps(manifest, indent=2) + '\n')
        self.save('live-manifest', manifest)
        result = self.reconcile('live-remote-reconcile')
        assert result['status'] == 200, result
        workflow = dict(definitionId='wfdef_fr2_remote_delivery_1145', name='FR2 remote delivery proof', version='1.0.0', deleted=False,
                        sourceCreatedAt='2026-11-05T00:00:00Z', state=dict(variables=[], inputs=[], outputs=[], workflowActivityOptions=None,
                        rootActivity=dict(nodeId='proof-result', activityVersionId='elsa.intrinsic.set-output@1', outputs=[],
                        intrinsic=dict(kind='setOutput', valueType=dict(alias='String', collectionKind='single')),
                        inputs=[dict(referenceKey=k, value=dict(value=v, expressionType='Literal')) for k, v in [('name', 'proof'), ('value', 'remote-feed-execution-passed')]])))
        (self.root / 'catalog/workflows.json').write_text(json.dumps([workflow], indent=2) + '\n')
        config = json.loads((self.root / 'config/shells.json').read_text())
        config['CShells']['Shells']['default']['Features']['JsonWorkflowReconciliation'] = dict(Options=dict(SourceId='fr2-1145', FilePath='/proof/catalog/workflows.json', PublishOnReconcile=False))
        (self.root / 'config/shells.json').write_text(json.dumps(config, indent=2) + '\n')
        time.sleep(5)
        self.reload('live-feature-reload')
        self.ready()
        self.login()
        deadline = time.monotonic() + 45
        while True:
            result = self.request('/design/workflows/definitions?search=FR2')
            if result['status'] == 200 and result['body']['items']:
                break
            if time.monotonic() >= deadline:
                raise RuntimeError(f'File reconciliation did not produce the fixture definition: {result}')
            time.sleep(5)
            self.reload('live-feature-reload-confirmed')
        self.save('workflow-list', result)
        print(json.dumps(result)[:2500])

    def snapshot(self):
        result = subprocess.check_output(['docker', 'inspect', self.host['name'], '--format', '{{json .State}}'], text=True)
        state = json.loads(result)
        self.save('container-current', {k: state[k] for k in ['Status', 'Running', 'Pid', 'StartedAt', 'ExitCode']})
        logs = subprocess.check_output(['docker', 'logs', self.host['name']], text=True, stderr=subprocess.STDOUT)
        for value in self.env.values():
            if len(value) > 20:
                logs = logs.replace(value, '<fixture-secret>')
        (self.root / 'receipts' / 'host.log').write_text(logs)

    def inventory(self, name):
        state = json.loads((pathlib.Path(self.host['state']) / 'state.json').read_text())
        descriptors = state['activePackageDescriptorsById'].values()
        inventory = [dict(id=x['packageId'], version=x['version'], feed=x['feedName'], source=x['sourceName'], activatedAtUtc=x.get('activatedAtUtc')) for x in descriptors]
        self.save(name, dict(packages=sorted(inventory, key=lambda x: x['id']), failures=state['lastFailureById']))
        return inventory

    def execute(self):
        self.login()
        result = self.request('/design/workflows/definitions?search=FR2')
        self.save('workflow-list', result)
        assert result['status'] == 200, result
        item = next(x for x in result['body']['items'] if x['id'] == 'wfdef_fr2_remote_delivery_1145')
        version = item['latestVersionId']
        detail = self.request('/design/workflows/definitions/' + item['id'])
        self.save('workflow-definition', detail)
        assert detail['status'] == 200, detail
        published = self.request('/publishing/workflows/' + version + '/publish', {})
        self.save('workflow-publish', published)
        assert published['status'] in [200, 201], published
        print('Published:', json.dumps(published)[:2500])
        body = published['body']
        execution = self.request('/runtime/workflows/executables/' + body['artifactId'] + '/execute', dict(sourceReferenceId=body['sourceReferenceId']) if body.get('sourceReferenceId') else {})
        self.save('workflow-execute', execution)
        assert execution['status'] in [200, 201, 202], execution
        print('Execution:', json.dumps(execution)[:2500])
        execution_id = execution['body']['workflowExecutionId']
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            observed = self.request('/runtime/workflows/instances/' + execution_id)
            if observed['status'] == 200 and observed['body']['instance']['status'] in ['Completed', 'Faulted', 'Cancelled']:
                break
            time.sleep(1)
        self.save('workflow-instance', observed)
        assert observed['status'] == 200, observed
        assert observed['body']['instance']['status'] == 'Completed', observed
        assert observed['body']['instance']['incidentCount'] == 0, observed
        assert observed['body']['outputs']['proof']['value']['preview'] == 'remote-feed-execution-passed', observed
        print('Completed with the expected output and zero incidents:', execution_id)

    def compatible(self):
        shutil.copyfile(self.root / 'fixtures/Fr2Proof.Compatible.1.0.0.nupkg', self.root / 'packages/Fr2Proof.Compatible.1.0.0.nupkg')
        config = json.loads((self.root / 'config/shells.json').read_text())
        config['CShells']['Shells']['default']['Features']['Fr2Compatible'] = {}
        (self.root / 'config/shells.json').write_text(json.dumps(config, indent=2) + '\n')
        time.sleep(5)
        self.reconcile('compatible-reconcile')
        self.reload('compatible-reload')
        result = self.wait_witness('/fr2/compatible', 'compatible-witness')
        assert result['status'] == 200, result
        assert 'Fr2Proof.Compatible' in result['body']['assemblies'], result
        print('Compatible floor activated')

    def incompatible(self):
        before = self.request('/fr2/directory')
        shutil.copyfile(self.root / 'fixtures/Fr2Proof.Incompatible.1.0.0.nupkg', self.root / 'packages/Fr2Proof.Incompatible.1.0.0.nupkg')
        time.sleep(2)
        self.reconcile('incompatible-reconcile')
        after = self.request('/fr2/directory')
        self.save('incompatible-control', dict(before=before, after=after, endpoint=self.request('/fr2/incompatible')))
        assert after['status'] == 200, after
        assert 'Fr2Proof.Incompatible' not in after['body']['assemblies'], after
        assert before['body']['pid'] == after['body']['pid'], after
        state = json.loads((pathlib.Path(self.host['state']) / 'state.json').read_text())
        assert 'Fr2Proof.Incompatible' not in state['activeVersionById'], state['activeVersionById']
        self.save('incompatible-state', dict(active=state['activeVersionById'], failures=state['lastFailureById']))
        print('Incompatible package absent from active state and loaded assemblies; prior serving process retained')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('root', type=pathlib.Path)
    parser.add_argument('action', choices=['ready', 'directory', 'live', 'snapshot', 'login', 'execute', 'compatible', 'incompatible'])
    args = parser.parse_args()
    proof = Proof(args.root.resolve())
    try:
        getattr(proof, args.action)()
    finally:
        proof.snapshot()

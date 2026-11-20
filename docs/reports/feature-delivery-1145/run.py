#!/usr/bin/env python3
"""Replay #1145 against the immutable published host; stop only the container this run creates."""
import argparse
import hashlib
import json
import pathlib
import shutil
import subprocess
import datetime
from prepare import IMAGE, prepare
from configure import configure
from fixtures import build
from launch import launch
from exercise import Proof


def check_remote_inventory(packages, declared):
    observed = {x['id'].lower(): x for x in packages}
    assert set(observed) == set(declared), dict(undeclared=sorted(set(observed) - set(declared)), missing=sorted(set(declared) - set(observed)))
    for name, pin in declared.items():
        assert observed[name]['version'] == pin['version'] and observed[name]['feed'] == pin['feed'], dict(expected=pin, actual=observed[name])


def stop_owned_host(proof, receipts):
    try:
        if not (receipts / 'summary.json').exists():
            proof.snapshot()
    finally:
        subprocess.run(['docker', 'stop', proof.host['name']], check=True, stdout=subprocess.DEVNULL)


def main(root, attempt):
    if root.exists() and any(root.iterdir()):
        raise RuntimeError('Use a new, empty output directory for every replay')
    root.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(pathlib.Path(__file__).with_name('pins.json'), root / 'published-versions.json')
    subprocess.run(['docker', 'pull', IMAGE], check=True)
    extract = subprocess.check_output(['docker', 'create', IMAGE], text=True).strip()
    try:
        for member, destination in [('Elsa.Foundation.Host.deps.json', 'host.deps.json'), ('appsettings.json', 'host.appsettings.json'), ('appsettings.Production.json', 'host.production.json'), ('CShells.Abstractions.dll', 'CShells.Abstractions.dll'), ('CShells.AspNetCore.Abstractions.dll', 'CShells.AspNetCore.Abstractions.dll')]:
            subprocess.run(['docker', 'cp', extract + ':/app/' + member, str(root / destination)], check=True)
    finally:
        subprocess.run(['docker', 'rm', extract], check=True, stdout=subprocess.DEVNULL)
    prepare(root)
    with (root / 'restore.log').open('w') as log:
        subprocess.run(['dotnet', 'restore', 'restore.csproj', '--packages', './pkgs', '--configfile', 'NuGet.config', '--disable-parallel'], cwd=root / 'restore', check=True, stdout=log, stderr=subprocess.STDOUT)
    configure(root)
    build(root)
    receipts = root / 'receipts'
    for name in ['appsettings.json', 'shells.json', 'desired.json']:
        shutil.copyfile(root / 'config' / name, receipts / ('initial-' + name))
    launch(root, attempt)
    proof = Proof(root)
    try:
        proof.ready()
        initial_state = json.loads(subprocess.check_output(['docker', 'inspect', proof.host['name'], '--format', '{{json .State}}'], text=True))
        cold = proof.inventory('cold-inventory')
        declared = json.loads((root / 'declared-remote-pins.json').read_text())
        check_remote_inventory(cold, declared)
        proof.save('declared-remote-pins', declared)
        assert all(x['feed'] in ['elsa-4', 'nuget.org'] for x in cold), cold
        assert not any(x['id'] == 'Elsa.Workflows.Design.Reconciliation.Json' for x in cold)
        anonymous = proof.request('/design/workflows/definitions')
        proof.save('anonymous-design-api', anonymous)
        assert anonymous['status'] in [401, 403] or (anonymous['status'] == 302 and '/_elsa/identity/login' in (anonymous['redirectLocation'] or '')), anonymous
        proof.login()
        empty = proof.request('/design/workflows/definitions?search=FR2')
        proof.save('before-reconciliation', empty)
        assert empty['status'] == 200 and not empty['body']['items'], empty
        proof.directory()
        proof.live()
        proof.execute()
        proof.compatible()
        proof.incompatible()
        # The workflow path must still work alongside both local witnesses and the refused package.
        proof.execute()
        final = proof.inventory('final-inventory')
        json_package = next(x for x in json.loads((root / 'package-closure.json').read_text()) if x['id'] == 'Elsa.Workflows.Design.Reconciliation.Json')
        final_pins = dict(declared)
        final_pins[json_package['id'].lower()] = dict(id=json_package['id'], version=json_package['version'], feed='elsa-4')
        check_remote_inventory([x for x in final if x['feed'] != 'directory-witness'], final_pins)
        proof.save('remote-pin-validation', dict(coldCount=len(cold), finalRemoteCount=len(final_pins), undeclared=[], versionOrSourceMismatches=[], capabilityPins=[x for x in declared.values() if 'contribution' in x]))
        witness = proof.request('/fr2/directory')
        proof.save('final-directory-witness', witness)
        assert witness['status'] == 200 and 'JsonWorkflowReconciliation' in witness['body']['features'], witness
        final_state = json.loads(subprocess.check_output(['docker', 'inspect', proof.host['name'], '--format', '{{json .State}}'], text=True))
        assert initial_state['StartedAt'] == final_state['StartedAt'] and initial_state['Pid'] == final_state['Pid']
        proof.snapshot()
        log = (receipts / 'host.log').read_text()
        state = json.loads((pathlib.Path(proof.host['state']) / 'state.json').read_text())
        failure = state['lastFailureById']['Fr2Proof.Incompatible']
        assert failure['stage'] == 'host-version-unsatisfied', failure
        diagnostic = [x.strip() for x in log.splitlines() if 'Host-provided dependency version refused' in x and 'Fr2Proof.Incompatible' in x]
        assert diagnostic, 'The incompatible package requires a named version-refusal diagnostic'
        proof.save('compatibility-diagnostic', dict(failure=failure, lines=diagnostic, remedy='Use an image providing the required host dependency, or select a feature version whose declared range includes this image.'))
        fixture_hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in pathlib.Path(__file__).parent.iterdir() if p.suffix in ['.py', '.json']}
        proof.save('summary', dict(issue=1145, outcome='passed', image=IMAGE, timestampUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(), coldPackages=len(cold), finalActivePackages=len(final), sameProcess=True, containerId=proof.host['id'], startedAt=initial_state['StartedAt'], containerHostPid=initial_state['Pid'], production=True, https=True, loadMode='HostIntegrated', assemblyUnloadingClaimed=False, fixtureSha256=fixture_hashes, criteria=dict(isolation=True, remoteAcquisition=True, fileReconciliationPublicationExecution=True, directoryCoexistence=True, compatibilityRefusalAndControl=True, reproducibleRunningHostPath=True)))
        for name in ['appsettings.json', 'shells.json', 'desired.json']:
            shutil.copyfile(root / 'config' / name, receipts / ('final-' + name))
        shutil.copyfile(root / 'catalog/workflows.json', receipts / 'workflow-catalog.json')
        shutil.copyfile(root / 'package-closure.json', receipts / 'package-closure.json')
        hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(receipts.iterdir()) if p.is_file()}
        (receipts / 'sha256.json').write_text(json.dumps(hashes, indent=2) + '\n')
        print('PASS:', (receipts / 'summary.json').read_text())
    finally:
        stop_owned_host(proof, receipts)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('root', type=pathlib.Path)
    parser.add_argument('--attempt', type=int, default=1)
    args = parser.parse_args()
    main(args.root.resolve(), args.attempt)

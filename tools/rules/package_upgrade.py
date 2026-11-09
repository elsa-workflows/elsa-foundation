"""Prove retained Rules recovery across two independently packaged deployments."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET
import zipfile

import package_adversarial
from package_adversarial import ObservedProcess


RUNTIME_ROOTS = {
    'Elsa.Rules.Core', 'Elsa.Rules.Persistence.Core', 'Elsa.Rules.Runtime',
    'Elsa.Rules.Persistence.EntityFrameworkCore',
}
EXPECTED_FACTS = ['final', 'joined', 'left', 'right', 'seed']
PIN_PATTERN = re.compile(r'[0-9a-f]{64}')
COMMIT_PATTERN = re.compile(r'[0-9a-f]{40}')


def _sha256(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def _write_json(path, data):
    Path(path).write_text(json.dumps(data, indent=2, sort_keys=True) + '\n')


def _read_json(path):
    return json.loads(Path(path).read_text())


def _require(condition, message):
    if not condition:
        raise ValueError(message)


def _local_name(name):
    return name.rsplit('}', 1)[-1]


def _package_metadata(path):
    with zipfile.ZipFile(path) as archive:
        specs = [name for name in archive.namelist() if '/' not in name and name.endswith('.nuspec')]
        _require(len(specs) == 1, 'Expected exactly one nuspec in ' + str(path))
        root = ET.fromstring(archive.read(specs[0]))
        metadata = next((element for element in root if _local_name(element.tag) == 'metadata'), None)
        _require(metadata is not None, 'Package has no nuspec metadata: ' + str(path))
        fields = {_local_name(element.tag): element for element in metadata}
        repository = fields.get('repository')
        _require(repository is not None, 'Package nuspec has no source repository identity: ' + str(path))
        fingerprint = json.loads(archive.read('elsa-input-fingerprint.json'))['fingerprint']
        return {
            'id': fields['id'].text,
            'version': fields['version'].text,
            'sourceCommit': repository.attrib.get('commit'),
            'fingerprint': fingerprint,
            'archiveSha512': base64.b64encode(hashlib.sha512(Path(path).read_bytes()).digest()).decode(),
        }


def _expected_packages(receipt):
    packages = receipt.get('packages')
    _require(isinstance(packages, list) and packages, 'Deployment receipt has no immutable package list.')
    expected = {}
    for package in packages:
        package_id, version = package.get('id'), package.get('version')
        _require(package_id and version and package_id not in expected,
                 'Deployment package list contains an invalid or duplicate identity.')
        _require(package.get('sourceCommit') == receipt.get('sourceCommit'),
                 'Packed package source commit differs from the consumer source commit: ' + package_id)
        expected[package_id] = package
    closure = {item['id']: item['version'] for item in receipt.get('closure', [])}
    _require({key: value['version'] for key, value in expected.items()} == closure,
             'Packaged archives do not match the immutable dependency closure.')
    return expected


def _validate_feed(feed, receipt):
    feed = Path(feed).resolve()
    _require(feed.is_dir(), 'Immutable package feed is missing: ' + str(feed))
    expected = _expected_packages(receipt)
    archives = sorted(feed.glob('*.nupkg'))
    _require(len(archives) == len(expected), 'Local feed does not contain the exact package closure: ' + str(feed))
    found = {}
    for archive in archives:
        actual = _package_metadata(archive)
        record = expected.get(actual['id'])
        _require(record is not None and actual['id'] not in found,
                 'Local feed contains an unexpected or duplicate package: ' + archive.name)
        for field in ('version', 'sourceCommit', 'fingerprint', 'archiveSha512'):
            _require(actual[field] == record.get(field),
                     f'Local package {actual["id"]} {field} differs from its immutable receipt.')
        found[actual['id']] = archive
    _require(found.keys() == expected.keys(), 'Local feed is missing an immutable package archive.')
    return feed


def _resolve_feed(run_dir, receipt):
    local_feed = run_dir / 'feed'
    if local_feed.is_dir() and any(local_feed.glob('*.nupkg')):
        try:
            return _validate_feed(local_feed, receipt), None
        except (OSError, ValueError, KeyError, zipfile.BadZipFile) as error:
            local_error = error
    else:
        local_error = ValueError('Consumer run has no local package archives.')

    reused = receipt.get('reusedPackReceipt')
    if not isinstance(reused, dict) or not reused.get('path') or not reused.get('sha256'):
        raise ValueError('No verified package feed is available; ' + str(local_error))
    pack_receipt_path = Path(reused['path']).expanduser().resolve()
    _require(pack_receipt_path.is_file() and _sha256(pack_receipt_path) == reused['sha256'],
             'The documented reused pack receipt is missing or its SHA-256 changed.')
    pack_receipt = _read_json(pack_receipt_path)
    _require(pack_receipt.get('schemaVersion') == 1 and pack_receipt.get('harnessUnchanged') is True
             and pack_receipt.get('packPassed') is True and not pack_receipt.get('error'),
             'The referenced reused pack did not pass its immutable packing checkpoint.')
    _require(pack_receipt.get('sourceCommit') == receipt.get('sourceCommit')
             and pack_receipt.get('publishStateCommit') == receipt.get('publishStateCommit'),
             'The referenced reused pack uses different immutable source or publish-state inputs.')
    outer = _expected_packages(receipt)
    inner = _expected_packages(pack_receipt)
    _require({key: (value['version'], value['archiveSha512']) for key, value in outer.items()}
             == {key: (value['version'], value['archiveSha512']) for key, value in inner.items()},
             'Consumer receipt package records differ from its referenced immutable pack.')
    feed = _validate_feed(pack_receipt_path.parent / 'feed', receipt)
    return feed, {'path': str(pack_receipt_path), 'sha256': reused['sha256']}


def _validate_deployment(run_path, label):
    run_dir = Path(run_path).expanduser().resolve()
    receipt_path = run_dir / 'receipt.json'
    _require(receipt_path.is_file(), f'{label} run has no receipt.json: {run_dir}')
    receipt = _read_json(receipt_path)
    _require(receipt.get('schemaVersion') == 1 and receipt.get('harnessUnchanged') is True
             and receipt.get('packPassed') is True and receipt.get('directProofPassed') is True
             and receipt.get('consumerPackageReferenceRestorePassed') is True,
             f'{label} is not an independently passing immutable package consumer run.')
    _require(not receipt.get('error'), f'{label} consumer run failed.')
    _require(COMMIT_PATTERN.fullmatch(receipt.get('sourceCommit', '')) is not None
             and COMMIT_PATTERN.fullmatch(receipt.get('publishStateCommit', '')) is not None,
             f'{label} run lacks full immutable source and publish-state commit identities.')
    _require(receipt.get('consumerModuleRoots') is not None
             and set(receipt['consumerModuleRoots']) == RUNTIME_ROOTS,
             f'{label} consumer does not use exactly the four package-only runtime roots.')
    resolved = receipt.get('resolvedPackages', {})
    design = [name for name in resolved if name.startswith('Elsa.')
              and ('.Design' in name or '.Definitions' in name)]
    _require(not design, f'{label} process consumer unexpectedly resolves design packages: {design}')
    feed, reused = _resolve_feed(run_dir, receipt)
    deployment = run_dir / 'deployment'
    executable = deployment / 'Rules.PackageProbe.dll'
    deployment_receipt_path = deployment / 'elsa.rules.deployment-receipt.json'
    direct_path = run_dir / 'direct.json'
    _require(executable.is_file() and deployment_receipt_path.is_file() and direct_path.is_file(),
             f'{label} consumer deployment or direct execution evidence is incomplete.')
    deployment_receipt = _read_json(deployment_receipt_path)
    direct = _read_json(direct_path)
    _require(direct.get('passed') is True and direct.get('packageReferenceOnly') is True
             and PIN_PATTERN.fullmatch(direct.get('identityPinHash', '')) is not None,
             f'{label} direct proof did not record a loaded trusted identity pin.')
    _require(deployment_receipt.get('receiptSchemaVersion') == 1
             and deployment_receipt.get('sourceCommit') == receipt['sourceCommit']
             and deployment_receipt.get('publishStateCommit') == receipt['publishStateCommit'],
             f'{label} deployment receipt provenance differs from its immutable consumer run.')
    pin = deployment_receipt.get('pin')
    _require(isinstance(pin, dict) and pin.get('packages'), f'{label} trusted deployment receipt has no semantic pin.')
    runtime_assets = []
    trusted_assets = []
    for package in pin['packages']:
        for asset in package.get('assets', []):
            relative = Path(asset.get('path', ''))
            asset_path = (deployment / relative).resolve()
            if not asset_path.is_file():
                # Deployment receipts preserve the package-relative asset path while publish
                # flattens runtime assemblies into the application directory.
                asset_path = (deployment / relative.name).resolve()
            _require(asset_path.is_relative_to(deployment.resolve()) and asset_path.is_file(),
                     f'{label} trusted asset is absent from the package deployment: {relative}')
            actual_sha = _sha256(asset_path)
            _require(actual_sha == asset.get('sha256'),
                     f'{label} deployed trusted asset does not match its trusted receipt digest: {relative}')
            trusted_assets.append({'packageId': package.get('id'), 'path': str(asset_path),
                                   'receiptSha256': asset.get('sha256'), 'deployedSha256': actual_sha})
            if package.get('id') == 'Elsa.Rules.Runtime' and asset_path.name == 'Elsa.Rules.Runtime.dll':
                runtime_assets.append(actual_sha)
    _require(len(runtime_assets) == 1, f'{label} trusted deployment pin does not contain one Runtime assembly.')
    for name in ('Rules.PackageProbe.dll', 'Rules.PackageProbe.deps.json', 'Rules.PackageProbe.runtimeconfig.json'):
        path = deployment / name
        _require(path.is_file(), f'{label} package consumer deployment is missing {name}.')
    deployed_files = _deployment_file_hashes(deployment)
    return {
        'label': label, 'runDirectory': str(run_dir), 'runReceiptSha256': _sha256(receipt_path),
        'sourceCommit': receipt['sourceCommit'], 'publishStateCommit': receipt['publishStateCommit'],
        'stage': receipt.get('stage'), 'feedDirectory': str(feed), 'reusedPackReceipt': reused,
        'deploymentDirectory': str(deployment), 'executable': str(executable),
        'deploymentReceiptPath': str(deployment_receipt_path),
        'deploymentReceiptSha256': _sha256(deployment_receipt_path),
        'directEvidencePath': str(direct_path), 'directEvidenceSha256': _sha256(direct_path),
        'directPinHash': direct['identityPinHash'], 'trustedPin': pin,
        'trustedAssets': trusted_assets, 'runtimeAssetSha256': runtime_assets[0],
        'deploymentFiles': deployed_files, 'publishStateCommitValue': receipt['publishStateCommit'],
    }


def _runtime_pin(receipt_pin):
    """Compare the public runtime DTO with the canonical receipt's explicitly named fields."""
    return {
        **{key: value for key, value in receipt_pin.items() if key != 'packages'},
        'packages': [
            {'packageId': package['id'], 'nuGetVersion': package['version'],
             'nuGetContentHash': package['contentHash'],
             'selectedAssets': [
                 {'packageRelativeAssetPath': asset['path'], 'assemblyFullName': asset['assembly'],
                  'assetSha256': asset['sha256']} for asset in package['assets']]}
            for package in receipt_pin['packages']],
    }


def _scrub_log(path, env):
    content = Path(path).read_text(errors='replace')
    connection = env.get('ELSA_RULES_PROCESS_CONNECTION')
    if connection:
        content = content.replace(connection, '[REDACTED CONNECTION]')
        for password in re.findall(r'(?i)(?:^|;)Password=([^;]+)', connection):
            content = content.replace(password, '[REDACTED]')
    Path(path).write_text(content)


def _run_command(receipt, evidence, command, cwd, env, timeout=210):
    number = len(receipt['commands']) + 1
    log = evidence / f'{number:02d}-command.log'
    entry = {'command': list(map(str, command)), 'cwd': str(cwd), 'log': str(log)}
    receipt['commands'].append(entry)
    _write_json(evidence / 'receipt.json', receipt)
    try:
        with log.open('w') as output:
            completed = subprocess.run(list(map(str, command)), cwd=cwd, env=env,
                                       stdout=output, stderr=subprocess.STDOUT, timeout=timeout)
        entry['exitCode'] = completed.returncode
    except subprocess.TimeoutExpired:
        entry['timedOutSeconds'] = timeout
        raise
    finally:
        if log.exists():
            _scrub_log(log, env)
            entry['logSha256'] = _sha256(log)
        _write_json(evidence / 'receipt.json', receipt)
    if completed.returncode != 0:
        raise RuntimeError(f'Upgrade probe command returned {completed.returncode}; see {log}.')


def _read_result(path, command):
    result = _read_json(path)
    _require(result.get('passed') is True and result.get('command') == command,
             f'{command} did not record a passing result.')
    return result


def _assert_preflight(result, role, source_identity, retained_hash, expected_loaded_hash):
    _require(result.get('role') == role and result.get('acceptNewInvocations') is False
             and result.get('workerSuppressedForAdmissionSnapshot') is True,
             f'{role} preflight did not use disabled admission with its worker suppressed.')
    _require(result.get('installedPinHash') == expected_loaded_hash
             and result.get('retainedPinHash') == retained_hash,
             f'{role} preflight loaded an unexpected semantic pin.')
    _require(result.get('exactAdmissionReplay') == 'Replay'
             and result.get('freshAdmission') == 'Unavailable'
             and result.get('freshLookup') == 'NotFound'
             and result.get('snapshotUnchanged') is True,
             f'{role} preflight did not preserve exact replay while denying new admission.')
    database_name = source_identity.get('databaseName')
    if database_name is None and isinstance(source_identity.get('database'), dict):
        database_name = source_identity['database'].get('databaseName')
    _require(result.get('databaseEngine') == 'PostgreSQL'
             and result.get('databaseName') == database_name,
             f'{role} preflight used a different process-witness database.')


def _deployment_environment(base, deployment, evidence, database, port, password):
    env = base.copy()
    env['ELSA_RULES_PUBLISH_STATE_COMMIT'] = deployment['publishStateCommitValue']
    env['ELSA_RULES_PROCESS_CONNECTION'] = (
        f'Host=127.0.0.1;Port={port};Database={database};Username=rules;Password={password};Pooling=False')
    host_cache = evidence / 'host-environments' / deployment['label']
    packages = host_cache / 'nuget-packages'
    http_cache = host_cache / 'nuget-http-cache'
    packages.mkdir(parents=True, exist_ok=True)
    http_cache.mkdir(parents=True, exist_ok=True)
    env['NUGET_PACKAGES'] = str(packages)
    env['NUGET_HTTP_CACHE_PATH'] = str(http_cache)
    return env, {'nugetPackages': str(packages), 'nugetHttpCache': str(http_cache)}


def _clean_environment(source=None):
    environment = os.environ.copy() if source is None else source.copy()
    for name in ('GH_TOKEN', 'GITHUB_TOKEN', 'CustomBeforeDirectoryBuildProps', 'MSBuildExtensionsPath',
                 'MSBuildSDKsPath', 'ELSA_EF_CONNECTION', 'ELSA_EF_SCHEMA'):
        environment.pop(name, None)
    return environment


def _upgrade_harness_hashes():
    return {
        'tools/rules/package_upgrade.py': _sha256(Path(__file__).resolve()),
        'tools/rules/package_adversarial.py': _sha256(Path(package_adversarial.__file__).resolve()),
    }


def _deployment_file_hashes(directory):
    directory = Path(directory)
    return {
        str(path.relative_to(directory)): {'path': str(path), 'sha256': _sha256(path)}
        for path in sorted(directory.rglob('*')) if path.is_file()
    }


def _deployment_files_unchanged(deployments):
    return bool(deployments) and all(
        _deployment_file_hashes(deployment['deploymentDirectory']) == deployment['deploymentFiles']
        for deployment in deployments)


def verify_upgrade(receipt, evidence, baseline_run, successor_run, environment=None):
    """Run an A accepted-boundary -> B common-receipt -> A recovery witness.

    ``baseline_run`` and ``successor_run`` are independent, already verified PackageReference
    consumer runs. This coordinator does not pack or build either deployment.
    """
    evidence_root = Path(evidence).expanduser().resolve()
    evidence_root.mkdir(parents=True, exist_ok=True)
    upgrade_dir = evidence_root / 'upgrade' / ('run-' + uuid.uuid4().hex)
    upgrade_dir.mkdir(parents=True)
    local_receipt = {'schemaVersion': 1, 'commands': [], 'evidenceDirectory': str(upgrade_dir),
                     'qualification': 'retained-compatible-deployment-upgrade-rollback-only',
                     'fullAcceptancePassed': False}
    harness_hashes = _upgrade_harness_hashes()
    local_receipt['harnessSourceSha256'] = harness_hashes
    proof = {'passed': False, 'qualified': False, 'evidenceDirectory': str(upgrade_dir),
             'topology': 'two-independent-package-deployments-on-one-machine-and-one-disposable-postgresql-database',
             'harnessSourceSha256': harness_hashes, 'fullAcceptancePassed': False}
    receipt['retainedUpgradeProof'] = proof
    receipt['retainedUpgradeProofPassed'] = False
    receipt['fullAcceptancePassed'] = False
    container = 'rules-upgrade-' + uuid.uuid4().hex
    password = uuid.uuid4().hex + uuid.uuid4().hex
    env = _clean_environment(environment)
    docker_env = env.copy()
    docker_env['POSTGRES_PASSWORD'] = password
    launch_attempted = False
    container_created = False
    containers = {'name': container, 'image': 'postgres:16.15', 'cleanupAttempted': False,
                  'removed': False}
    local_receipt['ownedPostgresContainer'] = containers
    proof['ownedPostgresContainer'] = containers

    def docker(*args, check=True):
        completed = subprocess.run(['docker', *map(str, args)], env=docker_env, text=True,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        output = completed.stdout.replace(password, '[REDACTED]')
        if check and completed.returncode:
            raise RuntimeError('Owned PostgreSQL container operation failed: ' + output[-3000:])
        return completed.returncode, output

    try:
        baseline = _validate_deployment(baseline_run, 'baseline')
        successor = _validate_deployment(successor_run, 'successor')
        _require(baseline['sourceCommit'] != successor['sourceCommit'],
                 'Baseline and successor must use distinct immutable source commits.')
        _require(baseline['publishStateCommit'] == successor['publishStateCommit'],
                 'Baseline and successor package versions must use the same immutable publish-state input.')
        _require(baseline['directPinHash'] != successor['directPinHash'],
                 'The independent direct consumers did not load different trusted semantic pin identities.')
        _require(baseline['runtimeAssetSha256'] != successor['runtimeAssetSha256'],
                 'The independent deployments do not contain different trusted Runtime assets.')
        baseline_pin = baseline['trustedPin']
        successor_pin = successor['trustedPin']
        for field in ('pinSchemaVersion', 'languageId', 'languageVersion', 'receiptSchemaVersion', 'stateSchemaVersion'):
            _require(baseline_pin.get(field) == successor_pin.get(field),
                     'Successor deployment lacks a retained common semantic schema: ' + field)
        proof['deployments'] = {
            'baseline': baseline,
            'successor': successor,
            'distinctSourceCommits': True,
            'samePublishStateCommit': baseline['publishStateCommit'],
            'distinctLoadedPinHashes': [baseline['directPinHash'], successor['directPinHash']],
            'distinctRuntimeAssetSha256': [baseline['runtimeAssetSha256'], successor['runtimeAssetSha256']],
            'commonSchemas': {field: baseline_pin.get(field) for field in
                              ('pinSchemaVersion', 'languageId', 'languageVersion',
                               'receiptSchemaVersion', 'stateSchemaVersion')},
        }
        local_receipt['deployments'] = proof['deployments']
        _write_json(upgrade_dir / 'receipt.json', local_receipt)

        launch_attempted = True
        code, output = docker('run', '--detach', '--name', container, '--publish', '127.0.0.1::5432',
                              '--env', 'POSTGRES_PASSWORD', '--env', 'POSTGRES_USER=rules',
                              '--env', 'POSTGRES_DB=rules', 'postgres:16.15')
        container_created = code == 0
        _require(container_created, 'Could not create the owned PostgreSQL evidence container.')
        containers['containerId'] = output.strip()
        _, image = docker('inspect', '--format', '{{.Image}}', container)
        containers['imageId'] = image.strip()
        _, ports = docker('inspect', '--format', '{{json .NetworkSettings.Ports}}', container)
        port_info = json.loads(ports)
        port = int(port_info['5432/tcp'][0]['HostPort'])
        deadline = time.monotonic() + 60
        while True:
            ready, _ = docker('exec', container, 'pg_isready', '-h', '127.0.0.1', '-U', 'rules', check=False)
            if ready == 0:
                break
            if time.monotonic() >= deadline:
                raise RuntimeError('Owned PostgreSQL container did not become ready.')
            time.sleep(.25)
        _, server_version = docker('exec', container, 'psql', '-U', 'rules', '-d', 'rules', '-Atc', 'select version()')
        server_version = server_version.strip()
        database = 'rules_upgrade_' + uuid.uuid4().hex
        docker('exec', container, 'createdb', '-U', 'rules', database)
        proof['database'] = {'engine': 'PostgreSQL', 'serverVersion': server_version,
                             'databaseName': database, 'containerImage': containers['imageId'],
                             'boundPort': port}

        base_env = _clean_environment(environment)
        env_a, cache_a = _deployment_environment(base_env, baseline, upgrade_dir, database, port, password)
        env_b, cache_b = _deployment_environment(base_env, successor, upgrade_dir, database, port, password)
        proof['isolatedRuntimeEnvironments'] = {'baseline': cache_a, 'successor': cache_b}
        _require(cache_a['nugetPackages'] != cache_b['nugetPackages']
                 and cache_a['nugetHttpCache'] != cache_b['nugetHttpCache'],
                 'Baseline and successor must not share consumer cache identity.')
        local_receipt['database'] = proof['database']
        local_receipt['isolatedRuntimeEnvironments'] = proof['isolatedRuntimeEnvironments']
        _write_json(upgrade_dir / 'receipt.json', local_receipt)

        state = upgrade_dir / 'baseline-accepted-state.json'
        origin_result = upgrade_dir / 'baseline-origin-result.json'
        origin = ObservedProcess(local_receipt,
            ['dotnet', baseline['executable'], 'process-boundary', 'accepted', state, origin_result],
            baseline['deploymentDirectory'], env_a, upgrade_dir / 'baseline-origin.log')
        try:
            marker = origin.wait_marker(state, database)
            _require(marker.get('boundary') == 'accepted' and marker.get('databaseEngine') == 'PostgreSQL'
                     and marker.get('databaseName') == database,
                     'Baseline did not create a real accepted-unconsumed PostgreSQL boundary.')
            proof['baselineOriginProcessId'] = origin.process.pid
            proof['acceptedBoundary'] = marker
            origin.terminate_at_boundary()
        finally:
            origin.close()
        _write_json(upgrade_dir / 'receipt.json', local_receipt)

        baseline_pin_hash = baseline['directPinHash']
        successor_pin_hash = successor['directPinHash']
        b_preflight_path = upgrade_dir / 'successor-admission.json'
        _run_command(local_receipt, upgrade_dir,
            ['dotnet', successor['executable'], 'process-upgrade-admission', 'successor', state, b_preflight_path],
            successor['deploymentDirectory'], env_b)
        b_preflight = _read_result(b_preflight_path, 'process-upgrade-admission')
        _assert_preflight(b_preflight, 'successor', proof, baseline_pin_hash, successor_pin_hash)
        _require(b_preflight.get('installedPin') == _runtime_pin(successor_pin),
                 'Successor preflight did not report the actual trusted deployment pin.')
        _require(b_preflight.get('retainedPinHash') == baseline_pin_hash,
                 'Successor preflight does not show the original retained pin.')
        _require(b_preflight.get('snapshotBefore') == b_preflight.get('snapshotAfterReplay')
                 == b_preflight.get('snapshotAfterFreshAdmission'),
                 'Successor admission checks changed a full retained-state snapshot.')

        b_result_path = upgrade_dir / 'successor-worker.json'
        _run_command(local_receipt, upgrade_dir,
            ['dotnet', successor['executable'], 'process-upgrade-successor', state, b_preflight_path, b_result_path],
            successor['deploymentDirectory'], env_b, timeout=180)
        b_result = _read_result(b_result_path, 'process-upgrade-successor')
        _require(b_result.get('acceptNewInvocations') is False and b_result.get('workerEnabled') is True
                 and b_result.get('commonReceiptConsumed') is True
                 and b_result.get('installedPinHash') == successor_pin_hash
                 and b_result.get('retainedPinHash') == baseline_pin_hash,
                 'Incompatible successor did not consume the retained common receipt under its own pin.')
        _require(b_result.get('semanticEvaluationAfterReceipt') is False
                 and b_result.get('successorClaimDisposition') == 'NoEligibleWork'
                 and b_result.get('successorClaimTokenAbsent') is True
                 and b_result.get('noEligibleSemanticEvaluationClaim') is True
                 and b_result.get('compatibleClaimControlDisposition') == 'Claimed'
                 and b_result.get('compatibleClaimControlExpectedModelRevision') == 3
                 and b_result.get('compatibleClaimControlLeaseRelease') == 'Released'
                 and b_result.get('stateStableAfterReceiptConsumption') is True,
                 'Successor was not shown to have no eligible semantic evaluation after common receipt consumption.')
        _require(b_result.get('snapshotAfterReceiptConsumption') == b_result.get('snapshotAfterStabilityRead')
                 == b_result.get('snapshotAfterSuccessorClaimCheck'),
                 'Successor mutated retained state after consuming the common receipt.')
        _require(b_result.get('snapshotAfterReceiptConsumption', {}).get('view', {}).get('implementationPinHash')
                 == baseline_pin_hash,
                 'Successor repinned the retained run to its own implementation.')

        a_preflight_path = upgrade_dir / 'rollback-admission.json'
        _run_command(local_receipt, upgrade_dir,
            ['dotnet', baseline['executable'], 'process-upgrade-admission', 'rollback', state, a_preflight_path],
            baseline['deploymentDirectory'], env_a)
        a_preflight = _read_result(a_preflight_path, 'process-upgrade-admission')
        _assert_preflight(a_preflight, 'rollback', proof, baseline_pin_hash, baseline_pin_hash)
        _require(a_preflight.get('installedPin') == _runtime_pin(baseline_pin)
                 and a_preflight.get('snapshotBefore') == b_result.get('snapshotAfterSuccessorClaimCheck')
                 and a_preflight.get('snapshotBefore') == a_preflight.get('snapshotAfterFreshAdmission'),
                 'Retained baseline did not recover the unchanged post-successor state under its original pin.')

        a_result_path = upgrade_dir / 'rollback-recovery.json'
        _run_command(local_receipt, upgrade_dir,
            ['dotnet', baseline['executable'], 'process-upgrade-recover', state, a_result_path],
            baseline['deploymentDirectory'], env_a, timeout=210)
        a_result = _read_result(a_result_path, 'process-upgrade-recover')
        _require(a_result.get('acceptNewInvocations') is False
                 and a_result.get('exactAdmissionReplay') == 'Replay'
                 and a_result.get('freshAdmission') == 'Unavailable'
                 and a_result.get('freshLookup') == 'NotFound'
                 and a_result.get('loadedImplementationPinHash') == baseline_pin_hash
                 and a_result.get('retainedImplementationPinHash') == baseline_pin_hash,
                 'Retained compatible recovery did not keep new admission disabled and replay the original invocation.')
        _require(a_result.get('databaseEngine') == 'PostgreSQL' and a_result.get('databaseName') == database
                 and a_result.get('originalProcessId') == origin.process.pid
                 and a_result.get('processId') != origin.process.pid
                 and a_result.get('invocationFingerprint') == marker.get('invocationFingerprint'),
                 'Retained recovery did not resume the original invocation in a new process on the same database.')
        rounds = a_result.get('roundHistory', [])
        _require(a_result.get('status') == 'Succeeded' and a_result.get('committedRounds') == 4
                 and a_result.get('finalFacts') == EXPECTED_FACTS and len(rounds) == 4
                 and [item.get('ordinal') for item in rounds] == [0, 1, 2, 3]
                 and len({item.get('receiptId') for item in rounds}) == 4
                 and len({item.get('acceptedAttemptId') for item in rounds}) == 4
                 and all(item.get('consumedModelRevision') is not None for item in rounds)
                 and rounds[0].get('receiptId') == marker.get('receiptId')
                 and rounds[0].get('semanticReceiptSha256') == marker.get('semanticReceiptSha256')
                 and a_result.get('terminalStabilityVerified') is True
                 and a_result.get('recoveredSnapshot', {}).get('view', {}).get('implementationPinHash') == baseline_pin_hash,
                 'Retained baseline did not produce the exact unique four-round terminal history.')

        proof.update({
            'passed': True,
            'qualified': True,
            'successorAdmissionPreflight': b_preflight,
            'successorWorker': b_result,
            'rollbackAdmissionPreflight': a_preflight,
            'rollbackRecovery': a_result,
            'snapshotBeforeUpgrade': b_preflight.get('snapshotBefore'),
            'snapshotAfterSuccessor': b_result.get('snapshotAfterSuccessorClaimCheck'),
            'snapshotAfterRollback': a_result.get('recoveredSnapshot'),
            'fullAcceptancePassed': False,
        })
        receipt['retainedUpgradeProofPassed'] = True
        receipt['retainedUpgradeEvidenceDirectory'] = str(upgrade_dir)
        local_receipt['proof'] = proof
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError,
            AssertionError, zipfile.BadZipFile) as error:
        proof['error'] = str(error) or type(error).__name__
        local_receipt['error'] = proof['error']
        raise
    finally:
        if launch_attempted:
            containers['cleanupAttempted'] = True
            try:
                code, output = docker('rm', '--force', '--volumes', container, check=False)
                containers['removed'] = code == 0
                containers['cleanupExitCode'] = code
                if code != 0:
                    proof['qualified'] = False
                    proof['passed'] = False
                    receipt['retainedUpgradeProofPassed'] = False
                    proof['cleanupError'] = output[-3000:]
            except (OSError, subprocess.SubprocessError) as error:
                containers['removed'] = False
                proof['qualified'] = False
                proof['passed'] = False
                receipt['retainedUpgradeProofPassed'] = False
                proof['cleanupError'] = str(error)
        harness_unchanged = _upgrade_harness_hashes() == harness_hashes
        deployment_records = ([proof['deployments'][name] for name in ('baseline', 'successor')]
                              if isinstance(proof.get('deployments'), dict) else [])
        deployment_files_unchanged = _deployment_files_unchanged(deployment_records)
        proof['harnessUnchanged'] = harness_unchanged
        proof['deploymentFilesUnchanged'] = deployment_files_unchanged
        local_receipt['harnessUnchanged'] = harness_unchanged
        local_receipt['deploymentFilesUnchanged'] = deployment_files_unchanged
        if not harness_unchanged or not deployment_files_unchanged:
            proof['passed'] = False
            proof['qualified'] = False
            proof['error'] = 'Upgrade coordinator harness or packaged deployment files changed during the witness.'
            receipt['retainedUpgradeProofPassed'] = False
        local_receipt['proof'] = proof
        local_receipt['commands'] = local_receipt.get('commands', [])
        _write_json(upgrade_dir / 'receipt.json', local_receipt)
        if proof.get('qualified') is not True:
            receipt['retainedUpgradeProofPassed'] = False
        receipt['retainedUpgradeProof'] = proof

    return proof


def _base_environment():
    return _clean_environment()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-run', required=True, type=Path)
    parser.add_argument('--successor-run', required=True, type=Path)
    parser.add_argument('--evidence-dir', required=True, type=Path)
    args = parser.parse_args(argv)
    evidence = args.evidence_dir.expanduser().resolve()
    evidence.mkdir(parents=True, exist_ok=True)
    receipt = {'schemaVersion': 1, 'commands': [], 'fullAcceptancePassed': False}
    try:
        proof = verify_upgrade(receipt, evidence, args.baseline_run, args.successor_run, _base_environment())
        _write_json(evidence / 'receipt.json', receipt)
        print('Retained Rules upgrade evidence: ' + proof['evidenceDirectory'])
        return 0 if proof.get('qualified') is True else 1
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError, AssertionError) as error:
        receipt['error'] = str(error) or type(error).__name__
        _write_json(evidence / 'receipt.json', receipt)
        print(receipt['error'], file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())

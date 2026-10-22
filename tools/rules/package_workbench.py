"""Build the immutable-source Workbench host against the packed Rules module roots."""
import base64
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import zipfile
import xml.etree.ElementTree as ET


RULES_MODULES = (
    'Elsa.Rules.Core',
    'Elsa.Rules.Persistence.Core',
    'Elsa.Rules.Runtime',
    'Elsa.Rules.Persistence.EntityFrameworkCore',
    'Elsa.Rules.Workflows.Design',
    'Elsa.Rules.Workflows.Runtime',
)
SEMANTIC_PACKAGES = (
    'Elsa.Rules.Core',
    'Elsa.Rules.Persistence.Core',
    'Elsa.Rules.Runtime',
)
RECEIPT_FILE_NAME = 'elsa.rules.deployment-receipt.json'


def verify_workbench_package_host(receipt, evidence, source, feed, run, environment, probe_deployment) -> dict:
    """Publish Workbench with exact packed Rules roots and verify its deployed package assets.

    This proves package-host restore/publish and identity-receipt compatibility. It does not
    execute a native Rules workflow in Workbench.
    """
    evidence, source, feed, probe_deployment = map(Path, (evidence, source, feed, probe_deployment))
    original_project = source / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj'
    if not original_project.is_file():
        raise ValueError('Immutable source does not contain the expected Workbench project: ' + str(original_project))
    module_roots = receipt.get('modules')
    if not isinstance(module_roots, list) or len(module_roots) != len(RULES_MODULES) or set(module_roots) != set(RULES_MODULES):
        raise ValueError('Workbench package-host proof requires all six Rules module roots.')

    versions = {item['id']: item['version'] for item in receipt['closure']}
    if len(versions) != len(receipt['closure']):
        raise ValueError('Computed package closure contains duplicate IDs.')
    missing = [package for package in RULES_MODULES if package not in versions]
    if missing:
        raise ValueError('Rules module roots are absent from the computed package closure: ' + ', '.join(missing))
    package_records = _unique_by_id(receipt['packages'], 'id', 'pack receipt packages')
    closure_package_ids = set(versions)
    if not closure_package_ids.issubset(package_records):
        raise ValueError('Pack receipt is missing package archive records for its computed closure.')

    workbench_evidence = evidence / 'workbench-package-host'
    workbench_evidence.mkdir(parents=True)
    props_path = workbench_evidence / 'RulesPackageReferences.props'
    lock_path = workbench_evidence / 'packages.lock.json'
    config_path = workbench_evidence / 'NuGet.Config'
    deployment = workbench_evidence / 'deployment'
    deployment.mkdir()
    package_cache = workbench_evidence / 'nuget-packages'
    http_cache = workbench_evidence / 'nuget-http-cache'

    props = ET.Element('Project')
    properties = ET.SubElement(props, 'PropertyGroup')
    ET.SubElement(properties, 'RestorePackagesWithLockFile').text = 'true'
    ET.SubElement(properties, 'NuGetLockFilePath').text = str(lock_path)
    references = ET.SubElement(props, 'ItemGroup')
    for package in RULES_MODULES:
        ET.SubElement(references, 'PackageReference', Include=package,
                      VersionOverride='[' + versions[package] + ']')
    ET.indent(props)
    ET.ElementTree(props).write(props_path, encoding='unicode', xml_declaration=True)

    # Keep the production dependency graph unconditional. The qualification host is a copy at
    # the same directory depth, so ordinary relative references/imports and host sources retain
    # their meaning. Only its direct Rules references are replaced with exact package roots.
    workbench_source = original_project.parent.with_name('RulesPackageWorkbench')
    shutil.copytree(original_project.parent, workbench_source,
                    ignore=shutil.ignore_patterns('bin', 'obj'))
    workbench_project = workbench_source / original_project.name
    project = ET.parse(workbench_project)
    replaced = []
    for group in project.findall('ItemGroup'):
        for reference in list(group.findall('ProjectReference')):
            include = reference.get('Include', '').replace('\\', '/')
            if '/Extensions/Rules/' not in include:
                continue
            module = PurePosixPath(include).stem
            if module not in RULES_MODULES or group.get('Condition') or reference.get('Condition'):
                raise ValueError('Unexpected or conditional Rules host reference: ' + include)
            replaced.append(module)
            group.remove(reference)
    expected_references = set(RULES_MODULES) - {'Elsa.Rules.Core', 'Elsa.Rules.Persistence.Core'}
    if len(replaced) != len(expected_references) or set(replaced) != expected_references:
        raise ValueError('The qualification host must replace exactly the four direct Rules project references.')
    ET.SubElement(project.getroot(), 'Import', Project=str(props_path.resolve()))
    ET.indent(project)
    project.write(workbench_project, encoding='unicode', xml_declaration=True)
    if (workbench_source / 'Program.cs').read_bytes() != (original_project.parent / 'Program.cs').read_bytes():
        raise ValueError('Qualification host entry point differs from immutable Workbench source.')

    nuget_config = ET.parse(source / 'NuGet.Config')
    sources = nuget_config.find('packageSources')
    mappings = nuget_config.find('packageSourceMapping')
    if sources is None or mappings is None:
        raise ValueError('Source NuGet.Config must define packageSources and packageSourceMapping.')
    source_key = 'RulesPackageEvidence'
    if any(item.attrib.get('key') == source_key for item in sources.findall('add')) or any(
            item.attrib.get('key') == source_key for item in mappings.findall('packageSource')):
        raise ValueError('Source NuGet.Config already uses the reserved local feed key.')
    ET.SubElement(sources, 'add', key=source_key, value=str(feed.resolve()))
    local_mapping = ET.SubElement(mappings, 'packageSource', key=source_key)
    for package in sorted(closure_package_ids):
        ET.SubElement(local_mapping, 'package', pattern=package)
    ET.indent(nuget_config)
    nuget_config.write(config_path, encoding='unicode', xml_declaration=True)

    computed_props = feed.parent / 'package-versions.props'
    if not computed_props.is_file():
        raise ValueError('The immutable package run is missing its computed version properties: ' + str(computed_props))
    property_args = [
        '-p:CustomBeforeDirectoryBuildProps=' + str(computed_props.resolve()),
    ]
    env = environment.copy()
    env['NUGET_PACKAGES'] = str(package_cache)
    env['NUGET_HTTP_CACHE_PATH'] = str(http_cache)
    for variable in ('GH_TOKEN', 'GITHUB_TOKEN', 'ELSA_EF_CONNECTION', 'ELSA_RULES_PROCESS_CONNECTION'):
        env.pop(variable, None)
    run(['dotnet', 'restore', workbench_project, '--configfile', config_path, *property_args], cwd=source, env=env)

    assets_path = workbench_source / 'obj/project.assets.json'
    if not assets_path.is_file():
        raise ValueError('Workbench restore did not produce project.assets.json.')
    assets = json.loads(assets_path.read_text())
    asset_target_name, asset_target = _active_target(assets, 'Workbench project.assets.json')
    asset_libraries = assets.get('libraries')
    if not isinstance(asset_libraries, dict):
        raise ValueError('Workbench project.assets.json has no libraries map.')
    _assert_rules_are_packages(asset_libraries, versions, 'Workbench project.assets.json')
    project_libraries = [key for key, value in asset_libraries.items() if value.get('type') == 'project']
    # Workbench intentionally remains source-built apart from the six Rules roots.
    asset_records = _verify_module_packages(
        RULES_MODULES, versions, receipt['sourceCommit'], package_records, asset_target_name, asset_target, asset_libraries,
        package_cache, feed, None)

    run(['dotnet', 'restore', workbench_project, '--locked-mode', '--configfile', config_path,
         *property_args], cwd=source, env=env)
    run(['dotnet', 'publish', workbench_project, '-c', 'Release', '--no-restore',
         '--output', deployment, *property_args], cwd=source, env=env)
    deps_path = deployment / 'Elsa.Workbench.deps.json'
    if not deps_path.is_file():
        raise ValueError('Workbench publish did not produce Elsa.Workbench.deps.json.')
    deps = json.loads(deps_path.read_text())
    deps_target_name, deps_target = _active_target(deps, 'published Workbench deps.json')
    deps_libraries = deps.get('libraries')
    if not isinstance(deps_libraries, dict):
        raise ValueError('Published Workbench deps.json has no libraries map.')
    _assert_rules_are_packages(deps_libraries, versions, 'published Workbench deps.json')
    deployed_records = _verify_module_packages(
        RULES_MODULES, versions, receipt['sourceCommit'], package_records, deps_target_name, deps_target, deps_libraries,
        package_cache, feed, deployment)

    probe_receipt_path = probe_deployment / RECEIPT_FILE_NAME
    if not probe_receipt_path.is_file():
        raise ValueError('PackageProbe deployment receipt is missing: ' + str(probe_receipt_path))
    probe_receipt_bytes = probe_receipt_path.read_bytes()
    probe_receipt = json.loads(probe_receipt_bytes)
    semantic_assets = _verify_probe_identity(receipt, probe_receipt, probe_deployment, deployed_records)

    workbench_receipt_path = deployment / RECEIPT_FILE_NAME
    if workbench_receipt_path.exists():
        raise ValueError('Workbench publish unexpectedly contains a deployment receipt before verification.')
    # The trusted receipt is copied only after Workbench's selected assets match its proof inputs.
    shutil.copyfile(probe_receipt_path, workbench_receipt_path)
    if workbench_receipt_path.read_bytes() != probe_receipt_bytes:
        raise ValueError('Copied Workbench deployment receipt differs from the verified PackageProbe receipt.')

    manifest = {
        'passed': True,
        'proofScope': ('Published Workbench package host resolves all six Rules modules from the computed local feed; '
                       'selected module assets and the three-package semantic identity receipt match PackageProbe. '
                       'Native Rules execution in Workbench is not covered.'),
        'nativeExecutionPassed': False,
        'sourceCommit': receipt['sourceCommit'],
        'workbenchProject': str(workbench_project),
        'workbenchSourceSha256': {
            'project': _sha256(original_project),
            'program': _sha256(original_project.parent / 'Program.cs'),
        },
        'qualificationProjectSha256': _sha256(workbench_project),
        'qualificationProgramSha256': _sha256(workbench_source / 'Program.cs'),
        'replacedProjectReferences': sorted(replaced),
        'moduleVersions': {package: versions[package] for package in RULES_MODULES},
        'sourceProjectLibraryCount': len(project_libraries),
        'projectAssetsRulesModules': asset_records,
        'publishedRulesModules': deployed_records,
        'semanticReceiptPackages': semantic_assets,
        'deploymentReceipt': str(workbench_receipt_path),
        'deploymentReceiptSha256': _sha256(workbench_receipt_path),
        'rulesPackageReferencesPropsSha256': _sha256(props_path),
        'nuGetConfigSha256': _sha256(config_path),
        'nuGetLockFileSha256': _sha256(lock_path),
    }
    manifest_path = workbench_evidence / 'workbench-package-host.json'
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    manifest['manifestPath'] = str(manifest_path)
    manifest['manifestSha256'] = _sha256(manifest_path)
    return manifest


def _verify_module_packages(module_ids, versions, source_commit, package_records, target_name, active_target, libraries,
                            package_cache, feed, deployment):
    target_entries = _index_case_insensitive(active_target, 'active deps target')
    library_entries = _index_case_insensitive(libraries, 'active deps libraries')
    cache_root = package_cache.resolve()
    feed_root = feed.resolve()
    result = []
    for package in module_ids:
        version = versions[package]
        key = package + '/' + version
        library = library_entries.get(key.casefold())
        target = target_entries.get(key.casefold())
        if not isinstance(library, dict) or library.get('type') != 'package' or not isinstance(target, dict):
            raise ValueError('Workbench must resolve packed Rules library as a package: ' + key)
        record = package_records[package]
        if record.get('version') != version:
            raise ValueError('Package archive record version differs from computed closure: ' + key)
        if record.get('sourceCommit') != source_commit:
            raise ValueError('Package archive provenance differs from the immutable source commit: ' + key)
        archive = Path(record['path']).resolve()
        if archive.parent != feed_root or not archive.is_file():
            raise ValueError('Packed Rules archive is outside the immutable feed: ' + key)
        archive_sha512 = _sha512_base64(archive)
        if archive_sha512 != record['archiveSha512']:
            raise ValueError('Packed Rules archive changed after pack receipt creation: ' + key)
        package_cache_path = _canonical_cache_path(cache_root, library.get('path'))
        cache_archive = package_cache_path / (package.lower() + '.' + version.lower() + '.nupkg')
        if not cache_archive.is_file() or _sha512_base64(cache_archive) != archive_sha512:
            raise ValueError('Restored Rules cache archive differs from the packed local feed: ' + key)
        cache_marker = cache_archive.with_name(cache_archive.name + '.sha512')
        if not cache_marker.is_file():
            raise ValueError('Restored Rules cache content hash marker is missing: ' + key)
        cache_content_hash = _normalize_sha512(cache_marker.read_text().strip())
        deps_sha512 = _normalize_sha512(library.get('sha512', ''))
        if deps_sha512 != cache_content_hash:
            raise ValueError('Workbench active deps content hash differs from its restored NuGet cache marker: ' + key)

        assembly_name = package + '.dll'
        selected = _selected_runtime_assets(target, target_name)
        assembly_assets = [asset for asset in selected if PurePosixPath(asset).name.casefold() == assembly_name.casefold()]
        if len(assembly_assets) != 1:
            raise ValueError('Workbench deps must select exactly one Rules module assembly asset: ' + key)
        asset_path = assembly_assets[0]
        package_asset_hash = _zip_entry_sha256(archive, asset_path)
        deployed_hash = None
        if deployment is not None:
            deployed_asset = deployment / assembly_name
            if not deployed_asset.is_file():
                raise ValueError('Workbench deployment is missing selected Rules module DLL: ' + assembly_name)
            deployed_hash = _sha256(deployed_asset)
            if deployed_hash != package_asset_hash:
                raise ValueError('Deployed Rules module DLL differs from its selected local package asset: ' + key)
        result.append({
            'id': package,
            'version': version,
            'contentHash': deps_sha512,
            'archiveSha512': archive_sha512,
            'selectedRuntimeAssets': selected,
            'assemblyAsset': asset_path,
            'assemblySha256': package_asset_hash,
            'deployedAssemblySha256': deployed_hash,
        })
    return result


def _verify_probe_identity(receipt, probe_receipt, probe_deployment, deployed_records):
    if probe_receipt.get('receiptSchemaVersion') != 1:
        raise ValueError('PackageProbe identity receipt has an unsupported schema.')
    for field in ('sourceCommit', 'publishStateCommit'):
        if probe_receipt.get(field) != receipt.get(field):
            raise ValueError('PackageProbe identity receipt does not match the immutable pack ' + field + '.')
    probe_pin = probe_receipt.get('pin')
    probe_provenance = probe_receipt.get('provenance', {}).get('dependencyResolution', {}).get('packages')
    if not isinstance(probe_pin, dict) or not isinstance(probe_provenance, list):
        raise ValueError('PackageProbe receipt is missing its pin or dependency provenance.')
    pin_packages = _unique_by_id(probe_pin.get('packages', []), 'id', 'PackageProbe pin packages')
    provenance_packages = _unique_by_id(probe_provenance, 'id', 'PackageProbe provenance packages')
    pack_packages = _unique_by_id(receipt['packages'], 'id', 'pack receipt packages')
    host_by_id = {item['id']: item for item in deployed_records}
    probe_assets = []
    for package in SEMANTIC_PACKAGES:
        pin = pin_packages.get(package)
        provenance = provenance_packages.get(package)
        host = host_by_id[package]
        if not isinstance(pin, dict) or not isinstance(provenance, dict):
            raise ValueError('PackageProbe receipt omits semantic Rules package: ' + package)
        if pin.get('version') != host['version'] or provenance.get('version') != host['version']:
            raise ValueError('Workbench and PackageProbe semantic package versions differ: ' + package)
        if pin.get('contentHash') != host['contentHash'] or provenance.get('nuGetContentHash') != host['contentHash']:
            raise ValueError('Workbench and PackageProbe semantic package content hashes differ: ' + package)
        expected_archive_hash = pack_packages[package]['archiveSha512']
        if provenance.get('rawArchiveSha512Base64') != expected_archive_hash or \
                provenance.get('restoredArchiveSha512Base64') != expected_archive_hash:
            raise ValueError('PackageProbe semantic raw archive hashes differ from the packed feed: ' + package)
        if provenance.get('type') != 'package':
            raise ValueError('PackageProbe semantic dependency is not a package library: ' + package)
        selected = provenance.get('selectedRuntimeAssets')
        if not isinstance(selected, list) or selected != [host['assemblyAsset']]:
            raise ValueError('Workbench and PackageProbe selected semantic runtime assets differ: ' + package)
        assets = pin.get('assets')
        if not isinstance(assets, list) or len(assets) != 1:
            raise ValueError('PackageProbe semantic pin must contain exactly one selected assembly: ' + package)
        asset = assets[0]
        if asset.get('path') != host['assemblyAsset'] or asset.get('sha256') != host['deployedAssemblySha256']:
            raise ValueError('Workbench semantic asset differs from the pinned PackageProbe asset: ' + package)
        if not asset.get('assembly'):
            raise ValueError('PackageProbe semantic asset is missing its assembly identity: ' + package)
        probe_asset = probe_deployment / (package + '.dll')
        if not probe_asset.is_file() or _sha256(probe_asset) != host['deployedAssemblySha256']:
            raise ValueError('Workbench semantic deployed DLL differs byte-for-byte from PackageProbe: ' + package)
        probe_assets.append({
            'id': package,
            'version': host['version'],
            'contentHash': host['contentHash'],
            'selectedRuntimeAssets': selected,
            'assembly': asset['assembly'],
            'assemblySha256': asset['sha256'],
        })
    if set(pin_packages) != set(SEMANTIC_PACKAGES) or set(provenance_packages) != set(SEMANTIC_PACKAGES):
        raise ValueError('PackageProbe receipt contains an unexpected semantic identity package set.')
    return probe_assets


def _active_target(document, label):
    runtime_target = document.get('runtimeTarget')
    targets = document.get('targets')
    if not isinstance(targets, dict) or not targets:
        raise ValueError(label + ' is missing its dependency targets.')
    if runtime_target is None:
        # project.assets.json uses a target map without runtimeTarget. Accept only an explicit,
        # unambiguous single-framework graph instead of guessing among target variants.
        frameworks = document.get('project', {}).get('frameworks')
        if (not isinstance(frameworks, dict) or len(frameworks) != 1 or len(targets) != 1):
            raise ValueError(label + ' has no runtimeTarget and is not a single-framework graph.')
        target_name = next(iter(targets))
        framework = next(iter(frameworks))
        if target_name != framework and not target_name.startswith(framework + '/'):
            raise ValueError(label + ' single target does not match its sole declared framework.')
    elif isinstance(runtime_target, dict) and isinstance(runtime_target.get('name'), str):
        target_name = runtime_target['name']
    else:
        raise ValueError(label + ' has a malformed active runtime target.')
    if target_name not in targets or not isinstance(targets[target_name], dict):
        raise ValueError(label + ' has no target matching its active runtime target.')
    return target_name, targets[target_name]


def _assert_rules_are_packages(libraries, versions, label):
    expected = {package.casefold(): version for package, version in versions.items() if package in RULES_MODULES}
    found = set()
    for identity, metadata in libraries.items():
        if not isinstance(identity, str) or '/' not in identity:
            continue
        package, version = identity.rsplit('/', 1)
        folded = package.casefold()
        if folded not in expected:
            continue
        if not isinstance(metadata, dict) or metadata.get('type') != 'package' or version != expected[folded]:
            raise ValueError(label + ' contains a non-package or mismatched Rules library: ' + identity)
        if folded in found:
            raise ValueError(label + ' contains multiple Rules module versions: ' + package)
        found.add(folded)
    missing = [package for package in RULES_MODULES if package.casefold() not in found]
    if missing:
        raise ValueError(label + ' is missing package-resolved Rules modules: ' + ', '.join(missing))


def _selected_runtime_assets(target, target_name):
    assets = set()
    runtime = target.get('runtime', {})
    if not isinstance(runtime, dict):
        raise ValueError('A Rules runtime asset map is malformed.')
    for path in runtime:
        if _is_dll_asset(path):
            assets.add(path)
    runtime_targets = target.get('runtimeTargets', {})
    if not isinstance(runtime_targets, dict):
        raise ValueError('A Rules runtimeTargets map is malformed.')
    target_rid = target_name.rsplit('/', 1)[1] if '/' in target_name else None
    for path, metadata in runtime_targets.items():
        if not isinstance(metadata, dict) or metadata.get('assetType') != 'runtime':
            continue
        rid = metadata.get('rid')
        if rid is not None and (target_rid is None or rid.casefold() != target_rid.casefold()):
            continue
        if _is_dll_asset(path):
            assets.add(path)
    if not assets:
        raise ValueError('A Rules module has no selected runtime DLL assets.')
    return sorted(assets)


def _is_dll_asset(path):
    if not isinstance(path, str):
        return False
    parsed = PurePosixPath(path)
    if parsed.is_absolute() or '..' in parsed.parts or '\\' in path:
        raise ValueError('A Rules deps asset has an unsafe package-relative path.')
    return parsed.name != '_._' and parsed.suffix.casefold() == '.dll'


def _canonical_cache_path(cache_root, relative):
    if not isinstance(relative, str) or not relative:
        raise ValueError('A restored Rules package path is missing from the assets graph.')
    path = Path(relative)
    if path.is_absolute() or '..' in path.parts:
        raise ValueError('A restored Rules package path escapes the isolated NuGet cache.')
    resolved = (cache_root / path).resolve()
    if not resolved.is_relative_to(cache_root):
        raise ValueError('A restored Rules package path escapes the isolated NuGet cache.')
    return resolved


def _index_case_insensitive(mapping, label):
    if not isinstance(mapping, dict):
        raise ValueError(label + ' is malformed.')
    result = {}
    for key, value in mapping.items():
        folded = key.casefold()
        if folded in result:
            raise ValueError(label + ' contains duplicate case-insensitive entries.')
        result[folded] = value
    return result


def _unique_by_id(items, field, label):
    if not isinstance(items, list):
        raise ValueError(label + ' is malformed.')
    result = {}
    for item in items:
        if not isinstance(item, dict) or not isinstance(item.get(field), str):
            raise ValueError(label + ' contains an invalid record.')
        key = item[field]
        if key in result:
            raise ValueError(label + ' contains duplicate package IDs.')
        result[key] = item
    return result


def _normalize_sha512(value):
    if not isinstance(value, str):
        raise ValueError('A NuGet SHA512 content hash is malformed.')
    encoded = value.strip()
    if encoded.startswith('sha512-'):
        encoded = encoded[len('sha512-'):]
    try:
        decoded = base64.b64decode(encoded, validate=True)
    except Exception as error:
        raise ValueError('A NuGet SHA512 content hash is not canonical Base64.') from error
    if len(decoded) != 64 or base64.b64encode(decoded).decode() != encoded:
        raise ValueError('A NuGet SHA512 content hash is not canonical Base64.')
    return encoded


def _sha512_base64(path):
    with Path(path).open('rb') as stream:
        return base64.b64encode(hashlib.file_digest(stream, 'sha512').digest()).decode()


def _sha256(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def _zip_entry_sha256(archive, entry):
    with zipfile.ZipFile(archive) as package:
        try:
            with package.open(entry) as stream:
                digest = hashlib.sha256()
                while chunk := stream.read(1024 * 1024):
                    digest.update(chunk)
                return digest.hexdigest()
        except KeyError as error:
            raise ValueError('Selected Rules runtime asset is absent from its package archive: ' + entry) from error

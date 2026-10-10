import pathlib, json, hashlib, base64, zipfile, sys, os, xml.etree.ElementTree as ET

artifacts = pathlib.Path(__file__).parent
release_root = artifacts.parent
repo = pathlib.Path(os.environ['GITHUB_WORKSPACE'])
expected_cache = pathlib.Path(os.environ['NUGET_PACKAGES']).resolve()
stable_commits = {
    'CShells': ('0.0.30', '789e36cc1cec885d86a48e18b7b51437de519a39'),
    'Nuplane': ('0.0.11', '32776c18ab8263c2ed7a9112ff58a7f3bcc7ead0')
}
family_inputs = {
    'CShells': {
        'audit': release_root / 'cshells-0.0.30/archive-audit.json',
        'archives': release_root / 'cshells-0.0.30/nuget',
        'feedName': 'CShells',
    },
    'Nuplane': {
        'audit': release_root / 'nuplane-0.0.11/archive-audit.json',
        'archives': release_root / 'nuplane-0.0.11/nuget',
        'feedName': 'Nuplane',
    }
}
assert len(sys.argv) == 4, (
    'Usage: verify-project-upstream-assets.py <project.assets.json> '
    '<runtime-output-directory> <output-name.json>'
)
assets = repo / sys.argv[1]
runtime_output = repo / sys.argv[2]
output = artifacts / sys.argv[3]
assert not output.exists(), 'Preserve earlier evidence'
assert assets.is_file(), str(assets)
assert runtime_output.is_dir(), str(runtime_output)

def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def child_case_insensitive(directory, filename):
    matches = [path for path in directory.iterdir() if path.name.casefold() == filename.casefold()]
    assert len(matches) == 1, (directory, filename, [path.name for path in matches])
    return matches[0]

def package_records():
    records = {}
    expected_tfms = {'net8.0', 'net9.0', 'net10.0'}
    for family, config in family_inputs.items():
        data = json.loads(config['audit'].read_text())
        assert data.get('limitations'), config['audit']
        packages = data['packages']
        expected_count = 10 if family == 'CShells' else 8
        assert len(packages) == expected_count, (family, len(packages))

        if family == 'CShells':
            discovery = data['feed_discovery']['feedz_service_index']
            assert discovery['http_status'] == 200
            feed_url = discovery['url']
            assert data['final_nuget_recheck']['available_package_count'] == expected_count
            assert not data['final_nuget_recheck']['pending_package_ids']
        else:
            discovery = data['feed_discovery']['feedz']
            assert discovery['index_http'] == 200
            feed_url = discovery['index_url']

        for item in packages:
            package_id = item['id']
            version, commit = stable_commits[family]
            assert item['version'] == version, (package_id, item['version'])
            assert package_id.casefold().startswith(family.casefold()), package_id
            assert package_id.casefold() not in records, package_id

            if family == 'CShells':
                nuget = item['nuget_org']
                feedz = item['feedz']
                public_nuspec = nuget['nuspec']
                archive_name = nuget['archive_file']
                nuget_sha = nuget['sha256']
                feedz_sha = feedz['archive_sha256']
                assert nuget['nupkg_http'] == 200 and nuget['version_indexed']
                assert feedz['nupkg_http'] == 200 and feedz['version_index_http'] == 200
                assert feedz['version_indexed'] and feedz['payload_matches_except_signature']
                assert feedz['only_different_entry_names'] == ['.signature.p7s']
                assert public_nuspec['identity_matches_expected']
                assert public_nuspec['frameworks_match_net8_9_10']
                assert public_nuspec['internal_versions_match_cshells_0_0_30_nuplane_0_0_11']
                repo_commit = public_nuspec['repository']['commit']
                public_dependencies = public_nuspec['internal_dependencies']
                tfms = set(public_nuspec['frameworks'])
            else:
                feedz = item['feedz']
                public_nuspec = item['nuspec']
                archive_name = item['archive']
                nuget_sha = item['sha256']
                feedz_sha = feedz['nupkg_sha256']
                assert item['nuget_org']['nupkg_http'] == 200 and item['nuget_org']['version_indexed']
                assert feedz['nupkg_http'] == 200 and feedz['index_http'] == 200 and feedz['version_indexed']
                assert feedz['archive_payload_identical_except_signature']
                assert feedz['only_different_zip_entry'] == '.signature.p7s'
                assert public_nuspec['tfms_match_net8_net9_net10']
                assert public_nuspec['all_internal_dependencies_stable_0_0_11']
                repo_commit = public_nuspec['repository']['commit']
                public_dependencies = public_nuspec['internal_nuplane_dependencies']
                tfms = set(public_nuspec['tfms'])

            assert repo_commit == commit, (package_id, repo_commit)
            assert tfms == expected_tfms, (package_id, tfms)
            public_archive = child_case_insensitive(config['archives'], archive_name)
            assert sha256(public_archive) == nuget_sha, (package_id, 'audited NuGet archive hash')

            records[package_id.casefold()] = {
                'id': package_id,
                'family': family,
                'version': version,
                'commit': commit,
                'nugetSha256': nuget_sha,
                'feedzSha256': feedz_sha,
                'nugetArchive': str(public_archive),
                'feedUrl': feed_url,
                'publicDependencies': public_dependencies,
                'tfms': sorted(tfms),
                'auditNuspec': public_nuspec,
            }
    assert len(records) == 18, len(records)
    return records

audited_packages = package_records()
d = json.loads(assets.read_text())
cache = pathlib.Path(next(iter(d['packageFolders']))).resolve()
assert cache == expected_cache, (cache, expected_cache)
targets = d['targets']
assert 'net10.0' in targets, sorted(targets)
records = []

for key, lib in d['libraries'].items():
    if not key.startswith(('CShells/', 'CShells.', 'Nuplane/', 'Nuplane.')):
        continue
    package, version = key.split('/', 1)
    family = 'CShells' if package.casefold().startswith('cshells') else 'Nuplane'
    release_version, expected_commit = stable_commits[family]
    assert version == release_version, key
    audited = audited_packages.get(package.casefold())
    assert audited is not None and audited['family'] == family, key

    directory = (cache / lib['path']).resolve()
    assert directory.is_relative_to(cache), (directory, cache)
    archive_name = package.lower() + '.' + version + '.nupkg'
    archive = child_case_insensitive(directory, archive_name)
    archive_bytes = archive.read_bytes()
    archive_sha = hashlib.sha256(archive_bytes).hexdigest()
    raw_archive_sha512 = base64.b64encode(hashlib.sha512(archive_bytes).digest()).decode()
    metadata_path = directory / '.nupkg.metadata'
    sha512_path = pathlib.Path(str(archive) + '.sha512')
    metadata = json.loads(metadata_path.read_text())
    source = metadata['source'].rstrip('/')
    allowed_sources = {
        'https://api.nuget.org/v3/index.json': audited['nugetSha256'],
        audited['feedUrl'].rstrip('/'): audited['feedzSha256'],
    }
    assert source in allowed_sources, (key, source)
    assert archive_sha == allowed_sources[source], (key, source, archive_sha)
    # The sidecar hashes the exact signed .nupkg bytes. NuGet's contentHash and
    # assets sha512 are the stable package-content hash, which excludes the signature.
    assert sha512_path.read_text().strip() == raw_archive_sha512, key
    package_content_hash = metadata.get('contentHash')
    assets_content_hash = lib.get('sha512')
    assert package_content_hash and package_content_hash == assets_content_hash, key

    with zipfile.ZipFile(archive) as z:
        assert z.testzip() is None, key
        if '.signature.p7s' not in z.namelist():
            assert package_content_hash == raw_archive_sha512, key
        nuspec_name = next(name for name in z.namelist() if name.lower().endswith('.nuspec'))
        xml = ET.fromstring(z.read(nuspec_name))
        package_metadata = next(node for node in xml.iter() if node.tag.rsplit('}', 1)[-1] == 'metadata')
        nuspec_id = next(node.text for node in package_metadata if node.tag.rsplit('}', 1)[-1] == 'id')
        nuspec_version = next(node.text for node in package_metadata if node.tag.rsplit('}', 1)[-1] == 'version')
        repository = next(node for node in package_metadata if node.tag.rsplit('}', 1)[-1] == 'repository')
        commit = repository.attrib['commit']
        assert nuspec_id.casefold() == package.casefold(), (key, nuspec_id)
        assert nuspec_version == version, (key, nuspec_version)
        assert commit == expected_commit == audited['commit'], (key, commit)
        actual_dependencies = {}
        for node in package_metadata.iter():
            if node.tag.rsplit('}', 1)[-1] != 'dependency':
                continue
            dependency_id = node.attrib['id']
            if dependency_id.casefold().startswith(('cshells', 'nuplane')):
                actual_dependencies.setdefault(dependency_id, set()).add(node.attrib['version'])
        expected_dependencies = {
            dependency_id: set(versions if isinstance(versions, list) else [versions])
            for dependency_id, versions in audited['publicDependencies'].items()
        }
        assert actual_dependencies == expected_dependencies, (key, actual_dependencies, expected_dependencies)

        target = targets['net10.0'].get(key)
        assert target is not None, (key, 'missing net10.0 target')
        runtime = target.get('runtime', {})
        matches = []
        for relative in runtime:
            if not relative.lower().endswith('.dll'):
                continue
            dll = runtime_output / pathlib.Path(relative).name
            packaged = z.read(relative)
            assert dll.is_file() and dll.read_bytes() == packaged, str(dll)
            matches.append({
                'dll': str(dll),
                'archiveAsset': relative,
                'sha256': hashlib.sha256(packaged).hexdigest()
            })

    records.append({
        'package': key,
        'archive': str(archive),
        'publicNugetArchive': audited['nugetArchive'],
        'archiveSha256': archive_sha,
        'archiveSha512': raw_archive_sha512,
        'packageContentHash': package_content_hash,
        'source': source,
        'commit': commit,
        'matchedRuntimeAssets': matches
    })

assert records, 'No reached upstream libraries'
record = {
    'accepted': True,
    'evidenceScope': (
        'Stable released package libraries from the exact public archive audits and built Release net10.0 '
        'output assets; cache archives must match the exact audited NuGet.org or Feedz hash selected by '
        'the recorded package source. This does not itself prove a fresh cache restore.'
    ),
    'assetsPath': str(assets),
    'assetsSha256': hashlib.sha256(assets.read_bytes()).hexdigest(),
    'packageCache': str(cache),
    'stableReleases': {
        'CShells': {'version': '0.0.30', 'commit': stable_commits['CShells'][1]},
        'Nuplane': {'version': '0.0.11', 'commit': stable_commits['Nuplane'][1]}
    },
    'packages': records
}
output.write_text(json.dumps(record, indent=2) + '\n')
print(json.dumps({
    'accepted': True,
    'packages': len(records),
    'builtDlls': sum(len(record['matchedRuntimeAssets']) for record in records)
}))


"""Clean consumer construction for verify-packaged-rules; no repository build inheritance."""
import base64
import hashlib
import json
import re
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET


def verify_consumer(receipt, evidence, source, feed, run, environment):
    consumer, deployment = evidence / 'consumer', evidence / 'deployment'
    consumer.mkdir()
    for ancestor in consumer.parents:
        for name in ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props'):
            if (ancestor / name).exists():
                raise ValueError('Consumer would inherit ambient build configuration: ' + str(ancestor / name))
    template = Path(__file__).with_name('PackageProbe') / 'Program.cs'
    shutil.copyfile(template, consumer / 'Program.cs')
    receipt['consumerTemplateSha256'] = hashlib.sha256(template.read_bytes()).hexdigest()
    shutil.copyfile(source / 'global.json', consumer / 'global.json')
    versions = {p['id']: p['version'] for p in receipt['closure']}
    central = ET.parse(source / 'Directory.Packages.props')
    sqlite = next(p.attrib['Version'] for p in central.findall('.//PackageVersion')
                  if p.attrib['Include'] == 'Microsoft.EntityFrameworkCore.Sqlite')
    project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
    properties = ET.SubElement(project, 'PropertyGroup')
    for key, value in {'OutputType': 'Exe', 'TargetFramework': 'net10.0', 'ImplicitUsings': 'enable',
                       'Nullable': 'enable', 'RestorePackagesWithLockFile': 'true'}.items():
        ET.SubElement(properties, key).text = value
    references = ET.SubElement(project, 'ItemGroup')
    ET.SubElement(references, 'FrameworkReference', Include='Microsoft.AspNetCore.App')
    for package, version in {'Elsa.Rules.Runtime': versions['Elsa.Rules.Runtime'],
                             'Elsa.Rules.Persistence.EntityFrameworkCore': versions['Elsa.Rules.Persistence.EntityFrameworkCore'],
                             'Microsoft.EntityFrameworkCore.Sqlite': sqlite,
                             'NuGet.Packaging': '7.9.0'}.items():
        ET.SubElement(references, 'PackageReference', Include=package, Version='[' + version + ']')
    ET.indent(project)
    ET.ElementTree(project).write(consumer / 'Rules.PackageProbe.csproj', encoding='unicode')
    config = ET.parse(source / 'NuGet.Config')
    ET.SubElement(config.find('packageSources'), 'add', key='LocalRules', value=str(feed))
    mapping = ET.SubElement(config.find('packageSourceMapping'), 'packageSource', key='LocalRules')
    for package in sorted(versions):
        ET.SubElement(mapping, 'package', pattern=package)
    ET.indent(config)
    config.write(consumer / 'NuGet.Config', encoding='unicode')
    env = environment.copy()
    env['ELSA_RULES_PUBLISH_STATE_COMMIT'] = receipt['publishStateCommit']
    env['NUGET_PACKAGES'] = str(evidence / 'consumer-packages')
    env['NUGET_HTTP_CACHE_PATH'] = str(evidence / 'consumer-http-cache')
    run(['dotnet', '--version'], cwd=consumer, env=env)
    sdk_lines = Path(receipt['commands'][-1]['log']).read_text().splitlines()
    sdk_versions = [line.strip() for line in sdk_lines if re.fullmatch(r'\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?', line.strip())]
    if len(sdk_versions) != 1:
        raise ValueError('Could not capture the SDK selected in the consumer build directory.')
    receipt['consumerBuildSdkVersion'] = sdk_versions[0]
    env['ELSA_RULES_CONSUMER_BUILD_SDK'] = sdk_versions[0]
    project_path = consumer / 'Rules.PackageProbe.csproj'
    run(['dotnet', 'restore', project_path, '--configfile', consumer / 'NuGet.Config'], cwd=consumer, env=env)
    assets = json.loads((consumer / 'obj/project.assets.json').read_text())
    libraries = assets['libraries']
    if any(p['type'] == 'project' for p in libraries.values()):
        raise ValueError('Consumer has a direct or transitive ProjectReference.')
    for package, version in versions.items():
        key = package + '/' + version
        if key not in libraries or libraries[key]['type'] != 'package':
            raise ValueError('Consumer did not resolve exact packed package: ' + key)
        restored = Path(env['NUGET_PACKAGES']) / libraries[key]['path'] / (package.lower() + '.' + version.lower() + '.nupkg')
        archive_hash = base64.b64encode(hashlib.sha512(restored.read_bytes()).digest()).decode()
        expected = next(p for p in receipt['packages'] if p['id'] == package)
        if archive_hash != expected['archiveSha512']:
            raise ValueError('Restored archive differs from local packed artifact: ' + key)
    receipt['restoredProjectLibraries'] = 0
    receipt['resolvedPackages'] = {k: v['sha512'] for k, v in libraries.items()}
    run(['dotnet', 'restore', project_path, '--locked-mode', '--configfile', consumer / 'NuGet.Config'], cwd=consumer, env=env)
    run(['dotnet', 'publish', project_path, '-c', 'Release', '--no-restore', '--output', deployment], cwd=consumer, env=env)
    executable = deployment / 'Rules.PackageProbe.dll'
    run(['dotnet', executable, 'receipt', feed, evidence / 'deployment-receipt-evidence.json'], cwd=deployment, env=env)
    run(['dotnet', executable, 'direct', evidence / 'direct.db', evidence / 'direct.json'], cwd=deployment, env=env)
    run(['dotnet', executable, 'incompatible', evidence / 'incompatible.db', evidence / 'incompatible.json'], cwd=deployment, env=env)
    receipt_path = deployment / 'elsa.rules.deployment-receipt.json'
    original = receipt_path.read_bytes()
    try:
        changed = json.loads(original)
        asset = changed['pin']['packages'][0]['assets'][0]
        asset['sha256'] = ('1' if asset['sha256'][0] == '0' else '0') + asset['sha256'][1:]
        receipt_path.write_text(json.dumps(changed))
        run(['dotnet', executable, 'unavailable', evidence / 'unavailable.db', evidence / 'unavailable.json'], cwd=deployment, env=env)
    finally:
        receipt_path.write_bytes(original)
    run(['dotnet', executable, 'direct', evidence / 'restored.db', evidence / 'restored.json'], cwd=deployment, env=env)
    verify_identity_control(receipt, evidence, source, feed, deployment, executable, run, env)
    receipt['directProofPassed'] = True
    receipt['proofScope'] = 'Four-module clean PackageReference direct execution and identity rejection; native/process/upgrade gates pending.'


def verify_identity_control(receipt, evidence, source, feed, deployment, executable, run, env):
    """A valid alternate assembly is rejected, then omission of its digest check breaks that proof."""
    implementation = source / 'Extensions/Rules/src/Runtime/RuleDeploymentIdentitySource.cs'
    original_source = implementation.read_bytes()
    deployed_asset = deployment / 'Elsa.Rules.Runtime.dll'
    original_asset = deployed_asset.read_bytes()
    guard = b'                    if (!StringComparer.Ordinal.Equals(assetHash, asset.AssetSha256))\n                        return null;'
    if original_source.count(guard) != 1:
        raise ValueError('Identity control no longer matches the reviewed implementation; update the control.')
    project = source / 'Extensions/Rules/src/Runtime/Elsa.Rules.Runtime.csproj'
    built_asset = project.parent / 'bin/Release/net10.0/Elsa.Rules.Runtime.dll'
    build = ['dotnet', 'build', project, '-c', 'Release', '--no-restore', '--no-dependencies',
             '-p:CustomBeforeDirectoryBuildProps=' + str(feed.parent / 'package-versions.props')]
    control = {'originalSourceSha256': hashlib.sha256(original_source).hexdigest(),
               'originalDeployedAssetSha256': hashlib.sha256(original_asset).hexdigest()}
    receipt['identityRemovalControl'] = control
    try:
        # An unrelated limit change produces another valid assembly with the same full name.
        marker = b'private const int MaxReceiptBytes = 4 * 1024 * 1024;'
        if original_source.count(marker) != 1:
            raise ValueError('Cannot prepare the valid alternate identity assembly.')
        # Increase an unrelated limit by one byte. No reader check under test is removed in this baseline.
        alternate = original_source.replace(marker, b'private const int MaxReceiptBytes = 4 * 1024 * 1024 + 1;')
        implementation.write_bytes(alternate)
        run(build, cwd=source)
        shutil.copyfile(built_asset, deployed_asset)
        control['alternateAssetSha256'] = hashlib.sha256(deployed_asset.read_bytes()).hexdigest()
        if control['alternateAssetSha256'] == control['originalDeployedAssetSha256']:
            raise ValueError('The alternate asset must actually differ.')
        run(['dotnet', executable, 'unavailable', evidence / 'alternate.db', evidence / 'alternate.json'],
            cwd=deployment, env=env)
        implementation.write_bytes(alternate.replace(guard, b'                    // Identity removal control.'))
        run(build, cwd=source)
        shutil.copyfile(built_asset, deployed_asset)
        control['removedGuardAssetSha256'] = hashlib.sha256(deployed_asset.read_bytes()).hexdigest()
        run(['dotnet', executable, 'unavailable', evidence / 'control.db', evidence / 'control.json'],
            cwd=deployment, env=env, expect_failure=True)
        result = json.loads((evidence / 'control.json').read_text())
        if result.get('passed') is not False or result.get('error') != 'Expected deployed implementation identity to be unavailable.':
            raise ValueError('The control failed for a different reason; it is not valid regression evidence.')
        control['intendedAssertionFailed'] = True
    finally:
        implementation.write_bytes(original_source)
        deployed_asset.write_bytes(original_asset)
        control['restoredSourceSha256'] = hashlib.sha256(implementation.read_bytes()).hexdigest()
        control['restoredDeployedAssetSha256'] = hashlib.sha256(deployed_asset.read_bytes()).hexdigest()
    run(['dotnet', executable, 'direct', evidence / 'control-restored.db', evidence / 'control-restored.json'],
        cwd=deployment, env=env)
    control['restoredDirectPassed'] = True

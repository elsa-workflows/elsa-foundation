#!/usr/bin/env python3
"""Pack a committed Identity module closure and run an independent public NuGet consumer."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[3]
ROOTS = [
    'Essentials/src/Foundation/Identity/ExternalAuthentication/Api/Elsa.Foundation.Identity.ExternalAuthentication.Api.csproj',
    'Essentials/src/Foundation/Identity/ExternalAuthentication/Oidc/Elsa.Foundation.Identity.ExternalAuthentication.Oidc.csproj',
    'Essentials/src/Foundation/Identity/ExternalAuthentication/Persistence/EntityFrameworkCore/Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore.csproj',
    'Essentials/src/Foundation/Identity/AspNetCoreIdentity/EntityFrameworkCore/Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore.csproj',
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, help='New owned evidence directory')
    args = parser.parse_args()
    git = lambda *values: subprocess.check_output(['git', *values], cwd=REPO, text=True).strip()
    source = git('rev-parse', 'HEAD')
    if git('status', '--porcelain'):
        raise ValueError('Commit changes before package qualification')
    output = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix='elsa-3579-packages-')).resolve()
    if args.output:
        output.mkdir(parents=True, exist_ok=False)
    producer, feed, consumer = [output / name for name in ('producer', 'feed', 'consumer')]
    for directory in (producer, feed, consumer):
        directory.mkdir()
    version = '4.0.0-external-auth-ci.' + source[:12]
    report = {'sourceSha': source, 'dirty': False, 'version': version, 'status': 'failed', 'stage': 'archive', 'packages': []}
    print('Owned package qualification: ' + str(output), flush=True)

    def run(label, command, cwd):
        report['stage'] = label
        print(label, flush=True)
        with (output / (label + '.log')).open('w') as log:
            subprocess.run(command, cwd=cwd, stdout=log, stderr=subprocess.STDOUT, check=True)

    try:
        run('archive', ['git', 'archive', '--format=tar', '--output=' + str(output / 'source.tar'), source], REPO)
        run('extract', ['tar', '-xf', str(output / 'source.tar'), '-C', str(producer)], REPO)
        projects = set()

        def visit(project):
            project = project.resolve()
            if project in projects:
                return
            if not project.is_relative_to(producer):
                raise ValueError('Production reference escapes the committed source')
            projects.add(project)
            for reference in ET.parse(project).iter('ProjectReference'):
                value = reference.attrib['Include'].replace('\\', '/')
                if '$' in value:
                    raise ValueError('Resolve conditional project reference explicitly: ' + value)
                visit(project.parent / value)

        for relative in ROOTS:
            visit(producer / relative)
        properties = ['-p:Version=' + version, '-p:SourceRevisionId=' + source]
        for index, relative in enumerate(ROOTS):
            run('restore-' + str(index), ['dotnet', 'restore', relative, '--locked-mode'], producer)
            run('build-' + str(index), ['dotnet', 'build', relative, '-c', 'Release', '--no-restore', '-m:1', *properties], producer)
        for project in sorted(projects):
            run('pack-' + project.stem, ['dotnet', 'pack', str(project), '-c', 'Release', '--no-build', '--no-restore', *properties, '-o', str(feed)], producer)
        ids = []
        for package in sorted(feed.glob('*.nupkg')):
            with zipfile.ZipFile(package) as archive:
                spec = ET.fromstring(archive.read(next(name for name in archive.namelist() if name.endswith('.nuspec'))))
                values = {node.tag.rsplit('}', 1)[-1]: node.text for node in spec.iter()}
                repository = next(node.attrib for node in spec.iter() if node.tag.rsplit('}', 1)[-1] == 'repository')
                if values['version'] != version or repository.get('commit') != source:
                    raise ValueError('Candidate provenance differs: ' + package.name)
                if not any(name.startswith('lib/net10.0/') and name.endswith('.dll') for name in archive.namelist()):
                    raise ValueError('Candidate assembly missing: ' + package.name)
                ids.append(values['id'])
                report['packages'].append({'id': values['id'], 'sha256': hashlib.sha256(package.read_bytes()).hexdigest()})
        if len(ids) != len(projects) or len(set(ids)) != len(ids):
            raise ValueError('The complete production package closure was not produced')
        config = ET.parse(producer / 'NuGet.config')
        ET.SubElement(config.getroot().find('packageSources'), 'add', key='owned-candidate', value=str(feed))
        mapping = ET.SubElement(config.getroot().find('packageSourceMapping'), 'packageSource', key='owned-candidate')
        for package_id in ids:
            ET.SubElement(mapping, 'package', pattern=package_id)
        config.write(consumer / 'NuGet.config', encoding='unicode')
        shutil.copy2(producer / 'global.json', consumer / 'global.json')
        project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
        props = ET.SubElement(project, 'PropertyGroup')
        for key, value in {'TargetFramework': 'net10.0', 'OutputType': 'Exe', 'ImplicitUsings': 'enable', 'Nullable': 'enable', 'RestorePackagesWithLockFile': 'true', 'RestorePackagesPath': str(output / 'consumer-packages')}.items():
            ET.SubElement(props, key).text = value
        items = ET.SubElement(project, 'ItemGroup')
        ET.SubElement(items, 'FrameworkReference', Include='Microsoft.AspNetCore.App')
        for relative in ROOTS:
            ET.SubElement(items, 'PackageReference', Include=Path(relative).stem, Version=version)
        ET.ElementTree(project).write(consumer / 'PublicConsumer.csproj', encoding='unicode')
        (consumer / 'Program.cs').write_text('''using System.Security.Claims;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication;
using Elsa.Foundation.Identity.ExternalAuthentication.Api;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Foundation.Identity.ExternalAuthentication.Oidc;
using Elsa.Foundation.Identity.ExternalAuthentication.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddFoundationExternalAuthentication();
services.AddFoundationExternalOidc();
services.AddFoundationExternalAuthenticationApi();
services.AddExternalAuthenticationEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:" });
services.AddScoped<CompanyPolicy>();
services.AddScoped<IExternalProvisioningPolicy, CompanyPolicy>();
using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var record = new ConnectionRecord { Id = "consumer", Key = "company", DisplayName = "Company", Protocol = "oidc", Admission = new() { Type = "company-onboarding", DefaultRoleIds = ["reader", "operator"] } };
var context = new ExternalAdmissionContext(new(record, ConnectionSource.Deployment, true, null, false, "revision"),
    new ExternalIdentityKey("default", "company", "https://idp.example.invalid", "opaque-subject"),
    [new Claim("department", "Operations"), new Claim("role", "upstream-admin")]);
var decision = await scope.ServiceProvider.GetRequiredService<CompanyPolicy>().AssessAsync(context);
if (decision.Kind != ExternalAdmissionKind.NewUser || !decision.RoleIds.SequenceEqual(["operator"])) throw new Exception("Public policy contract failed");
var first = new TokenSubject("default", "equal-subject", "company", "https://first.example.invalid");
var second = new TokenSubject("default", "equal-subject", "company", "https://second.example.invalid");
if (first == second || first.CanonicalUserId is not null) throw new Exception("Typed external subject contract failed");
if (!services.Any(item => item.ServiceType == typeof(IAuthenticationArtifactStore)) || !services.Any(item => item.ServiceType == typeof(IAuthenticationSessionStore))) throw new Exception("Public persistence registration failed");
Console.WriteLine("Public NuGet consumer passed: optional module composition, custom pure provisioning policy, exact scoped token identities and artifact/session registration.");

public class SpecializedExternalAuthenticationFeature : ExternalAuthenticationApiFeature
{
    public override void ConfigureServices(IServiceCollection services) => base.ConfigureServices(services);
}

public sealed class CompanyPolicy : IExternalProvisioningPolicy
{
    public string Name => "company-onboarding";
    public ValueTask<ExternalProvisioningDecision> AssessAsync(ExternalAdmissionContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ExternalProvisioningDecision { Kind = ExternalAdmissionKind.NewUser,
            RoleIds = context.Claims.Any(claim => claim.Type == "department" && claim.Value == "Operations") ? ["operator"] : ["reader"] });
}
''')
        run('consumer-restore', ['dotnet', 'restore', 'PublicConsumer.csproj'], consumer)
        run('consumer-locked-restore', ['dotnet', 'restore', 'PublicConsumer.csproj', '--locked-mode'], consumer)
        run('consumer-run', ['dotnet', 'run', '--project', 'PublicConsumer.csproj', '-c', 'Release', '--no-restore'], consumer)
        assets = json.loads((consumer / 'obj/project.assets.json').read_text())
        if any(item.get('type') == 'project' for item in assets['libraries'].values()):
            raise ValueError('Public consumer unexpectedly used a project reference')
        if git('rev-parse', 'HEAD') != source or git('status', '--porcelain'):
            raise ValueError('Source changed during qualification')
        report.update(status='passed', stage='complete', publicConsumerProjectReferences=0, packageCount=len(ids))
    finally:
        (output / 'report.json').write_text(json.dumps(report, indent=2) + '\n')
        print(json.dumps(report, indent=2), flush=True)


if __name__ == '__main__':
    main()

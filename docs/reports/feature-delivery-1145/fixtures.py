#!/usr/bin/env python3
"""Build three tiny package-only witnesses against the pinned image's CShells contracts."""
import argparse
import json
import pathlib
import subprocess
import zipfile


def build(root):
    deps = json.loads((root / "host.deps.json").read_text())
    host_version = next(k.split("/")[1] for k in deps["libraries"] if k.startswith("Elsa.Primitives/"))
    projects = []
    for name in ["Directory", "Compatible", "Incompatible"]:
        package = f"Fr2Proof.{name}"
        directory = root / "fixtures" / name
        directory.mkdir(parents=True, exist_ok=True)
        project = directory / f"{package}.csproj"
        project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><Version>1.0.0</Version></PropertyGroup>
<ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" />
<Reference Include="CShells.Abstractions"><HintPath>{root}/CShells.Abstractions.dll</HintPath><Private>false</Private></Reference>
<Reference Include="CShells.AspNetCore.Abstractions"><HintPath>{root}/CShells.AspNetCore.Abstractions.dll</HintPath><Private>false</Private></Reference>
</ItemGroup></Project>''')
        (directory / "Feature.cs").write_text(f'''using CShells.AspNetCore.Features;
using CShells.Features;
using CShells.Lifecycle;
using CShells;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

[ShellFeature(name: "Fr2{name}")]
public sealed class Feature : IWebShellFeature
{{
    public void ConfigureServices(IServiceCollection services) => Console.WriteLine("FR2_ACTIVATED_{name}");
    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment) =>
        endpoints.MapGet("/fr2/{name.lower()}", (IRuntimeFeatureCatalog catalog, IShell shell) => Results.Ok(new {{ witness = "{name}", pid = Environment.ProcessId,
            shellGeneration = shell.Descriptor.Generation, catalogGeneration = catalog.CurrentSnapshot.Generation,
            features = catalog.CurrentSnapshot.FeatureDescriptors.Select(x => x.Id).Order().ToArray(),
            assemblies = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetName().Name).Where(x => x != null).Order().ToArray() }}));
}}
''')
        # No package dependencies are restored to compile these witnesses.
        (directory / "NuGet.config").write_text('<configuration><packageSources><clear /></packageSources></configuration>')
        projects.append(project)
    solution = root / "fixtures" / "fixtures.slnx"
    solution.write_text('<Solution>\n' + '\n'.join(f'<Project Path="{p.relative_to(solution.parent)}" />' for p in projects) + '\n</Solution>')
    with (root / "fixtures-build.log").open("w") as log:
        subprocess.run(["dotnet", "build", str(solution), "-c", "Release", "--nologo", "-m:1"], check=True, stdout=log, stderr=subprocess.STDOUT)
    for project in projects:
        name = project.parent.name
        package = project.stem
        minimum = "4.0.999-preview" if name == "Incompatible" else host_version
        dependency = f'<dependency id="Elsa.Primitives" version="[{minimum},5.0.0)" />'
        nuspec = f'''<?xml version="1.0"?><package><metadata><id>{package}</id><version>1.0.0</version><authors>FR2 acceptance fixture</authors><description>Issue 1145 {name} witness</description><dependencies><group targetFramework="net10.0">{dependency}</group></dependencies></metadata></package>'''
        with zipfile.ZipFile(root / "fixtures" / f"{package}.1.0.0.nupkg", "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr(f"{package}.nuspec", nuspec)
            archive.write(project.parent / "bin" / "Release" / "net10.0" / f"{package}.dll", f"lib/net10.0/{package}.dll")
    print(f"Three witnesses built. Compatible host floor: {host_version}; incompatible floor: 4.0.999-preview.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=pathlib.Path)
    build(parser.parse_args().root.resolve())

using System.Security;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

public sealed class StudioStaticWebAssetsBuildTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Asset_discovery_handles_stale_source_path_casing_without_merging_project_identities(bool useFix)
    {
        var directory = Directory.CreateTempSubdirectory(nameof(StudioStaticWebAssetsBuildTests));
        try
        {
            var root = directory.FullName;
            Directory.CreateDirectory(Path.Join(root, "Library"));
            // Exercise genuinely case-distinct project paths where the filesystem supports them.
            var secondDirectory = Directory.Exists(Path.Join(root, "library")) ? "OtherLibrary" : "library";
            var libraries = new[] { "Library", secondDirectory };
            for (var index = 0; index < libraries.Length; index++)
            {
                var path = Path.Join(root, libraries[index]);
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Join(path, "Library.csproj"), $"""
                    <Project Sdk="Microsoft.NET.Sdk.Razor">
                      <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                        <AssemblyName>AssetSource{index}</AssemblyName>
                      </PropertyGroup>
                    </Project>
                    """);
            }

            File.WriteAllText(Path.Join(root, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
            var project = Path.Join(root, "Host.csproj");
            var sources = Path.Join(root, "sources.txt");
            File.WriteAllText(project, $"""
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="Library/Library.csproj" />
                    <ProjectReference Include="{secondDirectory}/Library.csproj" />
                  </ItemGroup>
                  <Target Name="SimulateRestoreWithDifferentCasing" AfterTargets="PrepareProjectReferences">
                    <ItemGroup>
                      <_MSBuildProjectReferenceExistent Update="@(_MSBuildProjectReferenceExistent)">
                        <MSBuildSourceProjectFile>$([System.String]::Copy('%(_MSBuildProjectReferenceExistent.FullPath)').ToLowerInvariant())</MSBuildSourceProjectFile>
                      </_MSBuildProjectReferenceExistent>
                    </ItemGroup>
                  </Target>
                  <Import Project="{SecurityElement.Escape(Path.Join(RepoRoot, "Studio", "Directory.Build.targets"))}"
                          Condition="'$(UseStudioPathFix)' == 'true'" />
                  <Target Name="RecordAssetSources" DependsOnTargets="ResolveReferencedProjectsStaticWebAssetsConfiguration">
                    <WriteLinesToFile File="{SecurityElement.Escape(sources)}"
                                      Lines="@(StaticWebAssetProjectConfiguration->'%(Source)|%(Identity)')"
                                      Overwrite="true" />
                  </Target>
                </Project>
                """);

            var (exitCode, output) = ChildProcess.Dotnet(
                ["msbuild", project, "-restore", "-t:RecordAssetSources", "-nologo", "-nr:false", "-v:minimal", $"-p:UseStudioPathFix={useFix}"]);

            // The SDK already compares paths without case on Windows; Unix needs the fix.
            if (!useFix && !OperatingSystem.IsWindows())
            {
                Assert.NotEqual(0, exitCode);
                Assert.Contains("Unable to find a project reference for project configuration item", output);
                return;
            }

            Assert.True(exitCode == 0, output);
            var actual = File.ReadAllLines(sources).Order(StringComparer.Ordinal).ToArray();
            var expected = libraries.Select((name, index) => $"AssetSource{index}|{Path.Join(root, name, "Library.csproj")}")
                .Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}

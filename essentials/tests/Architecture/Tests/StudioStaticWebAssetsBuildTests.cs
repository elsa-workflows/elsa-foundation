using System.Security;
using System.Text.Json;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

public sealed class StudioStaticWebAssetsBuildTests
{
    [Theory]
    [InlineData("Studio")]
    [InlineData("studio")]
    [InlineData(".")]
    [InlineData(null)]
    public void Consumer_uses_solution_or_repository_Studio_path_for_references_and_content(string? solutionDirectory)
    {
        var lowercaseStudio = Path.Join(RepoRoot, "studio");
        var standaloneStudio = File.Exists(Path.Join(lowercaseStudio, "Elsa.Studio.slnx"))
            ? lowercaseStudio
            : Path.Join(RepoRoot, "Studio");
        var studioDirectory = solutionDirectory is "Studio" or "studio" && File.Exists(Path.Join(RepoRoot, solutionDirectory, "Elsa.Studio.slnx"))
            ? Path.Join(RepoRoot, solutionDirectory)
            : standaloneStudio;
        var project = Path.Join(RepoRoot, "tools", "ux-acceptance", "StudioConsumer", "Acme.TriggerStudioConsumer.csproj");
        var arguments = new List<string>
        {
            "msbuild",
            project,
            "-nologo",
            "-getProperty:StudioDirectory",
            "-getItem:ProjectReference,Content"
        };

        if (solutionDirectory is not null)
            arguments.Add($"-p:SolutionDir={Path.Join(RepoRoot, solutionDirectory)}{Path.DirectorySeparatorChar}");

        var (exitCode, output) = ChildProcess.Dotnet(arguments.ToArray());

        Assert.True(exitCode == 0, output);

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var actualStudioDirectory = root.GetProperty("Properties").GetProperty("StudioDirectory").GetString();
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(studioDirectory)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(actualStudioDirectory!)));

        var projectReferences = root.GetProperty("Items").GetProperty("ProjectReference");
        var expectedProjects = new[]
        {
            "Elsa.Studio.Api",
            "Elsa.Studio.Hosting",
            "Elsa.Studio.Backends",
            "Elsa.Studio.Hosting.Themes",
            "Elsa.Studio.Hosting.Nuplane",
            "Elsa.Studio.Core",
            "Elsa.Studio.Workflows"
        };

        foreach (var name in expectedProjects)
        {
            var expectedPath = Path.GetFullPath(Path.Join(studioDirectory, "src", "essentials", name, $"{name}.csproj"));
            Assert.Contains(projectReferences.EnumerateArray(), item => item.GetProperty("FullPath").GetString() == expectedPath);
        }

        var content = Assert.Single(root.GetProperty("Items").GetProperty("Content").EnumerateArray());
        var expectedContentPath = Path.GetFullPath(Path.Join(studioDirectory, "src", "apps", "Elsa.Studio.Workbench.Host", "nuplane-management.json"));
        Assert.Equal(expectedContentPath, content.GetProperty("FullPath").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Asset_discovery_handles_stale_source_path_casing_without_merging_project_identities(bool useFix)
    {
        var directory = Directory.CreateTempSubdirectory(nameof(StudioStaticWebAssetsBuildTests));
        try
        {
            var root = directory.FullName;
            // MSBuild's project cache treats case-only paths as the same project even on Linux.
            // Use distinct projects; the stale metadata below still changes their path casing.
            const string secondDirectory = "OtherLibrary";
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

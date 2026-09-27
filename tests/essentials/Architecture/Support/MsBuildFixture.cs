using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// A minimal, SDK-less fixture project (no restore needed, about a second per run) that imports the root
/// <c>Directory.Build.props</c>, for proving the build-time checks the root build files define.
/// </summary>
internal static class MsBuildFixture
{
    /// <summary>The root build files: <c>Directory.Build.props</c> and the two files it imports from beside itself.</summary>
    private static readonly string[] RootBuildFiles =
        ["Directory.Build.props", "PackageVersioning.props", "PackageRanges.targets", "VersionLines.props"];

    /// <summary>
    /// Writes the fixture project <paramref name="projectName"/> and runs <c>-t:</c><paramref name="target"/> against
    /// it: <c>BeforeBuild</c> by default, or <c>GenerateNuspec</c> or <c>_GetProjectVersion</c> to exercise a pack hook.
    /// That proves each check's <c>BeforeTargets</c> wiring for the hook itself, not just the check's body in isolation.
    /// </summary>
    /// <remarks>
    /// The project imports <paramref name="importFirst"/> when given - standing in for what MSBuild's
    /// <c>CustomBeforeDirectoryBuildProps</c> hook imports ahead of <c>Directory.Build.props</c> - then
    /// <c>Directory.Build.props</c>, then <paramref name="body"/>. Without <paramref name="repositoryRoot"/> it imports the
    /// real root file from a directory outside the repository; with one, a copy made by <see cref="CopyRootBuildFiles"/>,
    /// from under that copy's <c>src/</c>, where the checks that apply only to published projects apply too.
    /// </remarks>
    internal static (int ExitCode, string Output) Run(
        string projectName, string body = "", IEnumerable<string>? globalProperties = null, string? repositoryRoot = null,
        string target = "BeforeBuild", string? importFirst = null)
    {
        var directory = repositoryRoot is null
            ? Directory.CreateTempSubdirectory(nameof(MsBuildFixture))
            : Directory.CreateDirectory(Path.Join(repositoryRoot, "src", projectName));
        try
        {
            var projectPath = Path.Join(directory.FullName, $"{projectName}.proj");
            File.WriteAllText(projectPath, $"""
                <Project>
                  {(importFirst is null ? string.Empty : $"<Import Project=\"{importFirst}\" />")}
                  <Import Project="{Path.Join(repositoryRoot ?? RepoRoot, "Directory.Build.props")}" />
                  {body}
                  <Target Name="BeforeBuild" />
                  <Target Name="GenerateNuspec" />
                  <Target Name="_GetProjectVersion" />
                </Project>
                """);

            return ChildProcess.Dotnet(
                ["msbuild", projectPath, $"-t:{target}", "-nologo", "-nodeReuse:false", .. (globalProperties ?? []).Select(property => $"-p:{property}")]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Copies the root build files into <paramref name="destination"/>, passing <c>VersionLines.props</c> through
    /// <paramref name="rewriteVersionLines"/>, for <see cref="Run"/>'s <c>repositoryRoot</c>.
    /// </summary>
    internal static string CopyRootBuildFiles(string destination, Func<string, string>? rewriteVersionLines = null)
    {
        foreach (var file in RootBuildFiles)
        {
            var content = File.ReadAllText(Path.Join(RepoRoot, file));
            File.WriteAllText(Path.Join(destination, file), file == "VersionLines.props" && rewriteVersionLines is not null ? rewriteVersionLines(content) : content);
        }

        return destination;
    }
}

using System.Xml.Linq;
using Xunit;

namespace Groundwork.Tests;

public sealed class GroundworkDependencyBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<string, string[]> GroundworkProjectReferences => new()
    {
        { "src/Groundwork/Core/Groundwork.Core.csproj", [] },
        { "src/Groundwork/Relational/Groundwork.Relational.csproj", ["src/Groundwork/Core/Groundwork.Core.csproj", "src/Groundwork/Documents/Groundwork.Documents.csproj"] },
        { "src/Groundwork/Documents/Groundwork.Documents.csproj", ["src/Groundwork/Core/Groundwork.Core.csproj"] }
    };

    [Theory]
    [MemberData(nameof(GroundworkProjectReferences))]
    public void GroundworkProjectsReferenceOnlyAllowedProjects(string projectPath, string[] allowedReferences)
    {
        var fullPath = Path.Combine(RepositoryRoot, projectPath);
        var actual = ReadProjectReferences(fullPath)
            .Select(reference => NormalizeRelativeProjectPath(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullPath)!, NormalizeMsBuildPath(reference)))))
            .OrderBy(reference => reference)
            .ToList();

        var expected = allowedReferences
            .Select(reference => NormalizeRelativeProjectPath(Path.Combine(RepositoryRoot, reference)))
            .OrderBy(reference => reference)
            .ToList();

        Assert.Equal(expected, actual);
        Assert.DoesNotContain(actual, reference => reference.StartsWith("src/Elsa/", StringComparison.Ordinal));
    }

    [Fact]
    public void GenericGroundworkSourceDoesNotUseElsaNamespace()
    {
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src/Groundwork"), "*.cs", SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("namespace Elsa", text, StringComparison.Ordinal);
            Assert.DoesNotContain("using Elsa", text, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        return document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>();
    }

    private static string NormalizeRelativeProjectPath(string path) =>
        Path.GetRelativePath(RepositoryRoot, Path.GetFullPath(path)).Replace('\\', '/');

    private static string NormalizeMsBuildPath(string path) => path.Replace('\\', Path.DirectorySeparatorChar);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}

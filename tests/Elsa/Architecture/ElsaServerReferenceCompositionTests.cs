using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class ElsaServerReferenceCompositionTests
{
    [Fact]
    public void Elsa_Server_contains_no_workflow_management_endpoint_implementation_or_legacy_route()
    {
        var serverDirectory = Path.Combine(RepoRoot, "src", "Apps", "Elsa.Server");
        var violations = Directory
            .EnumerateFiles(serverDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(file => (Path: file, Source: File.ReadAllText(file)))
            .SelectMany(file => FindViolations(file.Path, file.Source))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Elsa.Server must compose domain APIs, not implement the supported workflow-management API:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> FindViolations(string path, string source)
    {
        var displayPath = Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');

        if (source.Contains("/_elsa/workflow-management", StringComparison.Ordinal))
            yield return $"{displayPath}: contains the legacy /_elsa/workflow-management route literal";
        if (source.Contains("ElsaWorkflowManagementApi", StringComparison.Ordinal))
            yield return $"{displayPath}: contains the host-owned workflow-management facade";
        if (source.Contains("MapElsaWorkflowManagementApi", StringComparison.Ordinal))
            yield return $"{displayPath}: maps host-owned workflow-management endpoints";
    }

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}

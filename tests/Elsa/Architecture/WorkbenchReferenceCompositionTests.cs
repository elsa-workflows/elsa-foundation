using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class WorkbenchReferenceCompositionTests
{
    private static readonly string LegacyRoute = string.Join('/', "", "_elsa", "workflow-management");
    private static readonly string FacadeTypeName = string.Concat("ElsaWorkflow", "ManagementApi");
    private static readonly string MapFacadeMethodName = string.Concat("MapElsaWorkflow", "ManagementApi");

    [Fact]
    public void Elsa_Server_contains_no_workflow_management_endpoint_implementation_or_legacy_route()
    {
        var serverDirectory = Path.Combine(RepoRoot, "src", "Apps", "Elsa.Workbench");
        var violations = Directory
            .EnumerateFiles(serverDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(file => (Path: file, Source: File.ReadAllText(file)))
            .SelectMany(file => FindViolations(file.Path, file.Source))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Elsa.Workbench must compose domain APIs, not implement the supported workflow-management API:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Workbench_does_not_consume_Elsa_Unified_Groundwork_composition()
    {
        var workbenchDirectory = Path.Combine(RepoRoot, "src", "Apps", "Elsa.Workbench");
        var program = File.ReadAllText(Path.Combine(workbenchDirectory, "Program.cs"));
        var project = File.ReadAllText(Path.Combine(workbenchDirectory, "Elsa.Workbench.csproj"));

        Assert.DoesNotContain("GroundworkUnified", program, StringComparison.Ordinal);
        Assert.DoesNotContain("GroundworkUnified", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Persistence.Groundwork.PostgreSql.Unified", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Persistence.Groundwork.Sqlite.Unified", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceComposition", project, StringComparison.Ordinal);
    }

    private static IEnumerable<string> FindViolations(string path, string source)
    {
        var displayPath = Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');

        if (source.Contains(LegacyRoute, StringComparison.Ordinal))
            yield return $"{displayPath}: contains the legacy workflow-management route literal";
        if (source.Contains(FacadeTypeName, StringComparison.Ordinal))
            yield return $"{displayPath}: contains the host-owned workflow-management facade";
        if (source.Contains(MapFacadeMethodName, StringComparison.Ordinal))
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

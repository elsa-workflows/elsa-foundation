using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

public sealed class WorkbenchCompositionTests
{
    private static readonly string LegacyRoute = string.Join('/', "", "_elsa", "workflow-management");
    private static readonly string FacadeTypeName = string.Concat("ElsaWorkflow", "ManagementApi");
    private static readonly string MapFacadeMethodName = string.Concat("MapElsaWorkflow", "ManagementApi");

    [Fact]
    public void Elsa_Server_contains_no_workflow_management_endpoint_implementation_or_legacy_route()
    {
        var serverDirectory = Path.Combine(RepoRoot, "src", "apps", "Elsa.Workbench");
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
}

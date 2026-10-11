using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogsApiDependencyTests
{
    [Fact]
    public void Production_structured_logs_project_no_longer_references_fast_endpoints_or_legacy_api_project()
    {
        var project = File.ReadAllText(Path.Join(
            RepoRoot, "src", "essentials", "Diagnostics", "StructuredLogs", "Elsa.Diagnostics.StructuredLogs", "Elsa.Diagnostics.StructuredLogs.csproj"));

        Assert.DoesNotContain("FastEndpoints", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Elsa.Api.FastEndpoints", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CShells.FastEndpoints", project, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

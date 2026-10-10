using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsApiDependencyTests
{
    [Fact]
    public void Production_api_project_no_longer_references_fast_endpoints()
    {
        var project = File.ReadAllText(Path.Join(RepoRoot, "src", "essentials", "Secrets", "Api", "Elsa.Secrets.Api.csproj"));
        Assert.DoesNotContain("FastEndpoints", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Elsa.Api.FastEndpoints", project, StringComparison.OrdinalIgnoreCase);
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

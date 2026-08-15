using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsApiDependencyTests
{
    [Fact]
    public void Production_api_project_no_longer_references_fast_endpoints()
    {
        var project = File.ReadAllText(Path.Join(RepoRoot, "src", "Elsa", "Secrets", "Api", "Elsa.Secrets.Api.csproj"));
        Assert.DoesNotContain("FastEndpoints", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Elsa.Api.FastEndpoints", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_api_source_contains_no_fast_endpoints_endpoint_bases_or_discovery_interfaces()
    {
        var source = string.Join("\n", Directory.EnumerateFiles(
            Path.Join(RepoRoot, "src", "Elsa", "Secrets", "Api"), "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));

        Assert.DoesNotContain("FastEndpoints", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Endpoint<", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EndpointWithoutRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FastEndpointsFeatureBase", source, StringComparison.Ordinal);
        Assert.Contains("IWebShellFeature", source, StringComparison.Ordinal);
    }

    [Fact]
    public void No_secrets_fast_endpoints_transition_registration_remains()
    {
        var baseline = File.ReadAllText(Path.Join(
            RepoRoot, "tests", "Elsa", "Architecture", "Baselines", "fastendpoints-transition-exceptions.json"));
        Assert.DoesNotContain("Elsa.Secrets.Api.Endpoints.Secrets.", baseline, StringComparison.Ordinal);
        Assert.DoesNotContain("\"followUp\": \"#1348\"", baseline, StringComparison.Ordinal);
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

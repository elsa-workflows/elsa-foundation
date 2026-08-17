using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Tests;

public sealed class StructuredLogsApiDependencyTests
{
    [Fact]
    public void Production_structured_logs_project_no_longer_references_fast_endpoints_or_legacy_api_project()
    {
        var project = File.ReadAllText(Path.Join(
            RepoRoot, "src", "Elsa", "Diagnostics", "StructuredLogs", "Elsa.Diagnostics.StructuredLogs.csproj"));

        Assert.DoesNotContain("FastEndpoints", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Elsa.Api.FastEndpoints", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CShells.FastEndpoints", project, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_structured_logs_sources_contain_no_fast_endpoints_discovery_or_shared_sse_helper()
    {
        var source = string.Join(
            "\n",
            Directory.EnumerateFiles(
                    Path.Join(RepoRoot, "src", "Elsa", "Diagnostics", "StructuredLogs"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("FastEndpoints", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IFastEndpoints", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FastEndpointsFeatureBase", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SseStreamWriter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ISseStreamFormatter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Elsa.Api.FastEndpoints", source, StringComparison.Ordinal);
        Assert.Contains("IWebShellFeature", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Structured_logs_transition_baseline_contains_no_legacy_endpoint_registrations()
    {
        var baseline = File.ReadAllText(Path.Join(
            RepoRoot,
            "tests",
            "Elsa",
            "Architecture",
            "Baselines",
            "fastendpoints-transition-exceptions.json"));

        Assert.DoesNotContain("Elsa.Diagnostics.StructuredLogs.Endpoints.RecentEndpoint", baseline, StringComparison.Ordinal);
        Assert.DoesNotContain("Elsa.Diagnostics.StructuredLogs.Endpoints.SourcesEndpoint", baseline, StringComparison.Ordinal);
        Assert.DoesNotContain("Elsa.Diagnostics.StructuredLogs.Endpoints.StreamEndpoint", baseline, StringComparison.Ordinal);
        Assert.DoesNotContain("\"followUp\": \"#1349\"", baseline, StringComparison.Ordinal);
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

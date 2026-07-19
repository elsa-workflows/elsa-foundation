using System.Security.Claims;
using System.Text.Json;
using Elsa.Attention.Core;
using Elsa.Modularity.Attention;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;
using Xunit;

namespace Elsa.Modularity.Attention.Tests;

public sealed class ModularityAttentionContributorTests
{
    [Fact]
    public async Task ReportsManifestDiagnosticsAndIncompatibleDependenciesFromCompleteCatalog()
    {
        var catalog = new FeatureCatalogResponse("revision-1",
        [
            Feature("broken", true, readError: "Manifest could not be read."),
            Feature("dependent", true, dependencies: [new("missing", false)]),
            Feature("healthy", true)
        ]);
        var service = new StubFeatureManagementService(catalog);
        var contributor = new ModularityAttentionContributor(service, new FixedTimeProvider());

        var contribution = await contributor.EvaluateAsync(Context(1));

        Assert.Equal(1, service.CallCount);
        Assert.Equal(2, contribution.TotalCount);
        Assert.False(contribution.IsTruncated);
        Assert.Equal(AttentionSeverity.Critical, contribution.Items.First().Severity);
        Assert.Equal("feature:broken", contribution.Items.First().Id);
        Assert.DoesNotContain("Manifest could not be read", contribution.Items.First().Summary, StringComparison.Ordinal);
        Assert.Contains(contribution.Items, x => x.Id == "feature:dependent" && x.Severity == AttentionSeverity.Warning);
        Assert.All(contribution.Items, x => Assert.Equal("/modules", x.Destination.Path));
    }

    [Fact]
    public async Task DisabledRequiredDependencyIsIncompatibleAndGenerationIsStableForSameIssue()
    {
        var service = new StubFeatureManagementService(new("revision-1",
        [
            Feature("dependency", false),
            Feature("dependent", true, dependencies: [new("dependency", false)])
        ]));
        var contributor = new ModularityAttentionContributor(service, new FixedTimeProvider());

        var first = await contributor.EvaluateAsync(Context(1));
        var second = await contributor.EvaluateAsync(Context(1));

        Assert.Single(first.Items);
        Assert.Equal(first.Items.Single().Generation, second.Items.Single().Generation);
    }

    [Fact]
    public async Task ReturnsAtMostFiveItemsButTruthfulTotal()
    {
        var features = Enumerable.Range(0, 8).Select(i => Feature($"broken-{i}", true, readError: "Bad manifest")).ToArray();
        var contributor = new ModularityAttentionContributor(new StubFeatureManagementService(new("revision", features)), new FixedTimeProvider());

        var contribution = await contributor.EvaluateAsync(Context(1));

        Assert.Equal(8, contribution.TotalCount);
        Assert.Equal(5, contribution.Items.Count);
        Assert.True(contribution.IsTruncated);
    }

    [Fact]
    public async Task BudgetPreventsCatalogCall()
    {
        var service = new StubFeatureManagementService(new("revision", []));
        var contributor = new ModularityAttentionContributor(service, new FixedTimeProvider());

        await Assert.ThrowsAsync<AttentionBudgetExceededException>(async () => await contributor.EvaluateAsync(Context(0)));
        Assert.Equal(0, service.CallCount);
    }

    private static AttentionContributorContext Context(int calls) => new(
        new(new ClaimsPrincipal(), null),
        new(calls),
        new Dictionary<string, string>());

    private static FeatureCatalogItem Feature(
        string id,
        bool enabled,
        string? readError = null,
        IReadOnlyList<FeatureDependency>? dependencies = null) => new(
        id,
        id,
        null,
        [],
        "shell",
        null,
        null,
        enabled,
        Json("{}"),
        false,
        false,
        null,
        null,
        readError,
        [],
        dependencies ?? []);

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed class StubFeatureManagementService(FeatureCatalogResponse catalog) : IFeatureManagementService
    {
        public int CallCount { get; private set; }
        public Task<FeatureCatalogResponse> GetCatalogAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(catalog);
        }

        public Task<FeatureApplyResult> ApplyAsync(FeatureApplyRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-07-13T10:00:00Z");
    }
}

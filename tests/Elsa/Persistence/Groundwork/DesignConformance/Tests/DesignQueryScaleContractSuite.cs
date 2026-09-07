using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// T039: provider-neutral scale behavior of the public design query surface. Scenarios prove that
/// results crossing one provider page remain complete and duplicate-free, that the definition-id
/// batch projection is deterministic for oversized, duplicated, and unknown inputs, and that
/// existence/latest resolution stays consistent with full listings at scale. Bounded-route command
/// evidence (query identities, page take bounds, deterministic batch membership) is asserted at the
/// unit level against the recording document-store substrate because this shared assembly cannot
/// observe provider commands; the architecture gate and provider plan suite carry the structural
/// no-load-all guarantees.
/// </summary>
public abstract class DesignQueryScaleContractSuite
{
    protected abstract Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default);

    protected abstract DesignPersistenceContractProfile ContractProfile { get; }

    private const string Scope = DesignPersistenceFixtureData.ScopeA;

    /// <summary>One more definition than the provider's bounded page size of 500.</summary>
    private const int BeyondOnePageCount = 501;

    [SkippableFact]
    public async Task Listing_beyond_one_provider_page_is_complete_and_duplicate_free()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.QueryScalePagedListing);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        var services = scope.ServiceProvider;
        var add = services.GetRequiredService<IAddWorkflowDefinitionCommand>();
        for (var index = 0; index < BeyondOnePageCount; index++)
        {
            var id = $"scale-{index:D4}";
            await add.Execute(
                DesignPersistenceFixtureData.OperationKey($"scale-seed:{id}"),
                DesignPersistenceFixtureData.WorkflowDefinitionAt(id, $"Scale item {index:D4}", null),
                DesignPersistenceFixtureData.WorkflowDraftFor(id, $"{id}-draft"));
        }

        var definitions = services.GetRequiredService<IWorkflowDefinitionStore>();
        var listed = await definitions.ListAsync(new WorkflowDefinitionFilter());
        var repeat = await definitions.ListAsync(new WorkflowDefinitionFilter());

        Assert.Equal(BeyondOnePageCount, listed.Count);
        Assert.Equal(BeyondOnePageCount, listed.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("scale-0000", listed.Select(x => x.Id));
        Assert.Contains($"scale-{BeyondOnePageCount - 1:D4}", listed.Select(x => x.Id));
        Assert.Equal(listed.Select(x => x.Id), repeat.Select(x => x.Id));
    }

    [SkippableFact]
    public async Task Batch_projection_emits_one_deterministic_row_per_distinct_requested_definition()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.QueryScaleBatchProjection);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        var services = scope.ServiceProvider;
        await SeedBatchCatalogAsync(services, definitionCount: 30);
        var projections = services.GetRequiredService<IWorkflowDefinitionListProjectionStore>();

        // Duplicated, unknown, and out-of-order inputs: one row per distinct requested id, in
        // first-seen request order, with empty lifecycle facts for unknown definitions.
        var requested = new List<string> { "batch-07", "batch-05", "batch-07", "batch-unknown-a" };
        requested.AddRange(Enumerable.Range(0, 30).Select(i => $"batch-{i:D2}"));
        requested.Add("batch-unknown-b");

        var rows = await projections.ListByDefinitionIdsAsync(requested);
        var repeat = await projections.ListByDefinitionIdsAsync(requested);

        var expectedIds = requested.Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedIds.Length, rows.Count);
        Assert.Equal(expectedIds, rows.Select(x => x.WorkflowDefinitionId));
        Assert.Equal(rows.Select(DesignPersistenceFixtureData.ResultHash), repeat.Select(DesignPersistenceFixtureData.ResultHash));

        var ladder = rows.Single(x => x.WorkflowDefinitionId == "batch-05");
        Assert.Equal("2.0.0", ladder.LatestVersion);
        Assert.Equal(2, ladder.VersionCount);
        Assert.NotNull(ladder.DraftId);

        var unknown = rows.Single(x => x.WorkflowDefinitionId == "batch-unknown-a");
        Assert.Null(unknown.DraftId);
        Assert.Null(unknown.LatestVersionId);
        Assert.Equal(0, unknown.VersionCount);
    }

    [SkippableFact]
    public async Task Oversized_membership_requests_remain_complete_and_correct()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.QueryScaleOversizedMembership);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        var services = scope.ServiceProvider;
        await SeedBatchCatalogAsync(services, definitionCount: 40);
        var projections = services.GetRequiredService<IWorkflowDefinitionListProjectionStore>();

        // A request far larger than any single bounded batch must be partitioned by the provider
        // adapter, never rejected and never answered from a full-catalog fetch: every known row is
        // present exactly once and every unknown id keeps its empty-facts row.
        var requested = Enumerable.Range(0, 600).Select(i => $"batch-{i % 200:D2}-{i / 200}").ToList();
        requested.AddRange(Enumerable.Range(0, 40).Select(i => $"batch-{i:D2}"));

        var rows = await projections.ListByDefinitionIdsAsync(requested);

        var expectedIds = requested.Distinct(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedIds.Length, rows.Count);
        Assert.Equal(expectedIds, rows.Select(x => x.WorkflowDefinitionId));
        Assert.Equal(40, rows.Count(x => x.DraftId is not null));
    }

    [SkippableFact]
    public async Task Existence_and_latest_resolution_stay_consistent_with_full_listings()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.QueryScaleExistenceConsistency);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        var services = scope.ServiceProvider;
        await SeedBatchCatalogAsync(services, definitionCount: 30);
        var versions = services.GetRequiredService<IWorkflowDefinitionVersionStore>();

        var listed = await versions.ListByDefinitionAsync("batch-05");
        Assert.Equal(2, listed.Count);
        foreach (var version in listed)
            Assert.True(await versions.ExistsAsync("batch-05", SemVer.ToSortKey(version.Version)));

        var latest = await versions.FindLatestVersionAsync("batch-05");
        Assert.NotNull(latest);
        Assert.Equal(
            listed.Select(x => SemVer.ToSortKey(x.Version)).OrderByDescending(x => x, StringComparer.Ordinal).First(),
            SemVer.ToSortKey(latest!.Version));
        Assert.False(await versions.ExistsAsync("batch-05", SemVer.ToSortKey("9.9.9")));
        Assert.Null(await versions.FindLatestVersionAsync("batch-without-versions"));
    }

    [SkippableFact]
    public async Task Known_definition_batch_facts_match_across_providers()
    {
        SkipIfNotApplicable(DesignPersistenceContractScenario.QueryScaleKnownBatchParity);
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        var services = scope.ServiceProvider;
        await SeedBatchCatalogAsync(services, definitionCount: 12);
        var projections = services.GetRequiredService<IWorkflowDefinitionListProjectionStore>();

        // Duplicated known ids: exactly one row per distinct known definition with identical
        // lifecycle facts on every provider.
        var requested = new List<string> { "batch-05", "batch-07", "batch-05" };
        requested.AddRange(Enumerable.Range(0, 12).Select(i => $"batch-{i:D2}"));

        var rows = await projections.ListByDefinitionIdsAsync(requested);

        Assert.Equal(12, rows.Count);
        Assert.Equal(12, rows.Select(x => x.WorkflowDefinitionId).Distinct(StringComparer.Ordinal).Count());
        var ladder = rows.Single(x => x.WorkflowDefinitionId == "batch-05");
        Assert.Equal("2.0.0", ladder.LatestVersion);
        Assert.Equal("batch-05-v2.0.0", ladder.LatestVersionId);
        Assert.Equal(2, ladder.VersionCount);
        Assert.Equal("batch-05-draft", ladder.DraftId);
        var single = rows.Single(x => x.WorkflowDefinitionId == "batch-07");
        Assert.Equal("1.0.0", single.LatestVersion);
        Assert.Equal(1, single.VersionCount);
        var bare = rows.Single(x => x.WorkflowDefinitionId == "batch-00");
        Assert.Null(bare.LatestVersionId);
        Assert.Equal(0, bare.VersionCount);
        Assert.Equal("batch-00-draft", bare.DraftId);
    }

    private void SkipIfNotApplicable(DesignPersistenceContractScenario scenario)
    {
        var applicability = ContractProfile.GetApplicability(scenario);
        Xunit.Skip.If(!applicability.IsApplicable, applicability.NotApplicableReason!);
    }

    /// <summary>
    /// Seeds a deterministic batch catalog: <paramref name="definitionCount"/> definitions with
    /// initial drafts, a two-step version ladder on batch-05, and one version on batch-07.
    /// </summary>
    private static async Task SeedBatchCatalogAsync(IServiceProvider services, int definitionCount)
    {
        var add = services.GetRequiredService<IAddWorkflowDefinitionCommand>();
        for (var index = 0; index < definitionCount; index++)
        {
            var id = $"batch-{index:D2}";
            await add.Execute(
                DesignPersistenceFixtureData.OperationKey($"batch-seed:{id}"),
                DesignPersistenceFixtureData.WorkflowDefinitionAt(id, $"Batch item {index:D2}", null),
                DesignPersistenceFixtureData.WorkflowDraftFor(id, $"{id}-draft"));
        }

        var materialize = services.GetRequiredService<IMaterializeWorkflowDefinitionVersionCommand>();
        foreach (var (definitionId, version) in new[] { ("batch-05", "1.0.0"), ("batch-05", "2.0.0"), ("batch-07", "1.0.0") })
        {
            await materialize.Execute(
                DesignPersistenceFixtureData.OperationKey($"batch-version:{definitionId}:{version}"),
                DesignPersistenceFixtureData.WorkflowVersionAt(definitionId, version, $"{definitionId}-v{version}"));
        }
    }
}

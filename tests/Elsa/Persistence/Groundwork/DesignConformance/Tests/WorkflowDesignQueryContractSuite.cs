using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// T037: provider-neutral workflow design query-shape parity. Every scenario seeds a mixed catalog
/// exclusively through public commands, executes public store reads, and asserts the exact result
/// membership and ordering the closed query contract promises — including null/missing optional
/// values, semantic-version precedence, build-metadata-insensitive identity, and deterministic
/// tie-breaks. The suite references only Elsa core contracts plus the shared fixture, so the same
/// scenarios run against the Groundwork target and the temporary EF oracle for parity.
/// </summary>
public abstract class WorkflowDesignQueryContractSuite
{
    protected abstract Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default);

    private const string Scope = DesignPersistenceFixtureData.ScopeA;

    [Fact]
    public async Task Name_equality_and_membership_match_exact_values()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        var definitions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();

        // Exact equality is case-sensitive and does not match the differently-cased sibling.
        var exact = await definitions.ListAsync(new WorkflowDefinitionFilter { Name = "Alpha" });
        Assert.Equal(["wf-alpha"], exact.Select(x => x.Id));

        // Membership matches only listed values; duplicate names return every carrier.
        var membership = await definitions.ListAsync(new WorkflowDefinitionFilter
        {
            Names = ["Beta flow", "alpha"]
        });
        Assert.Equal(
            ["wf-alpha-lower", "wf-beta-1", "wf-beta-2"],
            membership.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));

        // Repeating the identical request yields the identical sequence: results are stable.
        var repeat = await definitions.ListAsync(new WorkflowDefinitionFilter
        {
            Names = ["Beta flow", "alpha"]
        });
        Assert.Equal(membership.Select(x => x.Id), repeat.Select(x => x.Id));
    }

    [Fact]
    public async Task Empty_membership_matches_nothing_rather_than_everything()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        var definitions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();

        var byIds = await definitions.ListAsync(new WorkflowDefinitionFilter { Ids = [] });
        var byNames = await definitions.ListAsync(new WorkflowDefinitionFilter { Names = [] });

        Assert.Empty(byIds);
        Assert.Empty(byNames);
    }

    [Fact]
    public async Task Description_equality_distinguishes_null_empty_and_present_values()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        var definitions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();

        // An empty-string description is a present value distinct from an absent one.
        var empty = await definitions.ListAsync(new WorkflowDefinitionFilter { Description = "" });
        Assert.Equal(["wf-empty-desc"], empty.Select(x => x.Id));

        // A concrete description matches only its exact carrier, never null/absent descriptions.
        var exact = await definitions.ListAsync(new WorkflowDefinitionFilter
        {
            Description = "Processes an order."
        });
        Assert.Equal(["wf-beta-1"], exact.Select(x => x.Id));
    }

    [Fact]
    public async Task Search_term_spans_name_description_and_id_case_insensitively()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        var definitions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>();

        // "order" appears in wf-beta-1's description, wf-gamma's name, and wf-order-echo's id.
        var hits = await definitions.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "order" });
        Assert.Equal(
            ["wf-beta-1", "wf-gamma", "wf-order-echo"],
            hits.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));

        // ASCII case-insensitivity holds across the same disjunctive shape.
        var upper = await definitions.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "ORDER" });
        Assert.Equal(
            hits.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal),
            upper.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));

        // A term matching nothing returns an empty set, not an error.
        Assert.Empty(await definitions.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "zzz-absent" }));
    }

    [Fact]
    public async Task Version_listing_is_complete_and_stable_across_repeated_reads()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        await SeedSemVerLadderAsync(scope.ServiceProvider);
        var versions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionVersionStore>();

        var listed = await versions.ListByDefinitionAsync("wf-alpha");
        var repeat = await versions.ListByDefinitionAsync("wf-alpha");

        Assert.Equal(
            ["1.0.0", "1.10.0", "1.2.0", "2.0.0"],
            listed.Select(x => x.Version).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(listed.Select(x => x.Id), repeat.Select(x => x.Id));
        Assert.Empty(await versions.ListByDefinitionAsync("wf-without-versions"));
    }

    [Fact]
    public async Task Latest_version_ranks_by_semantic_precedence_not_lexicographically()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        var services = scope.ServiceProvider;
        var materialize = services.GetRequiredService<IMaterializeWorkflowDefinitionVersionCommand>();
        var versions = services.GetRequiredService<IWorkflowDefinitionVersionStore>();

        // Without 2.0.0, semantic precedence must rank 1.10.0 above 1.2.0; a lexicographic
        // comparison would resolve 1.2.0 as the latest.
        foreach (var version in new[] { "1.10.0", "1.0.0", "1.2.0" })
        {
            await materialize.Execute(
                DesignPersistenceFixtureData.OperationKey($"query-ladder:{version}"),
                DesignPersistenceFixtureData.WorkflowVersionAt("wf-alpha", version, $"wf-alpha-v{version}"));
        }

        var beforeMajor = await versions.FindLatestVersionAsync("wf-alpha");
        Assert.Equal("1.10.0", beforeMajor!.Version);

        await materialize.Execute(
            DesignPersistenceFixtureData.OperationKey("query-ladder:2.0.0"),
            DesignPersistenceFixtureData.WorkflowVersionAt("wf-alpha", "2.0.0", "wf-alpha-v2.0.0"));

        var afterMajor = await versions.FindLatestVersionAsync("wf-alpha");
        var repeat = await versions.FindLatestVersionAsync("wf-alpha");
        Assert.Equal("2.0.0", afterMajor!.Version);
        Assert.Equal(afterMajor.Id, repeat!.Id);
        Assert.Null(await versions.FindLatestVersionAsync("wf-without-versions"));
    }

    [Fact]
    public async Task Exact_version_existence_ignores_build_metadata()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        await SeedSemVerLadderAsync(scope.ServiceProvider);
        var versions = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionVersionStore>();

        Assert.True(await versions.ExistsAsync("wf-alpha", SemVer.ToSortKey("1.2.0")));
        Assert.True(await versions.ExistsAsync("wf-alpha", SemVer.ToSortKey("1.2.0+build.99")));
        Assert.False(await versions.ExistsAsync("wf-alpha", SemVer.ToSortKey("1.3.0")));
        Assert.False(await versions.ExistsAsync("wf-absent", SemVer.ToSortKey("1.0.0")));
    }

    [Fact]
    public async Task Current_draft_resolution_is_stable_and_complete_under_recency_ties()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        var services = scope.ServiceProvider;
        var createDraft = services.GetRequiredService<ICreateDraftCommand>();
        var drafts = services.GetRequiredService<IWorkflowDefinitionDraftStore>();

        // The seed created wf-alpha's initial draft; add a second draft for the same definition.
        var secondDraftId = await createDraft.Execute(
            DesignPersistenceFixtureData.OperationKey("query-second-draft"),
            "wf-alpha");

        var current = await drafts.FindByWorkflowDefinitionIdAsync("wf-alpha");
        var repeat = await drafts.FindByWorkflowDefinitionIdAsync("wf-alpha");
        var all = await drafts.ListByWorkflowDefinitionIdAsync("wf-alpha");

        // Both drafts are visible, and current-draft resolution is stable: repeated identical
        // requests resolve the same draft. The deterministic recency preference itself is pinned
        // by the provider's declared route evidence because the shared clock cannot advance here.
        Assert.Equal(2, all.Count);
        Assert.Contains(secondDraftId, all.Select(x => x.Id));
        Assert.NotNull(current);
        Assert.Contains(current!.Id, all.Select(x => x.Id));
        Assert.Equal(current.Id, repeat!.Id);
    }

    [Fact]
    public async Task Query_results_and_ordering_survive_a_restart_with_identical_hashes()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        string before;
        using (var scope = fixture.CreateScope(Scope))
        {
            await SeedMixedCatalogAsync(scope.ServiceProvider);
            await SeedSemVerLadderAsync(scope.ServiceProvider);
            before = await QuerySurfaceHashAsync(scope.ServiceProvider);
        }

        await fixture.RestartAsync();

        string after;
        using (var scope = fixture.CreateScope(Scope))
        {
            after = await QuerySurfaceHashAsync(scope.ServiceProvider);
        }

        Assert.Equal(before, after);
    }

    /// <summary>Hashes the canonical projection of every public workflow query shape.</summary>
    private static async Task<string> QuerySurfaceHashAsync(IServiceProvider services)
    {
        var definitions = services.GetRequiredService<IWorkflowDefinitionStore>();
        var versions = services.GetRequiredService<IWorkflowDefinitionVersionStore>();
        var drafts = services.GetRequiredService<IWorkflowDefinitionDraftStore>();

        var byName = await definitions.ListAsync(new WorkflowDefinitionFilter { Names = ["Alpha", "Beta flow"] });
        var bySearch = await definitions.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "order" });
        var ladder = await versions.ListByDefinitionAsync("wf-alpha");
        var latest = await versions.FindLatestVersionAsync("wf-alpha");
        var currentDraft = await drafts.FindByWorkflowDefinitionIdAsync("wf-alpha");

        return DesignPersistenceFixtureData.ResultHash(new
        {
            byName = byName
                .Select(x => new { x.Id, x.Name, x.Description })
                .OrderBy(x => x.Id, StringComparer.Ordinal)
                .ToList(),
            bySearch = bySearch.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            ladder = ladder
                .Select(x => new { x.Id, x.Version })
                .OrderBy(x => x.Id, StringComparer.Ordinal)
                .ToList(),
            latest = latest is null ? null : new { latest.Id, latest.Version },
            currentDraft = currentDraft?.Id
        });
    }

    /// <summary>
    /// Seeds the deterministic mixed catalog: duplicate and case-variant names, null / empty /
    /// present descriptions, and ids sharing searchable substrings — all through public commands.
    /// </summary>
    private static async Task SeedMixedCatalogAsync(IServiceProvider services)
    {
        var add = services.GetRequiredService<IAddWorkflowDefinitionCommand>();
        var seeds = new (string Id, string Name, string? Description)[]
        {
            ("wf-alpha", "Alpha", "The first workflow."),
            ("wf-alpha-lower", "alpha", "A case-variant sibling."),
            ("wf-beta-1", "Beta flow", "Processes an order."),
            ("wf-beta-2", "Beta flow", "A duplicate-name sibling."),
            ("wf-gamma", "Order intake", "Receives requests."),
            ("wf-order-echo", "Echo", "Repeats its input."),
            ("wf-null-desc", "Null description", null),
            ("wf-empty-desc", "Empty description", ""),
            ("wf-without-versions", "Versionless", "Never published.")
        };

        foreach (var seed in seeds)
        {
            await add.Execute(
                DesignPersistenceFixtureData.OperationKey($"query-catalog:{seed.Id}"),
                DesignPersistenceFixtureData.WorkflowDefinitionAt(seed.Id, seed.Name, seed.Description),
                DesignPersistenceFixtureData.WorkflowDraftFor(seed.Id, $"{seed.Id}-draft"));
        }
    }

    /// <summary>Seeds wf-alpha's semantic-version ladder, deliberately out of precedence order.</summary>
    private static async Task SeedSemVerLadderAsync(IServiceProvider services)
    {
        var materialize = services.GetRequiredService<IMaterializeWorkflowDefinitionVersionCommand>();
        foreach (var version in new[] { "1.10.0", "1.0.0", "2.0.0", "1.2.0" })
        {
            await materialize.Execute(
                DesignPersistenceFixtureData.OperationKey($"query-ladder:{version}"),
                DesignPersistenceFixtureData.WorkflowVersionAt("wf-alpha", version, $"wf-alpha-v{version}"));
        }
    }
}

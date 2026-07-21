using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// T038: provider-neutral activity design query-shape parity. Scenarios seed a mixed catalog through
/// public commands and pin equality, membership, substring, disjunctive-search, semantic-version, and
/// batching behavior of the public activity stores — including the contract's asymmetries (activity
/// description filtering is substring-based, unlike the workflow filter's equality). The suite
/// references only Elsa core contracts plus the shared fixture so the identical scenarios run on the
/// Groundwork target and the temporary EF oracle.
/// </summary>
public abstract class ActivityDesignQueryContractSuite
{
    protected abstract Task<IDesignPersistenceContractFixture> CreateFixtureAsync(CancellationToken cancellationToken = default);

    private const string Scope = DesignPersistenceFixtureData.ScopeA;

    [Fact]
    public async Task Type_key_equality_membership_and_existence_are_exact()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        var definitions = scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>();

        var exact = await definitions.ListAsync(new ActivityDefinitionFilter { ActivityTypeKey = "Fixture.Http.Get" });
        Assert.Equal(["act-http-get"], exact.Select(x => x.Id));

        var membership = await definitions.ListAsync(new ActivityDefinitionFilter
        {
            ActivityTypeKeys = ["Fixture.Http.Get", "Fixture.Timer.Delay"]
        });
        Assert.Equal(
            ["act-http-get", "act-timer"],
            membership.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));

        Assert.True(await definitions.ExistsByActivityTypeKeyAsync("Fixture.Http.Get"));
        Assert.False(await definitions.ExistsByActivityTypeKeyAsync("Fixture.Http.Absent"));
    }

    [Fact]
    public async Task Find_by_id_or_activity_type_key_resolves_both_identities()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        var definitions = scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>();

        var byId = await definitions.FindByIdOrActivityTypeKeyAsync("act-http-get", "Fixture.Http.Get");
        var byTypeKey = await definitions.FindByIdOrActivityTypeKeyAsync("no-such-id", "Fixture.Http.Get");
        var missing = await definitions.FindByIdOrActivityTypeKeyAsync("no-such-id", "Fixture.Http.Absent");

        Assert.Equal("act-http-get", byId?.Id);
        Assert.Equal("act-http-get", byTypeKey?.Id);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Category_equality_is_exact_while_description_filtering_is_substring_based()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        var definitions = scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>();

        // Category equality is case-sensitive: "HTTP" does not match the lowercase sibling.
        var category = await definitions.ListAsync(new ActivityDefinitionFilter { Category = "HTTP" });
        Assert.Equal(
            ["act-http-get", "act-http-post"],
            category.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));

        // The activity description filter is substring-based (unlike the workflow filter's equality):
        // "order" matches mid-sentence and null descriptions never match.
        var substring = await definitions.ListAsync(new ActivityDefinitionFilter { Description = "order" });
        Assert.Equal(["act-http-get"], substring.Select(x => x.Id));
    }

    [Fact]
    public async Task Search_term_spans_display_name_type_key_category_description_and_id()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        var definitions = scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>();

        // "order" hits act-http-get (description), act-echo (display name), act-order-marker (id).
        var hits = await definitions.ListAsync(new ActivityDefinitionFilter { SearchTerm = "order" });
        Assert.Equal(
            ["act-echo", "act-http-get", "act-order-marker"],
            hits.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));

        // "timer" hits the type key; "Utilities" hits the category; casing is insensitive.
        var byTypeKey = await definitions.ListAsync(new ActivityDefinitionFilter { SearchTerm = "TIMER" });
        Assert.Contains("act-timer", byTypeKey.Select(x => x.Id));
        var byCategory = await definitions.ListAsync(new ActivityDefinitionFilter { SearchTerm = "utilities" });
        Assert.Equal(
            ["act-echo", "act-order-marker"],
            byCategory.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));

        Assert.Empty(await definitions.ListAsync(new ActivityDefinitionFilter { SearchTerm = "zzz-absent" }));
    }

    [Fact]
    public async Task Id_membership_matches_exactly_and_empty_sets_match_nothing()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        var definitions = scope.ServiceProvider.GetRequiredService<IActivityDefinitionStore>();

        var membership = await definitions.ListAsync(new ActivityDefinitionFilter
        {
            Ids = ["act-echo", "act-timer", "act-absent"]
        });
        Assert.Equal(
            ["act-echo", "act-timer"],
            membership.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));

        Assert.Empty(await definitions.ListAsync(new ActivityDefinitionFilter { Ids = [] }));
        Assert.Empty(await definitions.ListAsync(new ActivityDefinitionFilter { ActivityTypeKeys = [] }));
    }

    [Fact]
    public async Task Exact_version_lookup_is_sortkey_exact_and_build_metadata_insensitive()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        await SeedVersionLadderAsync(scope.ServiceProvider);
        var versions = scope.ServiceProvider.GetRequiredService<IActivityDefinitionVersionStore>();

        var exact = await versions.FindByDefinitionAndSortKeyAsync("act-http-get", SemVer.ToSortKey("1.2.0"));
        var buildMetadata = await versions.FindByDefinitionAndSortKeyAsync("act-http-get", SemVer.ToSortKey("1.2.0+build.7"));
        var absent = await versions.FindByDefinitionAndSortKeyAsync("act-http-get", SemVer.ToSortKey("9.9.9"));

        Assert.Equal("act-http-get-v1.2.0", exact?.Id);
        Assert.Equal(exact?.Id, buildMetadata?.Id);
        Assert.Null(absent);
    }

    [Fact]
    public async Task Version_listing_batches_by_definition_ids_without_duplicates()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        await SeedVersionLadderAsync(scope.ServiceProvider);
        var versions = scope.ServiceProvider.GetRequiredService<IActivityDefinitionVersionStore>();

        var batch = await versions.ListByDefinitionIdsAsync(
            ["act-http-get", "act-timer", "act-http-get", "act-absent"]);

        // The batch is complete for every requested definition, contains no duplicate rows for the
        // repeated id, and ignores the unknown id.
        Assert.Equal(
            ["act-http-get-v1", "act-http-get-v1.10.0", "act-http-get-v1.2.0", "act-timer-v1"],
            batch.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));

        Assert.Empty(await versions.ListByDefinitionIdsAsync([]));
    }

    [Fact]
    public async Task Unfiltered_version_listing_is_complete_and_stable()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        using var scope = fixture.CreateScope(Scope);
        await SeedMixedCatalogAsync(scope.ServiceProvider);
        await SeedVersionLadderAsync(scope.ServiceProvider);
        var versions = scope.ServiceProvider.GetRequiredService<IActivityDefinitionVersionStore>();

        var all = await versions.ListAsync();
        var repeat = await versions.ListAsync();

        // Every seeded definition contributed its initial version plus the ladder additions.
        Assert.Equal(6 + 2, all.Count);
        Assert.Equal(all.Select(x => x.Id), repeat.Select(x => x.Id));
    }

    [Fact]
    public async Task Query_results_survive_a_restart_with_identical_hashes()
    {
        await using var fixture = await CreateFixtureAsync();
        await fixture.ValidateReadinessAsync();
        string before;
        using (var scope = fixture.CreateScope(Scope))
        {
            await SeedMixedCatalogAsync(scope.ServiceProvider);
            await SeedVersionLadderAsync(scope.ServiceProvider);
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

    private static async Task<string> QuerySurfaceHashAsync(IServiceProvider services)
    {
        var definitions = services.GetRequiredService<IActivityDefinitionStore>();
        var versions = services.GetRequiredService<IActivityDefinitionVersionStore>();

        var search = await definitions.ListAsync(new ActivityDefinitionFilter { SearchTerm = "order" });
        var category = await definitions.ListAsync(new ActivityDefinitionFilter { Category = "HTTP" });
        var ladder = await versions.ListByDefinitionAsync("act-http-get");
        var batch = await versions.ListByDefinitionIdsAsync(["act-http-get", "act-timer"]);

        return DesignPersistenceFixtureData.ResultHash(new
        {
            search = search.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            category = category
                .Select(x => new { x.Id, x.ActivityTypeKey, x.Category, x.DisplayName, x.Description })
                .OrderBy(x => x.Id, StringComparer.Ordinal)
                .ToList(),
            ladder = ladder
                .Select(x => new { x.Id, x.Version })
                .OrderBy(x => x.Id, StringComparer.Ordinal)
                .ToList(),
            batch = batch.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToList()
        });
    }

    /// <summary>
    /// Seeds six activity definitions with case-variant categories, null / empty / present
    /// descriptions, and searchable substrings spread across every disjunctive search field.
    /// </summary>
    private static async Task SeedMixedCatalogAsync(IServiceProvider services)
    {
        var add = services.GetRequiredService<IAddActivityDefinitionCommand>();
        var seeds = new (string Id, string TypeKey, string Category, string DisplayName, string? Description)[]
        {
            ("act-http-get", "Fixture.Http.Get", "HTTP", "HTTP GET", "Fetches an order."),
            ("act-http-post", "Fixture.Http.Post", "HTTP", "HTTP POST", null),
            ("act-timer", "Fixture.Timer.Delay", "Timers", "Delay", ""),
            ("act-echo", "Fixture.Echo", "Utilities", "Order echo", "Repeats input."),
            ("act-case", "fixture.http.lower", "http", "http tools", "Lowercase sibling."),
            ("act-order-marker", "Fixture.Marker", "Utilities", "Marker", null)
        };

        foreach (var seed in seeds)
        {
            await add.Execute(
                DesignPersistenceFixtureData.OperationKey($"activity-catalog:{seed.Id}"),
                DesignPersistenceFixtureData.ActivityDefinitionAt(
                    seed.Id, seed.TypeKey, seed.Category, seed.DisplayName, seed.Description),
                DesignPersistenceFixtureData.ActivityVersion(
                    id: $"{seed.Id}-v1",
                    definitionId: seed.Id));
        }
    }

    /// <summary>Adds act-http-get's extra versions, deliberately out of precedence order.</summary>
    private static async Task SeedVersionLadderAsync(IServiceProvider services)
    {
        var addVersion = services.GetRequiredService<IAddActivityDefinitionVersionCommand>();
        foreach (var version in new[] { "1.10.0", "1.2.0" })
        {
            await addVersion.Execute(
                DesignPersistenceFixtureData.OperationKey($"activity-ladder:{version}"),
                DesignPersistenceFixtureData.ActivityVersion(
                    version: version,
                    id: $"act-http-get-v{version}",
                    definitionId: "act-http-get"));
        }
    }
}

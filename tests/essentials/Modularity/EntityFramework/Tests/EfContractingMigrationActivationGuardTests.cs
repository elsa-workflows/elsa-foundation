using Elsa.Modularity.Core.Exceptions;
using Elsa.Modularity.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Xunit;
using static Elsa.Modularity.EntityFramework.Tests.ActivationGuardHarness;
using static Elsa.Persistence.EntityFramework.Tests.ContractingModule;

namespace Elsa.Modularity.EntityFramework.Tests;

/// <summary>
/// Spec 185, FR-025 and User Story 5, scenarios 2 and 3: under both migrate policies, enabling a feature whose module
/// would apply a contracting migration its schema family is not yet finalized for is refused before anything is saved,
/// naming the family and the version it waits for, exactly as the module's migrator would refuse the batch at Prepare;
/// and once the family reaches that version the same request goes through.
/// </summary>
public sealed class EfContractingMigrationActivationGuardTests : IDisposable
{
    private const string Feature = "ContractingProbe";
    private const string Sentinel = "SENTINEL-2136";

    private readonly ActivationGuardHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(EfMigratePolicy.Validate)]
    [InlineData(EfMigratePolicy.AutoMigrate)]
    public async Task Refuses_under_both_policies_a_feature_whose_module_would_apply_a_contracting_migration_too_early(EfMigratePolicy policy)
    {
        var connection = ConnectionTo(_harness.Database($"contracting-{Sentinel}"));
        await StageAsync("Sqlite", connection, DropObsolete);

        var decision = await Guard(policy).EvaluateAsync(Request(Enabled(Feature, connection: connection)));

        var refusal = Assert.Single(decision.Refusals);
        Assert.Equal(Feature, refusal.Feature);
        Assert.StartsWith($"Feature '{Feature}' depends on EF module '{Name}'. EF module '{Name}' was not migrated", refusal.Reason, StringComparison.Ordinal);
        Assert.Contains(
            $"'{Contract}' removes what schema family '{Family}' reads before '{CurrentVersion}', and '{Family}' is finalized at " +
            $"'{EarlierVersion}' in this database, not at '{CurrentVersion}' or later",
            refusal.Reason,
            StringComparison.Ordinal);
        Assert.EndsWith("Nothing was saved.", refusal.Reason, StringComparison.Ordinal);
        var exception = new FeatureActivationRefusedException(decision.Refusals);
        foreach (var text in new[] { refusal.Reason, exception.ToString() }.Concat(_harness.Logs.Lines))
        {
            Assert.DoesNotContain(Sentinel, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Data Source", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(Contract, await AppliedAsync("Sqlite", connection));
    }

    /// <summary>Scenario 3 under <c>AutoMigrate</c>: the retried request enables, and the migrator applies the batch at Prepare.</summary>
    [Fact]
    public async Task Allows_the_same_request_under_automigrate_once_the_family_is_finalized_at_the_version()
    {
        var connection = ConnectionTo(_harness.Database("contracting-finalized"));
        await StageAsync("Sqlite", connection, DropObsolete);
        var request = Request(Enabled(Feature, connection: connection));
        Assert.False((await Guard(EfMigratePolicy.AutoMigrate).EvaluateAsync(request)).IsAllowed);

        await FinalizeAsync("Sqlite", connection, CurrentVersion);

        Assert.True((await Guard(EfMigratePolicy.AutoMigrate).EvaluateAsync(request)).IsAllowed);
    }

    /// <summary>
    /// Scenario 3 under <c>Validate</c>: once finalized the batch is only pending, so the request is refused as any pending
    /// migration is, pointing at <c>apply</c>, and enables once that has run.
    /// </summary>
    [Fact]
    public async Task Under_validate_once_finalized_the_request_waits_only_for_the_migrations_to_be_applied()
    {
        var connection = ConnectionTo(_harness.Database("contracting-validate"));
        await StageAsync("Sqlite", connection, DropObsolete);
        await FinalizeAsync("Sqlite", connection, CurrentVersion);
        var request = Request(Enabled(Feature, connection: connection));

        var pending = Assert.Single((await Guard(EfMigratePolicy.Validate).EvaluateAsync(request)).Refusals);
        Assert.Contains("which has migrations that are not applied to its database", pending.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("was not migrated", pending.Reason, StringComparison.Ordinal);

        await using (var context = Create("Sqlite", connection))
            await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite);

        Assert.True((await Guard(EfMigratePolicy.Validate).EvaluateAsync(request)).IsAllowed);
    }

    /// <summary>A database the module has never been admitted in holds nothing a contracting migration could break.</summary>
    [Fact]
    public async Task Allows_a_fresh_database_under_automigrate()
    {
        var connection = ConnectionTo(_harness.Database("contracting-fresh"));

        Assert.True((await Guard(EfMigratePolicy.AutoMigrate).EvaluateAsync(Request(Enabled(Feature, connection: connection)))).IsAllowed);
    }

    private EfPendingMigrationActivationGuard Guard(EfMigratePolicy policy) =>
        _harness.Guard(policy, configuration: null, typeof(ContractingProbeFeature).Assembly);
}

/// <summary>The feature a shell enables to use the synthetic contracting module, carrying the settings a module's EF feature does.</summary>
[UsesEfModule(ContractingModule.Name)]
public sealed class ContractingProbeFeature
{
    public string? Provider { get; set; }

    public string? ConnectionString { get; set; }
}

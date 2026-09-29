using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.ContractingModule;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>
/// Spec 185, FR-024 and SC-006 on each server engine: the finalization record is read through the engine's own catalog
/// and the refusal leaves the contracting migration's column and the migrations history as they were, until the family
/// is finalized at the version it names, when the same batch applies. On a database no host has admitted the module in,
/// the migrator creates the family's record before the contraction, proved by the same scenarios the SQLite suite runs:
/// the step before the contraction constructs the engine's own migrator type, which only a run on that engine exercises.
/// </summary>
public sealed class ContractingMigrationProviderTests
{
    public static TheoryData<string> Providers => new() { "SqlServer", "PostgreSql", "MySql" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public Task A_contracting_migration_is_refused_until_its_family_is_finalized_and_then_applies(string provider) =>
        ProviderDatabase.RunAsync(provider, async connection =>
        {
            await StageAsync(provider, connection, DropObsolete);

            await using (var context = Create(provider, connection))
            {
                var refusal = await Assert.ThrowsAsync<EfContractingMigrationRefusedException>(
                    () => EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(provider)));
                Assert.Equal(
                    new EfContractingMigrationRefusal(Contract, Family, CurrentVersion, EarlierVersion, EfContractingMigrationRefusalReason.NotFinalized),
                    Assert.Single(refusal.Refusals));
            }

            Assert.Equal([Initial, Expand, DropObsolete], await AppliedAsync(provider, connection));
            Assert.True(await HasColumnAsync(provider, connection, RowsTable, "Legacy"));

            await FinalizeAsync(provider, connection, CurrentVersion);
            await using (var context = Create(provider, connection))
                await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(provider));

            Assert.Equal([Initial, Expand, DropObsolete, Contract], await AppliedAsync(provider, connection));
            Assert.False(await HasColumnAsync(provider, connection, RowsTable, "Legacy"));
        });

    // --- The migrator seeds first where no host has admitted the module (#2136) -------------------------------------

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public Task Persistence_apply_on_a_fresh_database_creates_the_record_before_the_contraction_so_an_older_release_is_refused(string provider) =>
        ProviderDatabase.RunAsync(provider, connection => ContractingSeedScenarios.ApplyThenAnOlderReleaseStartsAsync(provider, connection));

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public Task A_crash_between_the_seed_and_the_contraction_leaves_an_older_release_refused_and_the_next_apply_completes(string provider) =>
        ProviderDatabase.RunAsync(provider, connection => ContractingSeedScenarios.ACrashBetweenTheSeedAndTheContractionAsync(
            provider,
            connection,
            async interceptors =>
            {
                await using var context = Create(provider, connection, interceptors);
                await EfDatabaseMigrator.ApplyAsync(context, EfRelationalProviderBinding.ExpectedProviderName(provider));
            },
            ContractingSeedScenarios.MachineMigrator));

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public Task An_older_release_whose_gate_creates_the_record_first_keeps_the_contraction_from_running(string provider) =>
        ProviderDatabase.RunAsync(provider, connection =>
            ContractingSeedScenarios.AnOlderReleaseThatCreatesTheRecordFirstKeepsTheContractionFromRunningAsync(provider, connection));

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public Task An_older_release_that_starts_after_the_seed_is_refused_and_the_contraction_runs(string provider) =>
        ProviderDatabase.RunAsync(provider, connection =>
            ContractingSeedScenarios.AnOlderReleaseThatStartsAfterTheSeedIsRefusedAndTheContractionRunsAsync(provider, connection));
}

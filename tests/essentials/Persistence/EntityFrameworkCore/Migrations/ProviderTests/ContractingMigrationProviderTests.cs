using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.ContractingModule;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>
/// Spec 185, FR-024 and SC-006 on a server engine: the finalization record is read through PostgreSQL's own catalog and
/// the refusal leaves the contracting migration's column and the migrations history as they were, until the family is
/// finalized at the version it names, when the same batch applies. On a database no host has admitted the module in,
/// the migrator creates the family's record before the contraction, proved by the same scenarios the SQLite suite runs.
/// </summary>
public sealed class ContractingMigrationProviderTests
{
    private const string Provider = "PostgreSql";

    [SkippableFact]
    public Task A_contracting_migration_is_refused_on_postgresql_until_its_family_is_finalized_and_then_applies() =>
        ProviderDatabase.RunAsync(Provider, async connection =>
        {
            await StageAsync(Provider, connection, DropObsolete);

            await using (var context = Create(Provider, connection))
            {
                var refusal = await Assert.ThrowsAsync<EfContractingMigrationRefusedException>(
                    () => EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.PostgreSql));
                Assert.Equal(
                    new EfContractingMigrationRefusal(Contract, Family, CurrentVersion, EarlierVersion, EfContractingMigrationRefusalReason.NotFinalized),
                    Assert.Single(refusal.Refusals));
            }

            Assert.Equal([Initial, Expand, DropObsolete], await AppliedAsync(Provider, connection));
            Assert.True(await HasColumnAsync(Provider, connection, RowsTable, "Legacy"));

            await FinalizeAsync(Provider, connection, CurrentVersion);
            await using (var context = Create(Provider, connection))
                await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.PostgreSql);

            Assert.Equal([Initial, Expand, DropObsolete, Contract], await AppliedAsync(Provider, connection));
            Assert.False(await HasColumnAsync(Provider, connection, RowsTable, "Legacy"));
        });

    // --- The migrator seeds first where no host has admitted the module (#2136), on PostgreSQL ----------------------

    [SkippableFact]
    public Task Persistence_apply_on_a_fresh_postgresql_database_creates_the_record_before_the_contraction_so_an_older_release_is_refused() =>
        ProviderDatabase.RunAsync(Provider, connection => ContractingSeedScenarios.ApplyThenAnOlderReleaseStartsAsync(Provider, connection));

    [SkippableFact]
    public Task A_crash_between_the_seed_and_the_contraction_on_postgresql_leaves_an_older_release_refused_and_the_next_apply_completes() =>
        ProviderDatabase.RunAsync(Provider, connection => ContractingSeedScenarios.ACrashBetweenTheSeedAndTheContractionAsync(
            Provider,
            connection,
            async interceptors =>
            {
                await using var context = Create(Provider, connection, interceptors);
                await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.PostgreSql);
            },
            ContractingSeedScenarios.MachineMigrator));

    [SkippableFact]
    public Task An_older_release_whose_gate_creates_the_record_first_on_postgresql_keeps_the_contraction_from_running() =>
        ProviderDatabase.RunAsync(Provider, connection =>
            ContractingSeedScenarios.AnOlderReleaseThatCreatesTheRecordFirstKeepsTheContractionFromRunningAsync(Provider, connection));

    [SkippableFact]
    public Task An_older_release_that_starts_after_the_seed_on_postgresql_is_refused_and_the_contraction_runs() =>
        ProviderDatabase.RunAsync(Provider, connection =>
            ContractingSeedScenarios.AnOlderReleaseThatStartsAfterTheSeedIsRefusedAndTheContractionRunsAsync(Provider, connection));
}

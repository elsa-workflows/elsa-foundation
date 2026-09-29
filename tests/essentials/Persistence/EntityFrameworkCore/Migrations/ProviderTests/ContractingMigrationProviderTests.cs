using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Xunit;
using static Elsa.Persistence.EntityFramework.Tests.ContractingModule;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>
/// Spec 185, FR-024 and SC-006 on a server engine: the finalization record is read through PostgreSQL's own catalog and
/// the refusal leaves the contracting migration's column and the migrations history as they were, until the family is
/// finalized at the version it names, when the same batch applies.
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
}

using Elsa.Etl.Core.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Connector_run_completion_migration_keeps_prior_finite_rows_and_leaves_their_request_null()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var database = new EtlRuntimeTestDatabase(directory);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261021150000_ConnectorBatchAcceptance");
        var clock = new MutableTimeProvider();
        var fixture = await SeedLegacyHttpContinuationAsync(context, clock.GetUtcNow());
        await migrator.MigrateAsync("20261021170000_ConnectorInspectionEffects");
        var before = await ConnectorRunColumnsAsync(context);

        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();

        Assert.Equal(before, await ConnectorRunColumnsAsync(context));
        Assert.Null((await context.ConnectorRuns.AsNoTracking().SingleAsync()).CompletionRequestedAtUtcTicks);
        Assert.False(context.Database.HasPendingModelChanges());
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context, clock);
        Assert.IsType<EtlConnectorPartitionLease>(await store.ClaimConnectorPartitionAsync(
            fixture.Admission.Invocation.Scope, fixture.Admission.Invocation.RunId, "completion-migration"));
        var invalid = await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(
            "UPDATE elsa_etl_connector_runs SET CompletionRequestedAtUtcTicks = 0;"));
        Assert.Equal(19, invalid.SqliteErrorCode);
    }

    private static async Task<string> ConnectorRunColumnsAsync(DbContext context)
    {
        // Every column the prior writers stored, read raw so the comparison does not depend on the current entity model.
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ScopeKey, RunKey, BindingsJson, PreparedJson, RetainedInputBytes, SourceInputReservedBytes,
                   RetainedMetadataBytes, RetainedOutputBytes, Revision, SchemaVersion
            FROM elsa_etl_connector_runs;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "<null>" : reader.GetValue(i).ToString())));
        return string.Join('\n', rows);
    }
}

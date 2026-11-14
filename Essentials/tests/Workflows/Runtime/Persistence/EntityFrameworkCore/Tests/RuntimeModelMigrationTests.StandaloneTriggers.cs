using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class RuntimeModelMigrationTests
{
    [Fact]
    public async Task Standalone_migration_retains_an_actual_old_schema_workflow_schedule_and_its_live_claim_then_uses_every_new_table()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite(connection, sql => sql.MigrationsHistoryTable(RuntimeEfModule.HistoryTableName)).Options;
        var now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        var access = new MigrationAccess();
        RecurringTriggerOccurrenceClaim claim;
        Dictionary<string, string> before;
        string[] previousHistory;
        await using (var old = new RuntimeSqliteDbContext(options))
        {
            var previous = old.Database.GetMigrations().Single(id => id.EndsWith("_OutboxClaimDeliveredAt", StringComparison.Ordinal));
            await old.GetService<IMigrator>().MigrateAsync(previous);
            previousHistory = await History(connection);
            Assert.Equal(5, previousHistory.Length);
            // A genuine ordinary workflow schedule writer/claim transition runs against the pre-standalone schema.
            // This supplements the separate frozen legacy-host database; it is not substituted for that capture.
            var schedules = new EfRecurringTriggerScheduleStore(old, access, new MigrationCodec());
            var schedule = new RecurringTriggerSchedule(RecurringTriggerSchedule.BuildId("retained-artifact", "retained-timer"),
                "retained-artifact", "retained-timer", "Timer", "retained-stimulus", RecurringScheduleKind.Interval,
                "PT1H", now.AddMinutes(-1), now.AddDays(-1));
            await schedules.SaveAsync(schedule);
            claim = Assert.Single(await schedules.ClaimDueAsync(new("retained-owner", now, TimeSpan.FromMinutes(5), 1)));
            before = await LegacyRows(connection);
            var row = await old.RecurringTriggerSchedules.AsNoTracking().SingleAsync();
            Assert.Equal("1.0.0", row.SchemaVersion);
            Assert.NotNull(row.ArtifactId); Assert.NotNull(row.ExecutableNodeId); Assert.NotNull(row.StimulusHash); Assert.NotNull(row.ClaimOwnerId);
            Assert.Equal(claim.FencingToken, row.ClaimToken);
        }
        await using var upgraded = new RuntimeSqliteDbContext(options);
        await upgraded.Database.MigrateAsync();
        var history = await History(connection);
        Assert.Equal(previousHistory, history.Take(previousHistory.Length)); Assert.EndsWith("_StandaloneTriggers", history[^1]);
        Assert.Equal(previousHistory.Length + 1, history.Length);
        var after = await LegacyRows(connection);
        foreach (var (table, bytes) in before) Assert.Equal(bytes, after[table]);
        Assert.Equal(before.Count + 4, after.Count);
        var retained = await upgraded.RecurringTriggerSchedules.AsNoTracking().SingleAsync();
        Assert.Equal("1.0.0", retained.SchemaVersion); Assert.NotNull(retained.ClaimOwnerId); Assert.Equal(claim.FencingToken, retained.ClaimToken);
        var reader = new EfRecurringTriggerScheduleStore(upgraded, access, new MigrationCodec());
        Assert.Equal(JsonSerializer.Serialize(claim.Schedule), JsonSerializer.Serialize(await reader.FindAsync(claim.Schedule.ScheduleId)));
        Assert.Empty(await reader.ClaimDueAsync(new("peer", now.AddSeconds(1), TimeSpan.FromMinutes(1), 1)));
        await AssertNullableAsync(connection, RuntimeOperationalStateEfModule.RecurringScheduleTableName,
            ["ArtifactId", "ArtifactIdHash", "ArtifactIdOrderKey", "ExecutableNodeId", "StimulusType", "StimulusHash"]);
        await AssertNullableAsync(connection, RuntimePostCommitOutboxEfModule.TableName,
            ["WorkflowExecutionId", "WorkflowExecutionIdHash", "WorkflowExecutionIdOrderKey"]);

        var scope = new TriggerScope(access.Current.RequireScope(), null);
        var trigger = new StandaloneTrigger("migrated-trigger", "Migrated trigger", "event", 1, JsonSerializer.SerializeToElement(new { eventName = "migrated" }),
            1, 1, 1, TriggerDesiredState.Paused, new(TriggerManagementKind.Studio), now, now, "operator");
        var subscription = new TriggerSubscription("migrated-subscription", trigger.Id, "target-definition", "default", "native", 1, 1, false,
            [], new(TriggerManagementKind.Studio));
        var store = new EfStandaloneTriggerStore(upgraded, access, TimeProvider.System);
        var receipt = await store.WriteAsync(scope, new("migration-request", "migration-request", "operator", null, trigger, [subscription]));
        Assert.Equal(trigger.Id, (await store.FindAsync(scope, trigger.Id))!.Trigger.Id);
        Assert.Equal(subscription.Id, (await store.FindSubscriptionAsync(scope, subscription.Id))!.Id);
        Assert.Equal(receipt, await store.FindMutationAsync(scope, "operator", "migration-request"));
        var reservation = new RuntimeIngressReservation(RuntimeIngressReservation.CreateIdentity(scope.Persistence, "Http", "migration-route"), scope,
            "Http", "migration-route", RuntimeIngressReservation.StandaloneTriggerOwner, trigger.Id, 1, 1, new Dictionary<string, string>());
        var reservations = new EfIngressReservationStore(upgraded, access);
        await using (var transaction = await upgraded.Database.BeginTransactionAsync())
        {
            await reservations.StageOwnerAsync(scope, reservation.OwnerKind, trigger.Id, [reservation], default);
            await upgraded.SaveChangesAsync(); await transaction.CommitAsync();
        }
        Assert.Equal(JsonSerializer.Serialize(reservation), JsonSerializer.Serialize(await reservations.FindAsync(scope, "Http", "migration-route")));
        Assert.Equal(1, await upgraded.StandaloneTriggers.CountAsync()); Assert.Equal(1, await upgraded.TriggerSubscriptions.CountAsync());
        Assert.Equal(1, await upgraded.TriggerMutations.CountAsync()); Assert.Equal(1, await upgraded.IngressReservations.CountAsync());
        // No execution or source is fabricated by the schema upgrade or new authoring/reservation writes.
        Assert.Empty(await upgraded.WorkflowExecutionStates.AsNoTracking().ToArrayAsync());
        Assert.Empty(await upgraded.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync());
    }

    private static async Task AssertNullableAsync(DbConnection connection, string table, IReadOnlyCollection<string> columns)
    {
        await using var command = connection.CreateCommand(); command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync(); var observed = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
            if (columns.Contains(reader.GetString(1))) { Assert.Equal(0L, reader.GetInt64(3)); observed.Add(reader.GetString(1)); }
        Assert.Equal(columns.Order(StringComparer.Ordinal), observed.Order(StringComparer.Ordinal));
    }
    private sealed class MigrationAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("migration-retention")); }
    private sealed class MigrationCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => Convert.FromBase64String(token);
    }
}

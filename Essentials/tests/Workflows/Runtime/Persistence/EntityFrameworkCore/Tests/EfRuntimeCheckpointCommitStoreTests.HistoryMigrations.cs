using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Retained_SQLite_Runtime_upgrade_preserves_legacy_run_timer_and_exact_replay_marker()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options;
        var legacy = Commit("legacy-before-history");
        var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(legacy);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string markerBefore;
        await using (var before = new RuntimeSqliteDbContext(options))
        {
            var chain = before.Database.GetMigrations().ToArray();
            Assert.EndsWith("_RunHistory", chain[^1]); Assert.EndsWith("_UserTasks", chain[^2]);
            await before.GetService<IMigrator>().MigrateAsync(chain[^2]);
            var state = Execution("workflow-a", "tenant-a");
            before.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", "workflow-a"), 1));
            markerBefore = JsonSerializer.Serialize(new { legacy.CommitId, legacy.WorkflowExecutionId, OccurredAt = legacy.Checkpoint.OccurredAt, Fingerprint = fingerprint,
                PendingPostCommitWorkIds = Array.Empty<string>(), ConsumedSchedulerWorkItemIds = Array.Empty<string>() }, json);
            before.RuntimeCheckpointCommits.Add(new RuntimeCheckpointCommitEntity
            {
                Id = EfRuntimeOperationalStoreSupport.CompositeId("tenant-a", legacy.CommitId),
                ScopeKey = EfRuntimeOperationalStoreSupport.Encode("tenant-a"), ScopeKeyHash = EfRuntimeOperationalStoreSupport.Hash("tenant-a"),
                WorkflowExecutionId = EfRuntimeOperationalStoreSupport.Encode("workflow-a"), WorkflowExecutionIdHash = EfRuntimeOperationalStoreSupport.Hash("workflow-a"),
                WorkflowExecutionIdOrderKey = EfRuntimeOperationalStoreSupport.Order("workflow-a"),
                CommitId = EfRuntimeOperationalStoreSupport.Encode(legacy.CommitId), CommitIdHash = EfRuntimeOperationalStoreSupport.Hash(legacy.CommitId),
                OccurredAtUtcTicks = OccurredAt.UtcTicks, Fingerprint = fingerprint, ContentJson = markerBefore,
                PendingPostCommitWorkIdsJson = "[]", ConsumedSchedulerWorkItemIdsJson = "[]", SchemaVersion = RuntimeOperationalStateEfModule.SchemaVersion, Revision = 1
            });
            await before.SaveChangesAsync();
            var timer = new DurableTimer("legacy-timer", "workflow-a", "type", "hash", OccurredAt.AddMinutes(1), OccurredAt);
            // These are the actual pre-history columns. The new model cannot insert a current timer entity until
            // its additive columns exist, so retain the original writer-shaped row without touching saved inputs.
            await before.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO elsa_runtime_durable_timer
                (Id, ScopeKey, ScopeKeyHash, WorkflowExecutionId, WorkflowExecutionIdHash, WorkflowExecutionIdOrderKey,
                 TimerId, TimerIdHash, TimerIdOrderKey, StimulusType, StimulusHash, DueTimeUtcTicks, DueTimeOffsetMinutes,
                 CreatedAtUtcTicks, CreatedAtOffsetMinutes, ClaimOrderKey, ClaimToken, FailureCount, ContentJson, SchemaVersion, Revision)
                VALUES ({EfRuntimeOperationalStoreSupport.CompositeId("tenant-a", "workflow-a", "legacy-timer")},
                {EfRuntimeOperationalStoreSupport.Encode("tenant-a")}, {EfRuntimeOperationalStoreSupport.Hash("tenant-a")},
                {EfRuntimeOperationalStoreSupport.Encode("workflow-a")}, {EfRuntimeOperationalStoreSupport.Hash("workflow-a")}, {EfRuntimeOperationalStoreSupport.Order("workflow-a")},
                {EfRuntimeOperationalStoreSupport.Encode(timer.TimerId)}, {EfRuntimeOperationalStoreSupport.Hash(timer.TimerId)}, {EfRuntimeOperationalStoreSupport.Order(timer.TimerId)},
                {EfRuntimeOperationalStoreSupport.Encode(timer.StimulusType)}, {EfRuntimeOperationalStoreSupport.Encode(timer.StimulusHash)},
                {timer.DueTime.UtcTicks}, {0}, {timer.CreatedAt.UtcTicks}, {0}, {"temporary-old-writer-key"}, {0L}, {0},
                {RuntimeArtifactJson.Serialize(timer)}, {RuntimeOperationalStateEfModule.SchemaVersion}, {1L})
                """);
            // Existing claim ordering is unrelated to schema addition; fill it with the unchanged production formula.
            var frame = $"{timer.WorkflowExecutionId.Length}:{timer.WorkflowExecutionId}{timer.TimerId.Length}:{timer.TimerId}";
            var order = timer.DueTime.UtcTicks.ToString("D19", System.Globalization.CultureInfo.InvariantCulture) + "." +
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(frame)));
            await before.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_runtime_durable_timer SET ClaimOrderKey = {order}");
        }
        await using var after = new RuntimeSqliteDbContext(options);
        await after.Database.MigrateAsync();
        var marker = await after.RuntimeCheckpointCommits.AsNoTracking().SingleAsync();
        Assert.Equal(markerBefore, marker.ContentJson); Assert.Equal(fingerprint, marker.Fingerprint); Assert.Equal(1, marker.Revision);
        var access = new FixedAccessor("tenant-a");
        await new EfRuntimeCheckpointCommitStore(after, access).CommitAsync(legacy, Decision());
        Assert.Empty(await after.RuntimeHistoryHeads.ToArrayAsync()); Assert.Empty(await after.RuntimeHistoryEvents.ToArrayAsync());
        var native = await new EfWorkflowExecutionStateStore(after, access, new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "run-history-migration-signing-key-32" }))).FindAsync("workflow-a");
        Assert.Null(native!.HistoryCapturePolicy); Assert.Null(native.HistoryCaptureEnabledAtAdmission);
        var timerStore = TimerHistoryStore(after);
        var oldTimer = await timerStore.FindAsync("workflow-a", "legacy-timer"); Assert.Null(oldTimer!.HistoryResponsibility);
        Assert.False(await timerStore.HasPendingHistoryAsync("workflow-a"));
        var row = await after.DurableTimers.AsNoTracking().SingleAsync(); Assert.False(row.HistoryPending); Assert.False(row.HistoryRetired); Assert.Equal(0, row.HistoryRevision);
        // New capture settings cannot retrofit a timer source which already won under its old admission.
        Assert.Null((await timerStore.SaveAsync(HistoryTimer("legacy-timer"))).HistoryResponsibility);
    }
}

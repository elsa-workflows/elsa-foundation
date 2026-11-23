using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

internal static partial class RuntimeHistoryCheckpointProviderContract
{
    // Headers are controlled admissions; publication, public DI, mutations, SQL refusal and
    // acknowledgement reconciliation are real. This does not qualify authored capture or a host.
    private static async Task PublicOwner(Database database, string engine, string scenario)
    {
        var lost = new LostAcknowledgement();
        using var provider = OwnerServices(database, engine, lost).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new(Scope)));
        var context = services.GetRequiredService<RuntimeDbContext>();
        var native = services.GetRequiredService<IRuntimeCheckpointCommitStore>();
        var reader = services.GetRequiredService<IRuntimeHistoryStore>();
        var observer = services.GetRequiredService<IRuntimeHistoryObservationWriter>();
        var retention = services.GetRequiredService<IRuntimeHistoryRetentionStore>();
        var executions = services.GetRequiredService<IWorkflowExecutionStateStore>();
        var deletions = services.GetRequiredService<IWorkflowExecutionDeletionStore>();
        Assert.True(deletions.IsSupported);
        Assert.Same(observer, retention); Assert.True(observer.CanRecordHistory(reader));
        Assert.True(native.SupportsHistory); Assert.True(native.CanCommitHistory(reader));
        await native.CommitAsync(Captured("original"), Immediate);
        var observation = new RuntimeHistoryObservation(Policy, new("owner-observed", RunId, 0,
            RuntimeHistoryEventCategory.DeliveryObservation, "DeliveryReceived", At, "activity")
        {
            Authority = RuntimeHistoryAuthority.DeliveryObservation, OccurredAtKnown = false,
            BookmarkId = "bookmark", Causation = new(DeliveryId: "owner-delivery")
        });

        switch (scenario)
        {
            case "owner-replay-purge":
                var frozen = await ((IRuntimeHistoryOccurrenceStore)reader).ReadOccurrencesAsync(new(RunId, accessBinding: "owner-access"));
                Assert.Equal(1, frozen.TotalCount); Assert.NotNull(frozen.SequenceToken);
                var recorded = await observer.RecordAsync(observation); Assert.True(recorded.Recorded); Assert.Equal(3, recorded.Event!.Sequence);
                var same = await observer.RecordAsync(observation); Assert.True(same.Recorded); Assert.NotNull(same.Event);
                Assert.Equal(recorded.Event.HistoryEventId, same.Event.HistoryEventId);
                Assert.Equal(recorded.Event.Sequence, same.Event.Sequence); Assert.Equal(recorded.Event.RecordedAt, same.Event.RecordedAt);
                Assert.Equal(recorded.Event.PhysicalCommitId, same.Event.PhysicalCommitId); Assert.Equal(recorded.Event.OccurredAt, same.Event.OccurredAt);
                Assert.Equal(2, await retention.PurgeThroughAsync(RunId, 2));
                Assert.Null(await reader.FindEventAsync(RunId, "created"));
                Assert.Equal(3, (await reader.FindEventAsync(RunId, "owner-observed"))!.Sequence);
                var retired = await ((IRuntimeHistoryOccurrenceStore)reader).ReadOccurrencesAsync(new(RunId,
                    sequenceToken: frozen.SequenceToken, accessBinding: "owner-access"));
                Assert.Equal(RuntimeHistoryOccurrenceAvailability.Invalidated, retired.Availability); Assert.Empty(retired.Items);
                Assert.Equal(3, await retention.PurgeThroughAsync(RunId, long.MaxValue));
                var replay = await observer.RecordAsync(observation); Assert.True(replay.Recorded); Assert.Null(replay.Event);
                await AssertOwnerReadback(database, 3, 3);
                break;
            case "owner-observation-rollback":
            case "owner-purge-rollback":
                var purge = scenario == "owner-purge-rollback";
                await OwnerFailureTrigger(database, engine, purge ? "UPDATE" : "INSERT");
                var error = purge
                    ? await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => retention.PurgeThroughAsync(RunId, 2).AsTask())
                    : await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => observer.RecordAsync(observation).AsTask());
                Assert.Equal(RunId, error.WorkflowExecutionId); Assert.Contains(Causes(error), cause => cause is DbException);
                Assert.False(context.ChangeTracker.HasChanges()); Assert.Null(context.Database.CurrentTransaction);
                await database.AssertCaptured(1, "original");
                await DropOwnerFailureTrigger(database, engine);
                if (purge)
                { Assert.Equal(2, await retention.PurgeThroughAsync(RunId, 2)); await AssertOwnerReadback(database, 2, 2); }
                else
                { Assert.Equal(3, (await observer.RecordAsync(observation)).Event!.Sequence); await AssertOwnerReadback(database, 3, 0); }
                break;
            case "owner-lost-ack":
                lost.Arm(); var received = await observer.RecordAsync(observation);
                Assert.True(received.Recorded); Assert.Equal(3, received.Event!.Sequence); Assert.Equal(1, lost.Losses);
                lost.Arm(); Assert.Equal(3, await retention.PurgeThroughAsync(RunId, long.MaxValue)); Assert.Equal(2, lost.Losses);
                var purged = await observer.RecordAsync(observation); Assert.True(purged.Recorded); Assert.Null(purged.Event);
                Assert.False(context.ChangeTracker.HasChanges()); Assert.Null(context.Database.CurrentTransaction);
                await AssertOwnerReadback(database, 3, 3);
                break;
            case "owner-delete":
            case "owner-delete-terminal":
            case "owner-delete-rollback":
            case "owner-delete-terminal-rollback":
                var terminal = scenario.Contains("terminal", StringComparison.Ordinal);
                if (terminal)
                {
                    var retained = await executions.FindAsync(RunId); Assert.NotNull(retained);
                    await executions.SaveAsync(retained with { Status = WorkflowExecutionStatus.Completed });
                }
                if (scenario.EndsWith("rollback", StringComparison.Ordinal))
                {
                    await OwnerFailureTrigger(database, engine, "DELETE");
                    var deletion = await Assert.ThrowsAsync<WorkflowExecutionStateEntityFrameworkPersistenceException>(() => Delete().AsTask());
                    Assert.Contains(Causes(deletion), cause => cause is DbException);
                    Assert.Null(context.Database.CurrentTransaction); Assert.Empty(context.ChangeTracker.Entries());
                    Assert.NotNull(await executions.FindAsync(RunId)); Assert.False(await executions.IsDeletionTombstonedAsync(RunId));
                    await database.AssertCaptured(1, "original");
                    await DropOwnerFailureTrigger(database, engine);
                }
                await Delete();
                Assert.Null(await executions.FindAsync(RunId)); Assert.True(await executions.IsDeletionTombstonedAsync(RunId));
                Assert.False((await observer.RecordAsync(observation)).Recorded); Assert.Equal(0, await retention.PurgeThroughAsync(RunId, long.MaxValue));
                await AssertDeletedOwner(database);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        async ValueTask Delete()
        {
            if (scenario.Contains("terminal", StringComparison.Ordinal))
            {
                var candidate = await deletions.ReadAsync(RunId); Assert.NotNull(candidate);
                Assert.Equal(WorkflowExecutionDeletionResult.Deleted, await deletions.TryDeleteTerminalAsync(candidate));
            }
            else Assert.True(await executions.DeleteAsync(RunId));
        }
    }

    private static ServiceCollection OwnerServices(Database database, string engine, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(database.Clock);
        services.AddWorkflowRuntime(); services.AddWorkflowRuntimeHistory(new() { Enabled = false });
        services.AddRuntimeEntityFrameworkCore(new()
        {
            Provider = engine, ConnectionString = database.Connection, Pooling = false,
            RecoveryContinuationSigningKey = "history-native-public-owner-recovery-signing-key",
            HierarchyCursorSigningKey = "history-native-public-owner-hierarchy-signing-key"
        });
        switch (engine)
        {
            case "PostgreSql": services.ConfigureDbContext<RuntimePostgreSqlDbContext>((_, options) => options.AddInterceptors(interceptors)); break;
            case "SqlServer": services.ConfigureDbContext<RuntimeSqlServerDbContext>((_, options) => options.AddInterceptors(interceptors)); break;
            case "MySql": services.ConfigureDbContext<RuntimeMySqlDbContext>((_, options) => options.AddInterceptors(interceptors)); break;
            default: throw new ArgumentOutOfRangeException(nameof(engine));
        }
        return services;
    }

    private static async Task AssertOwnerReadback(Database database, long last, long purged)
    {
        await using var context = database.Open();
        var head = new EfRuntimeHistoryProjectionMapping(RuntimeHistoryEfModule.Chain)
            .Read(await context.Set<RuntimeHistoryHeadEntity>().SingleAsync(), Scope, RunId);
        Assert.Equal(last, head.LastSequence); Assert.Equal(purged, head.PurgedThroughSequence);
        Assert.Equal("original", head.LastPhysicalCommitId);
        var rows = await context.Set<RuntimeHistoryEventEntity>().OrderBy(row => row.Sequence).ToArrayAsync();
        Assert.Equal(Enumerable.Range(1, (int)last).Select(value => (long)value), rows.Select(row => row.Sequence));
        Assert.All(rows, row => Assert.Equal(row.Sequence <= purged, row.IsPurged));
        Assert.All(rows.Where(row => row.IsPurged), row => Assert.Empty(row.ContentJson));
        Assert.Single(await context.Bookmarks.ToArrayAsync()); Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Single(await context.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
        Assert.Equal(last, (await context.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null)).CoveredThroughSequence);
    }

    private static async Task AssertDeletedOwner(Database database)
    {
        await using var context = database.Open();
        Assert.Empty(await context.Set<RuntimeHistoryHeadEntity>().ToArrayAsync());
        Assert.Empty(await context.Set<RuntimeHistoryEventEntity>().ToArrayAsync());
        Assert.Empty(await context.Set<RuntimeHistoryActivityEntity>().ToArrayAsync());
        Assert.Empty(await context.Set<RuntimeHistoryBookmarkEntity>().ToArrayAsync());
        Assert.Empty(await context.Set<RuntimeHistoryBookmarkCountEntity>().ToArrayAsync());
        Assert.Empty(await context.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
        Assert.Empty(await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().ToArrayAsync());
        Assert.Empty(await context.Set<RuntimeHistoryOccurrenceCounterEntity>().ToArrayAsync());
    }

    private static async Task OwnerFailureTrigger(Database database, string engine, string operation)
    {
        const string table = "elsa_runtime_history_event";
        await using var context = database.Open();
        var sql = engine switch
        {
            "PostgreSql" => $"CREATE FUNCTION history_owner_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'controlled history owner refusal'; END; $$; CREATE TRIGGER history_owner_fail BEFORE {operation} ON {table} FOR EACH ROW EXECUTE FUNCTION history_owner_fail();",
            "SqlServer" => $"CREATE TRIGGER history_owner_fail ON {table} INSTEAD OF {operation} AS BEGIN THROW 51000, 'controlled history owner refusal', 1; END;",
            _ => $"CREATE TRIGGER history_owner_fail BEFORE {operation} ON {table} FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'controlled history owner refusal'"
        };
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task DropOwnerFailureTrigger(Database database, string engine)
    {
        await using var context = database.Open();
        await context.Database.ExecuteSqlRawAsync(engine == "PostgreSql"
            ? "DROP TRIGGER history_owner_fail ON elsa_runtime_history_event; DROP FUNCTION history_owner_fail();"
            : "DROP TRIGGER history_owner_fail");
    }
}

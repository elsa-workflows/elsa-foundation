using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

[assembly: EfSchemaFamily("occurrence-provider-controls", "1.0.0")]
[assembly: EfSchemaContent("occurrence-provider-controls", typeof(RuntimeHistoryOccurrenceEntity), "ContentJson")]
[assembly: EfSchemaContent("occurrence-provider-controls", typeof(RuntimeHistoryOccurrenceMembershipEntity), "ContentJson")]
[assembly: EfSchemaContent("occurrence-provider-controls", typeof(RuntimeHistoryOccurrenceCounterEntity), "ContentJson")]

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeHistoryOccurrenceProjectionPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_occurrence_projection_preserves_transaction_identity_replay_and_ordinals() =>
        RuntimeHistoryOccurrenceProjectionProviderContract.Run(fixture, builder => builder.UseNpgsql());
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeHistoryOccurrenceProjectionSqlServerTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_occurrence_projection_preserves_transaction_identity_replay_and_ordinals() =>
        RuntimeHistoryOccurrenceProjectionProviderContract.Run(fixture, builder => builder.UseSqlServer());
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeHistoryOccurrenceProjectionMySqlTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_occurrence_projection_preserves_transaction_identity_replay_and_ordinals() =>
        RuntimeHistoryOccurrenceProjectionProviderContract.Run(fixture, builder => builder.UseMySQL());
}

// Isolated real-provider storage controls, not Runtime migration/native checkpoint qualification.
// Every database has a new owned name; neither an existing fixture database nor another run is dropped.
internal static class RuntimeHistoryOccurrenceProjectionProviderContract
{
    private static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeHistoryOccurrenceProjectionProviderContract).Assembly,
        "occurrence-provider-controls");
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;

    internal static async Task Run(RuntimeBookmarksProviderFixture fixture, Action<DbContextOptionsBuilder<ProjectionContext>> configure)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var database = "occurrence_" + Guid.NewGuid().ToString("N");
        var connection = new DbConnectionStringBuilder { ConnectionString = fixture.ConnectionString };
        // The supplied fixture may expose Initial Catalog instead of Database. Remove that alias so
        // the unique owned database is the sole selected catalog on every supported provider.
        connection.Remove("Initial Catalog"); connection["Database"] = database;
        ProjectionContext Open()
        {
            var builder = new DbContextOptionsBuilder<ProjectionContext>(); configure(builder);
            var context = new ProjectionContext(builder.Options); context.Database.SetConnectionString(connection.ConnectionString); return context;
        }
        await using var owner = Open();
        try
        {
            await owner.Database.EnsureCreatedAsync();
            var scope = "Case|scope" + (char)0xD800; const string run = "run|one";
            var events = new List<RuntimeHistoryEvent>(); long sequence = 0;
            for (var index = 0; index < 200; index++)
            {
                var activity = "activity-" + index;
                events.Add(Event(++sequence, activity, run));
                events.Add(Event(++sequence, activity, run, "ActivitySuspended"));
                events.Add(Event(++sequence, activity, run, "ActivityContinuationStarted"));
            }
            await using (var transaction = await owner.Database.BeginTransactionAsync())
            {
                Assert.Equal(200, await EfRuntimeHistoryOccurrenceProjection.StageAsync(owner, scope, run, Chain, events));
                Assert.Equal(0, await owner.Set<RuntimeHistoryOccurrenceEntity>().CountAsync());
                await owner.SaveChangesAsync(); await transaction.CommitAsync();
            }
            await using (var replay = Open())
            {
                await using var transaction = await replay.Database.BeginTransactionAsync();
                Assert.Equal(0, await EfRuntimeHistoryOccurrenceProjection.StageAsync(replay, scope, run, Chain, events));
                Assert.Empty(replay.ChangeTracker.Entries()); Assert.Equal(0, await replay.SaveChangesAsync()); await transaction.CommitAsync();
            }
            await using (var rollback = Open())
            {
                await using var transaction = await rollback.Database.BeginTransactionAsync();
                await EfRuntimeHistoryOccurrenceProjection.StageAsync(rollback, scope, run, Chain, [Event(sequence + 1, "not-committed", run)]);
                await rollback.SaveChangesAsync(); await transaction.RollbackAsync();
            }
            await using (var reopened = Open())
            {
                var counterId = EfRuntimeHistoryOccurrenceProjection.CounterId(scope, run, null);
                var row = await reopened.Set<RuntimeHistoryOccurrenceCounterEntity>().AsNoTracking().SingleAsync(item => item.Id == counterId);
                var counter = EfRuntimeHistoryOccurrenceProjection.ReadCounter(row, scope, run, Chain);
                Assert.Equal(200, counter.NextOrdinal); Assert.Equal(sequence, counter.CoveredThroughSequence);
                foreach (var position in new long[] { 0, 100, 199, 23 })
                {
                    var member = await reopened.Set<RuntimeHistoryOccurrenceMembershipEntity>().AsNoTracking()
                        .SingleAsync(item => item.ScopeOwnerId == counterId && item.Ordinal == position);
                    var value = EfRuntimeHistoryOccurrenceProjection.ReadMembership(member, scope, run, Chain);
                    Assert.Equal("activity-" + position, EfRelationalIdentity.Decode(value.Activity));
                }
                // Distinct raw identities remain distinct under provider text collations and UTF encodings.
                await using var transaction = await reopened.Database.BeginTransactionAsync();
                await EfRuntimeHistoryOccurrenceProjection.StageAsync(reopened, "case|scope\uFFFD", run, Chain, [Event(1, "activity-0", run)]);
                await reopened.SaveChangesAsync(); await transaction.CommitAsync();
            }
            await using (var stale = Open())
            {
                var counterId = EfRuntimeHistoryOccurrenceProjection.CounterId(scope, run, null);
                _ = await stale.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(item => item.Id == counterId);
                await using (var winner = Open())
                {
                    await using var transaction = await winner.Database.BeginTransactionAsync();
                    await EfRuntimeHistoryOccurrenceProjection.StageAsync(winner, scope, run, Chain, [Event(sequence + 1, "winner", run)]);
                    await winner.SaveChangesAsync(); await transaction.CommitAsync();
                }
                await using var staleTransaction = await stale.Database.BeginTransactionAsync();
                await EfRuntimeHistoryOccurrenceProjection.StageAsync(stale, scope, run, Chain, [Event(sequence + 1, "activity-0", run, "ActivityContinuationStarted")]);
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync()); await staleTransaction.RollbackAsync();
            }
            await using (var deletion = Open())
            {
                await using var transaction = await deletion.Database.BeginTransactionAsync();
                await EfRuntimeHistoryOccurrenceProjection.StageDeleteAsync(deletion, scope, run);
                await deletion.SaveChangesAsync(); await transaction.CommitAsync();
            }
            await using var after = Open();
            var retained = Assert.Single(await after.Set<RuntimeHistoryOccurrenceEntity>().AsNoTracking().ToArrayAsync());
            Assert.Equal("case|scope\uFFFD", EfRelationalIdentity.Decode(retained.ScopeKey));
        }
        finally
        {
            // Only this method's randomly named, never reused database is retired.
            await using var cleanup = Open(); await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static RuntimeHistoryEvent Event(long sequence, string activity, string run, string name = "ActivityStarted") =>
        new("event-" + sequence, run, sequence, RuntimeHistoryEventCategory.ActivityLifecycle, name, At, activity)
        { Authority = RuntimeHistoryAuthority.CommittedTransition, RecordedAt = At, PhysicalCommitId = "projection-control", ActivityAncestryKnown = true };

    internal sealed class ProjectionContext(DbContextOptions<ProjectionContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        { RuntimeHistoryOccurrenceEntityConfiguration.Configure(model); model.IndexSchemaVersionStamps(); }
    }
}

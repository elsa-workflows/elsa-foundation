using System.Data;
using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;
using Reader = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryOccurrenceReader;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeHistoryOccurrenceReaderPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_frozen_occurrence_reads_use_exact_scoped_ordinals_and_caller_transactions() =>
        RuntimeHistoryOccurrenceReaderProviderContract.Run(fixture, options => options.UseNpgsql());
}
[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeHistoryOccurrenceReaderSqlServerTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_frozen_occurrence_reads_use_exact_scoped_ordinals_and_caller_transactions() =>
        RuntimeHistoryOccurrenceReaderProviderContract.Run(fixture, options => options.UseSqlServer());
}
[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeHistoryOccurrenceReaderMySqlTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_frozen_occurrence_reads_use_exact_scoped_ordinals_and_caller_transactions() =>
        RuntimeHistoryOccurrenceReaderProviderContract.Run(fixture, options => options.UseMySQL());
}

// Actual provider projection reads. No generated migrations, native execution/retention, process
// restart, access adapter, IAM mutation or composed host is supplied by this isolated context.
internal static class RuntimeHistoryOccurrenceReaderProviderContract
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("reader-providers", 1, true, ["ActivityStarted", "ActivitySuspended", "ActivityContinuationStarted"]);
    private static readonly EfSchemaChain Chain = EfSchemaChain.For(new EfSchemaFamilyDescriptor("OccurrenceReaderProviderControls", "Workflows.Runtime", "1.0.0",
        typeof(RuntimeHistoryOccurrenceReaderProviderContract).Assembly)
    { ContentColumns = new[] { typeof(RuntimeHistoryHeadEntity), typeof(RuntimeHistoryEventEntity), typeof(RuntimeHistoryBookmarkEntity),
        typeof(RuntimeHistoryBookmarkCountEntity), typeof(RuntimeHistoryActivityEntity), typeof(RuntimeHistoryOccurrenceEntity),
        typeof(RuntimeHistoryOccurrenceMembershipEntity), typeof(RuntimeHistoryOccurrenceCounterEntity) }.Select(type => new EfSchemaColumn(type, "ContentJson")).ToArray() });
    private static readonly IRuntimeRecoveryContinuationCodec Codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        { SigningKey = "occurrence-reader-provider-key-shared-0123456789", AllowEphemeralDevelopmentKey = false }));

    internal static async Task Run(RuntimeBookmarksProviderFixture fixture, Action<DbContextOptionsBuilder<HistoryContext>> configure)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var database = "occurrence_reader_" + Guid.NewGuid().ToString("N");
        var connection = new DbConnectionStringBuilder { ConnectionString = fixture.ConnectionString };
        connection.Remove("Initial Catalog"); connection["Database"] = database;
        HistoryContext Open()
        { var options = new DbContextOptionsBuilder<HistoryContext>(); configure(options); var context = new HistoryContext(options.Options);
          context.Database.SetConnectionString(connection.ConnectionString); return context; }
        var scope = "Case|scope" + (char)0xD800; const string run = "run|one"; var mapping = new Mapping(Chain);
        async Task Append(IReadOnlyList<RuntimeHistoryEvent> events)
        {
            await using var context = Open(); await using var transaction = await context.Database.BeginTransactionAsync();
            var id = Mapping.Id("head", scope, run); var row = await context.Set<RuntimeHistoryHeadEntity>().SingleOrDefaultAsync(item => item.Id == id);
            var head = row is null ? new RuntimeHistoryHead(run, 1, events[^1].Sequence, "projection-seed", At, Policy, true, false, 0)
                : mapping.Read(row, scope, run) with { LastSequence = events[^1].Sequence };
            var next = mapping.Row(scope, head); next.Revision = row is null ? 1 : row.Revision + 1;
            if (row is null) context.Add(next); else context.Entry(row).CurrentValues.SetValues(next);
            foreach (var item in events) context.Add(mapping.Row(scope, item, new string('b', 64)));
            await EfRuntimeHistoryOccurrenceProjection.StageAsync(context, scope, run, Chain, events);
            await context.SaveChangesAsync(); await transaction.CommitAsync();
        }
        async Task<RuntimeHistoryOccurrencePage> Read(RuntimeHistoryOccurrenceQuery query)
        { await using var context = Open(); var page = await Reader.ReadAsync(context, scope, Chain, Codec, query);
          Assert.Null(context.Database.CurrentTransaction); Assert.Empty(context.ChangeTracker.Entries()); return page; }
        try
        {
            await using (var setup = Open()) await setup.Database.EnsureCreatedAsync();
            var events = new List<RuntimeHistoryEvent>(); long sequence = 0;
            for (var index = 0; index < 200; index++)
            {
                var activity = index == 0 ? "root" : "a-" + index; var parent = index == 0 ? null : "root";
                events.Add(Event(++sequence, activity, run, parent));
                events.Add(Event(++sequence, activity, run, parent, "ActivitySuspended"));
                events.Add(Event(++sequence, activity, run, parent, "ActivityContinuationStarted"));
            }
            await Append(events); var admission = await Read(new(run, 5, accessBinding: "operator"));
            Assert.Equal(200, admission.TotalCount);
            foreach (var position in new long[] { 0, 100, 199, 23 })
            {
                var page = await Read(new(run, 5, admission.SequenceToken, position, accessBinding: "operator"));
                Assert.Equal(200, page.TotalCount); Assert.Equal(position, page.SelectedPosition); Assert.Equal(5, page.Items.Count);
                var selected = Assert.Single(page.Items, item => item.Position == position);
                Assert.Equal(position == 0 ? "root" : "a-" + position, selected.Observation.ActivityExecutionId);
                Assert.Equal(position == 0 ? null : position == 1 ? "root" : "a-" + (position - 1), selected.PreviousActivityExecutionId);
                Assert.Equal(position == 199 ? null : "a-" + (position + 1), selected.NextActivityExecutionId);
            }
            Assert.Equal(23, (await Read(new(run, 5, admission.SequenceToken, anchorActivityExecutionId: "a-23", accessBinding: "operator"))).SelectedPosition);
            await Append([Event(++sequence, "new-child", run, "a-100")]);
            var frozen = await Read(new(run, 5, admission.SequenceToken, 199, accessBinding: "operator"));
            Assert.Equal(200, frozen.TotalCount); Assert.True(frozen.HasNewerHistory);
            var descendants = await Read(new(run, 5, activityExecutionId: "a-100", includeDescendants: true));
            Assert.Equal(new[] { "a-100", "new-child" }, descendants.Items.Select(item => item.Observation.ActivityExecutionId));
            // Model the owner's acknowledged prefix-retirement projections without claiming its operation.
            await using (var retire = Open())
            {
                await using var transaction = await retire.Database.BeginTransactionAsync();
                var row = await retire.Set<RuntimeHistoryHeadEntity>().SingleAsync(); var head = mapping.Read(row, scope, run);
                foreach (var item in await retire.Set<RuntimeHistoryEventEntity>().Where(item => item.Sequence <= 3).ToArrayAsync())
                { item.IsPurged = true; item.ContentJson = ""; item.Revision++; }
                var next = mapping.Row(scope, head with { FirstSequence = 4, PurgedThroughSequence = 3 }); next.Revision = row.Revision + 1;
                retire.Entry(row).CurrentValues.SetValues(next); await retire.SaveChangesAsync(); await transaction.CommitAsync();
            }
            Assert.Equal(RuntimeHistoryOccurrenceAvailability.Invalidated, (await Read(new(run, 5, admission.SequenceToken, accessBinding: "operator"))).Availability);
            await Append([Event(++sequence, "root", run, null, "ActivityContinuationStarted")]);
            var refresh = await Read(new(run, 5));
            Assert.Equal(200, refresh.TotalCount); Assert.Equal(RuntimeHistoryOccurrenceAvailability.Partial, refresh.Availability);
            Assert.Equal("a-1", refresh.Items[0].Observation.ActivityExecutionId); Assert.Equal(0, refresh.Items[0].Position);
            await using (var context = Open())
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                Assert.Equal(200, (await Reader.ReadAsync(context, scope, Chain, Codec, new(run, 5))).TotalCount);
                Assert.Same(transaction, context.Database.CurrentTransaction); await transaction.RollbackAsync();
            }
            await using (var weak = Open())
            {
                await using var transaction = await weak.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                await Assert.ThrowsAsync<InvalidOperationException>(() => Reader.ReadAsync(weak, scope, Chain, Codec, new(run)).AsTask());
                Assert.Same(transaction, weak.Database.CurrentTransaction); await transaction.RollbackAsync();
            }
            await Assert.ThrowsAsync<ArgumentException>(() => Read(new(run, 5, refresh.SequenceToken, accessBinding: "changed")));
        }
        finally
        { await using var cleanup = Open(); await cleanup.Database.EnsureDeletedAsync(); }
    }
    private static RuntimeHistoryEvent Event(long sequence, string activity, string run, string? parent, string name = "ActivityStarted") =>
        new("event-" + sequence, run, sequence, RuntimeHistoryEventCategory.ActivityLifecycle, name, At, activity)
        { Authority = RuntimeHistoryAuthority.CommittedTransition, RecordedAt = At, PhysicalCommitId = "projection-seed",
            ParentActivityExecutionId = parent, ActivityAncestryKnown = true };
    internal sealed class HistoryContext(DbContextOptions<HistoryContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder model)
        { RuntimeHistoryEntityConfiguration.Configure(model); RuntimeHistoryOccurrenceEntityConfiguration.Configure(model); }
    }
}

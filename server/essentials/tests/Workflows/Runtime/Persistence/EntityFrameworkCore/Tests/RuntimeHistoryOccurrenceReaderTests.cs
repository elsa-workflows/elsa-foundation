using System.Data;
using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;
using Reader = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryOccurrenceReader;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// Committed projection controls. These seeds are not a native execution, owner retention operation,
// generated migration, process restart, IAM mutation or composed-host acceptance witness.
public sealed partial class RuntimeHistoryOccurrenceReaderTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("reader-controls", 1, true,
        ["ActivityStarted", "ActivityCompleted", "ActivitySuspended", "ActivityContinuationStarted", "ActivityRetryScheduled"]);
    private static readonly EfSchemaChain Chain = EfSchemaChain.For(new EfSchemaFamilyDescriptor("OccurrenceReaderControls", "Workflows.Runtime", "1.0.0",
        typeof(RuntimeHistoryOccurrenceReaderTests).Assembly)
    {
        ContentColumns = new[] { typeof(RuntimeHistoryHeadEntity), typeof(RuntimeHistoryEventEntity), typeof(RuntimeHistoryBookmarkEntity),
            typeof(RuntimeHistoryActivityEntity), typeof(RuntimeHistoryBookmarkCountEntity), typeof(RuntimeHistoryOccurrenceEntity),
            typeof(RuntimeHistoryOccurrenceMembershipEntity), typeof(RuntimeHistoryOccurrenceCounterEntity) }.Select(type => new EfSchemaColumn(type, "ContentJson")).ToArray()
    });
    private static readonly IRuntimeRecoveryContinuationCodec Codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        { SigningKey = "occurrence-reader-controls-shared-key-0123456789", AllowEphemeralDevelopmentKey = false }));

    [Fact]
    public async Task First_completion_resume_retry_and_parallel_identities_follow_committed_positions_without_timestamp_sorting()
    {
        await using var db = await Database.Create();
        await db.Append([Event(1, "root"), Event(2, "loop-1", "ActivityCompleted", "root", knownTime: false),
            Event(3, "loop-1", "ActivityContinuationStarted", "root"), Event(4, "retry-2", "ActivityRetryScheduled", "root"),
            Event(5, "parallel-b", parent: "root"), Event(6, "parallel-a", parent: "root")]);
        var page = await db.Read();
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Available, page.Availability);
        Assert.Equal(5, page.TotalCount); Assert.Equal(new[] { "root", "loop-1", "retry-2", "parallel-b", "parallel-a" }, Ids(page));
        Assert.Equal("event-2", page.Items[1].Observation.HistoryEventId); Assert.False(page.Items[1].Observation.OccurredAtKnown);
        Assert.Equal("ActivityCompleted", page.Items[1].Observation.Name); Assert.Equal(2, page.Items[1].Observation.Sequence);
        Assert.Null(page.Items[0].PreviousActivityExecutionId); Assert.Null(page.Items[^1].NextActivityExecutionId);
        for (var index = 1; index < page.Items.Count; index++)
            Assert.Equal(page.Items[index - 1].Observation.ActivityExecutionId, page.Items[index].PreviousActivityExecutionId);
    }

    [Fact]
    public async Task Distant_ordinal_anchor_and_neighbors_are_bounded_independently_of_preceding_lifecycle_volume()
    {
        await using var db = await Database.Create(); var events = new List<RuntimeHistoryEvent>(); long sequence = 0;
        for (var index = 0; index < 200; index++)
        {
            events.Add(Event(++sequence, "a-" + index));
            for (var wait = 0; wait < 20; wait++) events.Add(Event(++sequence, "a-" + index, "ActivityContinuationStarted"));
        }
        await db.Append(events);
        var admitted = await db.Read(new("run", 5));
        foreach (var position in new long[] { 0, 100, 199, 23 })
        {
            var commands = new Commands(); var page = await db.Read(new("run", 5, admitted.SequenceToken, position), commands);
            Assert.Equal(200, page.TotalCount); Assert.Equal(position, page.SelectedPosition); Assert.InRange(page.Items.Count, 1, 5);
            var selected = Assert.Single(page.Items, item => item.Position == position);
            Assert.Equal("a-" + position, selected.Observation.ActivityExecutionId);
            Assert.Equal(position == 0 ? null : "a-" + (position - 1), selected.PreviousActivityExecutionId);
            Assert.Equal(position == 199 ? null : "a-" + (position + 1), selected.NextActivityExecutionId);
            Assert.InRange(commands.Selects.Count, 1, 8);
            Assert.DoesNotContain(commands.Selects, sql => sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase) || sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
            var eventSql = Assert.Single(commands.Selects, sql => sql.Contains(RuntimeHistoryProjectionSchema.EventTable, StringComparison.Ordinal));
            Assert.Contains(" IN ", eventSql, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, commands.Writes);
        }
        var anchored = await db.Read(new("run", 5, admitted.SequenceToken, anchorActivityExecutionId: "a-23"));
        Assert.Equal(23, anchored.SelectedPosition); Assert.Equal("a-23", Assert.Single(anchored.Items, item => item.Position == 23).Observation.ActivityExecutionId);
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, (await db.Read(new("run", 5, admitted.SequenceToken, long.MaxValue))).Availability);
    }

    [Fact]
    public async Task Appends_and_later_ancestry_enrichment_do_not_change_an_admitted_scope()
    {
        await using var db = await Database.Create();
        await db.Append([Event(1, "root"), Event(2, "left", parent: "root"), Event(3, "right", parent: "root")]);
        var admitted = await db.Read(new("run", activityExecutionId: "left", includeDescendants: true));
        Assert.Equal(new[] { "left" }, Ids(admitted));
        await db.Append([Event(4, "right", "ActivityContinuationStarted", "left"), Event(5, "late-child", parent: "left")]);
        var frozen = await db.Read(new("run", sequenceToken: admitted.SequenceToken, activityExecutionId: "left", includeDescendants: true));
        Assert.Equal(new[] { "left" }, Ids(frozen)); Assert.Equal(1, frozen.TotalCount); Assert.True(frozen.HasNewerHistory);
        Assert.Equal(3, frozen.UpperSequence);
        var refresh = await db.Read(new("run", activityExecutionId: "left", includeDescendants: true));
        Assert.Equal(new[] { "left", "late-child" }, Ids(refresh)); Assert.Equal(2, refresh.TotalCount);
        var outside = await db.Read(new("run", sequenceToken: admitted.SequenceToken, anchorActivityExecutionId: "right",
            activityExecutionId: "left", includeDescendants: true));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, outside.Availability); Assert.Equal("anchor-outside-scope", outside.Reason);
    }

    [Fact]
    public async Task Purge_projection_invalidates_old_bounds_and_refresh_excludes_purged_first_observations_without_reintroducing_continuations()
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "a"), Event(2, "b"), Event(3, "c")]);
        var admitted = await db.Read(); await db.PurgeProjection(1);
        var old = await db.Read(new("run", sequenceToken: admitted.SequenceToken));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Invalidated, old.Availability); Assert.Empty(old.Items); Assert.Null(old.SequenceToken);
        await db.Append([Event(4, "a", "ActivityContinuationStarted"), Event(5, "d")]);
        var refresh = await db.Read();
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.Partial, refresh.Availability); Assert.Equal("retained-prefix-purged", refresh.Reason);
        Assert.Equal(new[] { "b", "c", "d" }, Ids(refresh)); Assert.Equal(new long[] { 0, 1, 2 }, refresh.Items.Select(item => item.Position));
        Assert.Equal(3, refresh.TotalCount); Assert.Equal(1, refresh.RetainedAfterSequence);
        var oldAnchor = await db.Read(new("run", anchorActivityExecutionId: "a"));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, oldAnchor.Availability); Assert.Equal("anchor-outside-range", oldAnchor.Reason);
    }

    [Fact]
    public async Task Exact_scope_has_only_its_own_first_occurrence_while_descendants_retain_their_own_identities()
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "root"), Event(2, "child", parent: "root"), Event(3, "grandchild", parent: "child")]);
        Assert.Equal(new[] { "child" }, Ids(await db.Read(new("run", activityExecutionId: "child"))));
        Assert.Equal(new[] { "child", "grandchild" }, Ids(await db.Read(new("run", activityExecutionId: "child", includeDescendants: true))));
        Assert.Equal("scope-unavailable", (await db.Read(new("run", activityExecutionId: "missing"))).Reason);
        await db.PurgeProjection(2);
        Assert.Empty((await db.Read(new("run", activityExecutionId: "child"))).Items);
        Assert.Equal(new[] { "grandchild" }, Ids(await db.Read(new("run", activityExecutionId: "child", includeDescendants: true))));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Empty_recording_and_all_retired_occurrences_have_truthful_empty_bounds(bool retired)
    {
        await using var db = await Database.Create();
        if (retired) { await db.Append([Event(1, "a")]); await db.PurgeProjection(1); }
        else await db.Append([]);
        var page = await db.Read(); Assert.Equal(0, page.TotalCount); Assert.Null(page.SelectedPosition); Assert.Empty(page.Items); Assert.NotNull(page.SequenceToken);
        var position = await db.Read(new("run", sequenceToken: page.SequenceToken, position: 0));
        Assert.Equal(RuntimeHistoryOccurrenceAvailability.TargetUnavailable, position.Availability);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Encoded_scope_run_activity_identities_preserve_case_delimiters_and_raw_UTF16(int scenario)
    {
        var identity = scenario switch { 0 => "Case", 1 => "a|b", _ => "a" + (char)0xD800 };
        var other = scenario switch { 0 => "case", 1 => "a", _ => "a\uFFFD" };
        await using var db = await Database.Create();
        await db.Append([Event(1, identity, run: identity)], identity, identity);
        await db.Append([Event(1, other, run: other)], other, other);
        Assert.Equal(new[] { identity }, Ids(await db.Read(new(identity), scope: identity)));
        Assert.Equal(new[] { other }, Ids(await db.Read(new(other), scope: other)));
    }

    private static string?[] Ids(RuntimeHistoryOccurrencePage page) => page.Items.Select(item => item.Observation.ActivityExecutionId).ToArray();
    private static RuntimeHistoryEvent Event(long sequence, string activity, string name = "ActivityStarted", string? parent = null,
        bool known = true, bool knownTime = true, string run = "run") =>
        new("event-" + sequence, run, sequence, RuntimeHistoryEventCategory.ActivityLifecycle, name, At.AddSeconds(-sequence), activity)
        { Authority = RuntimeHistoryAuthority.CommittedTransition, RecordedAt = At, PhysicalCommitId = "projection-seed", ParentActivityExecutionId = parent,
            ActivityAncestryKnown = known, OccurredAtKnown = knownTime };
    private sealed class HistoryContext(DbContextOptions<HistoryContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder model)
        { RuntimeHistoryEntityConfiguration.Configure(model); RuntimeHistoryOccurrenceEntityConfiguration.Configure(model); }
    }
    private sealed class Database(SqliteConnection keeper) : IAsyncDisposable
    {
        internal static async Task<Database> Create()
        { var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); var db = new Database(connection);
          await using var context = db.Open(); await context.Database.EnsureCreatedAsync(); return db; }
        internal HistoryContext Open(DbCommandInterceptor? interceptor = null)
        { var options = new DbContextOptionsBuilder<HistoryContext>().UseSqlite(keeper); if (interceptor is not null) options.AddInterceptors(interceptor); return new(options.Options); }
        internal async Task Append(IReadOnlyList<RuntimeHistoryEvent> events, string scope = "scope", string run = "run", bool stage = true)
        {
            await using var context = Open(); await using var transaction = await context.Database.BeginTransactionAsync(); var map = new Mapping(Chain);
            var id = Mapping.Id("head", scope, run); var existing = await context.Set<RuntimeHistoryHeadEntity>().SingleOrDefaultAsync(row => row.Id == id);
            var head = existing is null ? new RuntimeHistoryHead(run, events.Count == 0 ? 0 : events[0].Sequence, 0, "projection-seed", At, Policy, true, false, 0)
                : map.Read(existing, scope, run);
            head = head with { LastSequence = events.Count == 0 ? head.LastSequence : events[^1].Sequence };
            var next = map.Row(scope, head); next.Revision = existing is null ? 1 : existing.Revision + 1;
            if (existing is null) context.Add(next); else context.Entry(existing).CurrentValues.SetValues(next);
            foreach (var item in events) context.Add(map.Row(scope, item, new string('a', 64)));
            if (stage) await EfRuntimeHistoryOccurrenceProjection.StageAsync(context, scope, run, Chain, events);
            await context.SaveChangesAsync(); await transaction.CommitAsync();
        }
        internal async Task<RuntimeHistoryOccurrencePage> Read(RuntimeHistoryOccurrenceQuery? query = null, DbCommandInterceptor? interceptor = null, string scope = "scope")
        { await using var context = Open(interceptor); var page = await Reader.ReadAsync(context, scope, Chain, Codec, query ?? new("run"));
          Assert.Null(context.Database.CurrentTransaction); Assert.Empty(context.ChangeTracker.Entries()); return page; }
        internal async Task ChangeHead(Func<RuntimeHistoryHead, RuntimeHistoryHead> change)
        { await using var context = Open(); var row = await context.Set<RuntimeHistoryHeadEntity>().SingleAsync(); var map = new Mapping(Chain);
          var next = map.Row("scope", change(map.Read(row, "scope", "run"))); next.Revision = row.Revision + 1; context.Entry(row).CurrentValues.SetValues(next); await context.SaveChangesAsync(); }
        internal async Task PurgeProjection(long through)
        {
            await using var context = Open(); await using var transaction = await context.Database.BeginTransactionAsync(); var map = new Mapping(Chain);
            var row = await context.Set<RuntimeHistoryHeadEntity>().SingleAsync(); var head = map.Read(row, "scope", "run");
            foreach (var item in await context.Set<RuntimeHistoryEventEntity>().Where(item => item.Sequence <= through).ToArrayAsync())
            { item.IsPurged = true; item.ContentJson = ""; item.Revision++; }
            var next = map.Row("scope", head with { FirstSequence = through + 1, PurgedThroughSequence = through }); next.Revision = row.Revision + 1;
            context.Entry(row).CurrentValues.SetValues(next); await context.SaveChangesAsync(); await transaction.CommitAsync();
        }
        internal void BackupTo(SqliteConnection target) => keeper.BackupDatabase(target);
        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }
    private sealed class Commands : DbCommandInterceptor
    {
        internal List<string> Selects { get; } = []; internal int Writes;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Selects.Add(command.CommandText); else Writes++; return ValueTask.FromResult(result); }
    }
}

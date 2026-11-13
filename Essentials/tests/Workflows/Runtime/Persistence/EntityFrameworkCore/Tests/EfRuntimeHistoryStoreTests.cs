using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Elsa.Workflows.Runtime.Core.Contracts;
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

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// Actual committed private projection staging/reads. These tests do not enable the production
// writer/model or substitute for generated migrations, process restart, IAM or composed proof.
public sealed partial class EfRuntimeHistoryStoreTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("selected-reader-tests", 1, true,
        ["ActivityStarted", "ActivityCompleted", "ActivitySuspended", "ActivityContinuationStarted", "BookmarkCreated", "BookmarkConsumed", "BookmarkRemoved", "FutureLifecycle"]);
    private static EfSchemaFamilyDescriptor Declaration() => new("SelectedReaderControls", "Workflows.Runtime", "1.0.0", typeof(EfRuntimeHistoryStoreTests).Assembly)
    { ContentColumns = new[] { typeof(RuntimeHistoryHeadEntity), typeof(RuntimeHistoryEventEntity), typeof(RuntimeHistoryBookmarkEntity),
        typeof(RuntimeHistoryBookmarkCountEntity), typeof(RuntimeHistoryActivityEntity), typeof(RuntimeHistoryOccurrenceEntity),
        typeof(RuntimeHistoryOccurrenceMembershipEntity), typeof(RuntimeHistoryOccurrenceCounterEntity) }.Select(type => new EfSchemaColumn(type, "ContentJson")).ToArray() };
    private static readonly EfSchemaChain Chain = EfSchemaChain.For(Declaration());
    private static readonly IRuntimeRecoveryContinuationCodec Codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        { SigningKey = "selected-history-reader-controls-0123456789-shared", AllowEphemeralDevelopmentKey = false }));

    [Fact]
    public async Task Ordinary_pages_keep_fixed_committed_bounds_and_catch_up_only_from_the_confirmed_upper()
    {
        await using var db = await Database.Create(); await db.Append(Enumerable.Range(1, 7).Select(index => new Fact("e-" + index, "a-" + index)).ToArray());
        await using var context = db.Open(); var store = db.Store(context);
        var first = await store.ReadPageAsync(new("run", 2)); Assert.Equal(7, first.UpperSequence);
        Assert.Equal(new[] { "e-1", "e-2" }, first.Items.Select(item => item.HistoryEventId)); Assert.Equal(2, first.ScannedCount);
        await db.Append(new Fact("e-8", "a-8"));
        var ids = first.Items.Select(item => item.HistoryEventId).ToList(); var page = first;
        while (page.NextContinuationToken is not null)
        { page = await store.ReadPageAsync(new("run", 2, page.NextContinuationToken)); Assert.Equal(7, page.UpperSequence); ids.AddRange(page.Items.Select(item => item.HistoryEventId)); }
        Assert.Equal(Enumerable.Range(1, 7).Select(index => "e-" + index), ids);
        var catchUp = await store.ReadPageAsync(new("run", 2, first.CatchUpToken));
        Assert.Equal(new[] { "e-8" }, catchUp.Items.Select(item => item.HistoryEventId)); Assert.Equal(8, catchUp.UpperSequence);
        Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Empty_search_windows_advance_without_searching_metadata_or_preloading_the_run()
    {
        await using var db = await Database.Create();
        await db.Append(Enumerable.Range(1, 12).Select(index => new Fact(index == 12 ? "needle-12" : "e-" + index, "a-" + index,
            Metadata: new Dictionary<string, string> { ["history.reasonCode"] = "private-marker" })).ToArray());
        var commands = new Commands(); await using var context = db.Open(commands); var store = db.Store(context);
        var first = await store.ReadPageAsync(new("run", 1, search: "needle"));
        Assert.Empty(first.Items); Assert.Equal(4, first.ScannedCount); Assert.Equal(4, first.ScannedThroughSequence); Assert.NotNull(first.NextContinuationToken);
        var second = await store.ReadPageAsync(new("run", 1, first.NextContinuationToken, search: "needle"));
        Assert.Empty(second.Items); Assert.Equal(8, second.ScannedThroughSequence);
        var final = await store.ReadPageAsync(new("run", 1, second.NextContinuationToken, search: "needle"));
        Assert.Equal("needle-12", Assert.Single(final.Items).HistoryEventId); Assert.Null(final.NextContinuationToken);
        var metadata = await store.ReadPageAsync(new("run", 20, search: "private-marker")); Assert.Empty(metadata.Items);
        Assert.DoesNotContain(commands.Texts, sql => sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase) || sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, commands.Writes);
    }

    [Theory]
    [InlineData("name")] [InlineData("category")] [InlineData("activity")] [InlineData("bookmark")]
    public async Task Maintained_filter_indexes_and_decoded_predicates_select_exact_opaque_identities(string filter)
    {
        await using var db = await Database.Create();
        await db.Append(new Fact("start", "Case"), new("end", "case", "ActivityCompleted"),
            new("bookmark-event", "Case", "BookmarkCreated", Mutation: Mutation("b|1", "Case")));
        await using var context = db.Open(); var store = db.Store(context);
        var query = filter switch
        {
            "name" => new RuntimeHistoryQuery("run", name: "ActivityCompleted"),
            "category" => new("run", category: RuntimeHistoryEventCategory.BookmarkLifecycle),
            "activity" => new("run", activityExecutionId: "case"),
            _ => new("run", bookmarkId: "b|1")
        };
        var page = await store.ReadPageAsync(query); Assert.Single(page.Items);
        Assert.Equal(filter is "category" or "bookmark" ? "bookmark-event" : "end", page.Items[0].HistoryEventId);
        if (filter == "name") Assert.Empty((await store.ReadPageAsync(new("run", name: "activitycompleted"))).Items);
    }

    [Fact]
    public async Task Descendants_use_bounded_known_parent_proof_and_keep_unknown_links_incomplete()
    {
        await using var db = await Database.Create();
        await db.Append(new Fact("root", "root"), new("child", "child", Parent: "root"), new("grand", "grand", Parent: "child"), new("unknown", "unknown", Known: false));
        await using var context = db.Open(); var store = db.Store(context);
        var exact = await store.ReadPageAsync(new("run", activityExecutionId: "child")); Assert.Equal("child", Assert.Single(exact.Items).ActivityExecutionId);
        var page = await store.ReadPageAsync(new("run", activityExecutionId: "root", includeDescendants: true));
        Assert.Equal(new[] { "root", "child", "grand" }, page.Items.Select(item => item.ActivityExecutionId));
        Assert.False(page.ScopeComplete); Assert.NotNull(page.ScopeLimitation);
    }

    [Fact]
    public async Task Occurrence_adapter_delegates_the_same_scope_and_freezes_one_entry_per_execution()
    {
        await using var db = await Database.Create(); await db.Append(new Fact("a", "a"), new("resume", "a", "ActivityContinuationStarted"), new("b", "b"));
        await using var context = db.Open(); var store = db.Store(context); var first = await store.ReadOccurrencesAsync(new("run", 1));
        Assert.Equal(2, first.TotalCount); Assert.Equal("a", Assert.Single(first.Items).Observation.ActivityExecutionId);
        var second = await store.ReadOccurrencesAsync(new("run", 1, first.SequenceToken, 1)); Assert.Equal("b", Assert.Single(second.Items).Observation.ActivityExecutionId);
        Assert.True(store.Uses(context, db.Access, Chain)); Assert.False(store.Uses(context, new Access(), Chain));
        Assert.False(store.Uses(context, db.Access, EfSchemaChain.For(Declaration())));
        await using var other = db.Open(); Assert.False(store.Uses(other, db.Access, Chain));
    }

    private sealed record Fact(string Id, string Activity, string Name = "ActivityStarted", string? Parent = null, bool Known = true,
        RuntimeHistoryBookmarkMutation? Mutation = null, IReadOnlyDictionary<string, string>? Metadata = null, bool KnownTime = true);
    private static RuntimeHistoryBookmarkMutation Mutation(string id, string activity, RuntimeHistoryBookmarkStatus status = RuntimeHistoryBookmarkStatus.Active) =>
        new(new(new(id, "run", activity, "resume"), "signal", "hash", At, At.AddHours(1)), status);
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("scope")); }
    private sealed class HistoryContext(DbContextOptions<HistoryContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder model)
        { RuntimeHistoryEntityConfiguration.Configure(model); RuntimeHistoryOccurrenceEntityConfiguration.Configure(model); }
    }
    private sealed class Database(SqliteConnection keeper) : IAsyncDisposable
    {
        internal Access Access { get; } = new(); private int batches;
        internal static async Task<Database> Create()
        {
            var keeper = new SqliteConnection("Data Source=:memory:"); await keeper.OpenAsync(); var db = new Database(keeper);
            await using var context = db.Open(); await context.Database.EnsureCreatedAsync();
            var workflow = new WorkflowExecutionState("run", new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
                null, At, At, At, null, null, null, "scope", new Dictionary<string, string>()) { HistoryCapturePolicy = Policy, HistoryCaptureEnabledAtAdmission = true };
            context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(workflow, "scope", EfWorkflowExecutionStateStore.CreateId("scope", "run"), 1));
            await context.SaveChangesAsync(); return db;
        }
        internal HistoryContext Open(DbCommandInterceptor? interceptor = null)
        { var options = new DbContextOptionsBuilder<HistoryContext>().UseSqlite(keeper); if (interceptor is not null) options.AddInterceptors(interceptor); return new(options.Options); }
        internal EfRuntimeHistoryStore Store(HistoryContext context, Access? access = null) => new(context, access ?? Access, Codec, Chain);
        internal async Task Append(params Fact[] facts)
        {
            var logical = "logical-" + ++batches;
            var candidates = facts.Select((fact, index) => new RuntimeHistoryCandidate(new(fact.Id, "run", 0,
                fact.Mutation is null ? RuntimeHistoryEventCategory.ActivityLifecycle : RuntimeHistoryEventCategory.BookmarkLifecycle,
                fact.Name, At.AddMinutes(-batches), fact.Activity, checkpointId: logical, metadata: fact.Metadata)
            { Authority = RuntimeHistoryAuthority.CommittedTransition, ParentActivityExecutionId = fact.Parent, ActivityAncestryKnown = fact.Known,
                BookmarkId = fact.Mutation?.Bookmark.Identity.BookmarkId, OccurredAtKnown = fact.KnownTime }, index, fact.Mutation)).ToArray();
            var batch = new RuntimeHistoryBatch(Policy, logical, At, batches == 1, candidates);
            var commit = new RuntimeCheckpointCommit("physical-" + batches, new(logical, "control", "run", At, [], new Dictionary<string, string>()),
                new(null, null, [], [], [], [], []), [], new Dictionary<string, string>()) { HistoryBatches = [batch] };
            await using var context = Open(); await using var transaction = await context.Database.BeginTransactionAsync();
            await EfRuntimeHistoryCheckpoint.StageAsync(context, new Access(), commit, "scope", At.AddMinutes(batches), Chain, CancellationToken.None);
            await context.SaveChangesAsync(); await transaction.CommitAsync();
        }
        internal async Task ChangeHead(Func<RuntimeHistoryHead, RuntimeHistoryHead> change)
        {
            await using var context = Open(); var row = await context.Set<RuntimeHistoryHeadEntity>().SingleAsync(); var mapping = new Mapping(Chain);
            var next = mapping.Row("scope", change(mapping.Read(row, "scope", "run"))); next.Revision = row.Revision + 1; context.Entry(row).CurrentValues.SetValues(next); await context.SaveChangesAsync();
        }
        internal async Task Purge(long through)
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
        internal List<string> Texts { get; } = []; internal int Writes;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { Texts.Add(command.CommandText); if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Writes++; return ValueTask.FromResult(result); }
    }
}

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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class EfRuntimeHistoryStorePostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_selected_history_ports_preserve_native_ownership_and_scoped_pages() =>
        EfRuntimeHistoryStoreProviderContract.Run(fixture, options => options.UseNpgsql());
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class EfRuntimeHistoryStoreSqlServerTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_selected_history_ports_preserve_native_ownership_and_scoped_pages() =>
        EfRuntimeHistoryStoreProviderContract.Run(fixture, options => options.UseSqlServer());
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class EfRuntimeHistoryStoreMySqlTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableFact]
    public Task MySql_selected_history_ports_preserve_native_ownership_and_scoped_pages() =>
        EfRuntimeHistoryStoreProviderContract.Run(fixture, options => options.UseMySQL());
}

// Actual private stager/selected adapter against isolated native databases. EnsureCreated here
// does not qualify production registration, generated upgrades, IAM or composed-host behavior.
internal static class EfRuntimeHistoryStoreProviderContract
{
    private const string Scope = "Case|scope";
    private const string RunId = "run|one";
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("selected-provider", 1, true,
        ["ActivityStarted", "ActivityCompleted", "BookmarkCreated", "BookmarkConsumed"]);
    private static readonly EfSchemaChain Chain = EfSchemaChain.For(new EfSchemaFamilyDescriptor("SelectedHistoryProviderControls", "Workflows.Runtime", "1.0.0",
        typeof(EfRuntimeHistoryStoreProviderContract).Assembly)
    { ContentColumns = new[] { typeof(RuntimeHistoryHeadEntity), typeof(RuntimeHistoryEventEntity), typeof(RuntimeHistoryBookmarkEntity),
        typeof(RuntimeHistoryBookmarkCountEntity), typeof(RuntimeHistoryActivityEntity), typeof(RuntimeHistoryOccurrenceEntity),
        typeof(RuntimeHistoryOccurrenceMembershipEntity), typeof(RuntimeHistoryOccurrenceCounterEntity) }.Select(type => new EfSchemaColumn(type, "ContentJson")).ToArray() });
    private static readonly IRuntimeRecoveryContinuationCodec Codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
        { SigningKey = "selected-history-provider-key-shared-0123456789", AllowEphemeralDevelopmentKey = false }));

    internal static async Task Run(RuntimeBookmarksProviderFixture fixture, Action<DbContextOptionsBuilder<HistoryContext>> configure)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var connection = new DbConnectionStringBuilder { ConnectionString = fixture.ConnectionString };
        connection.Remove("Initial Catalog"); connection["Database"] = "selected_history_" + Guid.NewGuid().ToString("N");
        HistoryContext Open(DbCommandInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<HistoryContext>(); configure(options);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var context = new HistoryContext(options.Options); context.Database.SetConnectionString(connection.ConnectionString); return context;
        }
        var access = new Access();
        EfRuntimeHistoryStore Store(HistoryContext context) => new(context, access, Codec, Chain);
        var batches = 0;
        async Task Append(params (string Id, string Activity, string Name, RuntimeHistoryBookmarkMutation? Mutation)[] facts)
        {
            var logical = "logical-" + ++batches;
            var candidates = facts.Select((fact, index) => new RuntimeHistoryCandidate(new(fact.Id, RunId, 0,
                fact.Mutation is null ? RuntimeHistoryEventCategory.ActivityLifecycle : RuntimeHistoryEventCategory.BookmarkLifecycle,
                fact.Name, At, fact.Activity, checkpointId: logical)
            { Authority = RuntimeHistoryAuthority.CommittedTransition, ParentActivityExecutionId = fact.Activity == "root" ? null : "root",
                ActivityAncestryKnown = true, BookmarkId = fact.Mutation?.Bookmark.Identity.BookmarkId }, index, fact.Mutation)).ToArray();
            var commit = new RuntimeCheckpointCommit("physical-" + batches, new(logical, "control", RunId, At, [], new Dictionary<string, string>()),
                new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
            { HistoryBatches = [new(Policy, logical, At, batches == 1, candidates)] };
            await using var context = Open(); await using var transaction = await context.Database.BeginTransactionAsync();
            await EfRuntimeHistoryCheckpoint.StageAsync(context, access, commit, Scope, At.AddMinutes(batches), Chain, CancellationToken.None);
            await context.SaveChangesAsync(); await transaction.CommitAsync();
        }
        RuntimeHistoryBookmarkMutation Mutation(string id, string activity, RuntimeHistoryBookmarkStatus status = RuntimeHistoryBookmarkStatus.Active) =>
            new(new(new(id, RunId, activity, "resume"), "signal", "hash", At, At.AddHours(1)), status);
        try
        {
            await using (var setup = Open())
            {
                await setup.Database.EnsureCreatedAsync();
                var workflow = new WorkflowExecutionState(RunId, new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
                    null, At, At, At, null, null, null, Scope, new Dictionary<string, string>())
                { HistoryCapturePolicy = Policy, HistoryCaptureEnabledAtAdmission = true };
                setup.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(workflow, Scope, EfWorkflowExecutionStateStore.CreateId(Scope, RunId), 1));
                await setup.SaveChangesAsync();
            }
            await Append(("start", "root", "ActivityStarted", null), ("child", "Case", "ActivityStarted", null),
                ("created-1", "Case", "BookmarkCreated", Mutation("b-1", "Case")), ("created-2", "case", "BookmarkCreated", Mutation("b-2", "case")));
            var commands = new Commands(); await using var context = Open(commands); var store = Store(context);
            Assert.True(store.Uses(context, access, Chain)); Assert.False(store.Uses(context, new Access(), Chain));
            var first = await store.ReadPageAsync(new(RunId, 2, accessBinding: "operator"));
            Assert.Equal(new[] { "start", "child" }, first.Items.Select(item => item.HistoryEventId)); Assert.Equal(4, first.UpperSequence);
            var nativeTable = context.Model.FindEntityType(typeof(WorkflowExecutionStateEntity))!.GetTableName()!;
            Assert.Contains(nativeTable, commands.Texts[0], StringComparison.Ordinal);
            await Append(("complete", "Case", "ActivityCompleted", null), ("consumed", "Case", "BookmarkConsumed", Mutation("b-1", "Case", RuntimeHistoryBookmarkStatus.Consumed)));
            var frozen = await store.ReadPageAsync(new(RunId, 2, first.NextContinuationToken, accessBinding: "operator"));
            Assert.Equal(4, frozen.UpperSequence); Assert.Equal(new[] { "created-1", "created-2" }, frozen.Items.Select(item => item.HistoryEventId));
            Assert.Equal(new[] { "complete", "consumed" }, (await store.ReadPageAsync(new(RunId, 2, first.CatchUpToken, accessBinding: "operator"))).Items.Select(item => item.HistoryEventId));
            var detail = await store.FindEventDetailsAsync(RunId, "start"); Assert.Equal(6, detail.Head!.LastSequence); Assert.Equal("start", detail.Event!.HistoryEventId);
            Assert.Equal("child", (await store.FindEventAsync(RunId, "child"))!.HistoryEventId);
            Assert.Equal(4, (await store.ReadPageAsync(new(RunId, activityExecutionId: "Case"))).Items.Count);
            Assert.Equal(6, (await store.ReadPageAsync(new(RunId, activityExecutionId: "root", includeDescendants: true))).Items.Count);
            var inventory = await store.ListBookmarksAsync(new(RunId, 1)); Assert.Equal(2, inventory.TotalCount); Assert.Equal(1, inventory.ActiveCount);
            Assert.Equal("b-1", Assert.Single(inventory.Items).Bookmark.Identity.BookmarkId);
            var second = await store.ListBookmarksAsync(new(RunId, 1, inventory.NextContinuationToken)); Assert.Equal("b-2", Assert.Single(second.Items).Bookmark.Identity.BookmarkId);
            var bookmark = await store.FindBookmarkDetailsAsync(RunId, "b-1"); Assert.Equal(6, bookmark.Head!.LastSequence); Assert.Equal("consumed", bookmark.Bookmark!.EndedHistoryEventId);
            Assert.Equal(RuntimeHistoryBookmarkStatus.Active, (await store.FindBookmarkAsync(RunId, "b-2"))!.Status);
            var occurrences = await store.ReadOccurrencesAsync(new(RunId, 1)); Assert.Equal(2, occurrences.TotalCount);
            Assert.Equal("Case", Assert.Single((await store.ReadOccurrencesAsync(new(RunId, 1, occurrences.SequenceToken, 1))).Items).Observation.ActivityExecutionId);
            Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadPageAsync(new(RunId, 2, first.NextContinuationToken, accessBinding: "changed")).AsTask());
            access.Current = PersistenceAccessContext.Scoped(new("case|scope"));
            Assert.Null(await store.FindHeadAsync(RunId)); Assert.Empty((await store.ReadOccurrencesAsync(new(RunId))).Items);
            access.Current = PersistenceAccessContext.Scoped(new(Scope));
            await using (var caller = Open())
            {
                await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                Assert.Equal(6, (await Store(caller).FindHeadAsync(RunId))!.LastSequence);
                Assert.Same(transaction, caller.Database.CurrentTransaction); await transaction.RollbackAsync();
            }
            await using (var weak = Open())
            {
                await using var transaction = await weak.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                await Assert.ThrowsAsync<InvalidOperationException>(() => Store(weak).ReadPageAsync(new(RunId)).AsTask());
                Assert.Same(transaction, weak.Database.CurrentTransaction); await transaction.RollbackAsync();
            }
            await using (var remove = Open()) await remove.WorkflowExecutionStates.ExecuteDeleteAsync();
            commands.Texts.Clear(); Assert.Null(await store.FindEventAsync(RunId, "start")); Assert.Single(commands.Texts);
            Assert.Contains(nativeTable, commands.Texts[0], StringComparison.Ordinal);
        }
        finally { await using var cleanup = Open(); await cleanup.Database.EnsureDeletedAsync(); }
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new(Scope)); }
    internal sealed class HistoryContext(DbContextOptions<HistoryContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder model)
        { RuntimeHistoryEntityConfiguration.Configure(model); RuntimeHistoryOccurrenceEntityConfiguration.Configure(model); }
    }
    private sealed class Commands : DbCommandInterceptor
    {
        internal List<string> Texts { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { Texts.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}

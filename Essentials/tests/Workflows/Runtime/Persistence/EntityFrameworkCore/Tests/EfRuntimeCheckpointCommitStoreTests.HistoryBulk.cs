using System.Text.Json;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    private const string BulkEfRun = "workflow-a", BulkEfOwner = "activity-a";
    private static readonly RuntimeHistoryCapturePolicy BulkEfPolicy = new("runtime-history-v3", 3, true,
        ["WorkflowStarted", "ActivityCancelled", "BookmarkCreated", "BookmarkRemoved", "ControlApplied"]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "RunHistory")]
    public async Task History_bulk_actual_scope_cleanup_keeps_4097_bookmarks_in_one_native_commit_and_replays_after_context_restart(bool capture)
    {
        await using var fixture = await BulkEfFixture.CreateAsync(capture);
        await fixture.SeedAsync();
        var before = await fixture.Context.RuntimeCheckpointCommits.CountAsync();
        var physicalBefore = fixture.PhysicalCommits;

        await fixture.CancelAsync();

        Assert.Equal(physicalBefore + 1, fixture.PhysicalCommits);
        Assert.Equal(before + 1, await fixture.Context.RuntimeCheckpointCommits.CountAsync());
        Assert.Empty(await fixture.Context.Bookmarks.ToArrayAsync());
        Assert.Equal(ActivityExecutionStatus.Cancelled, (await fixture.Activities.FindAsync(BulkEfRun, BulkEfOwner))!.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.Executions.FindAsync(BulkEfRun))!.Status);
        var committed = fixture.Probe.Last!;
        if (capture)
        {
            var batch = Assert.Single(committed.HistoryBatches!);
            Assert.Equal(4097, batch.Candidates.Count(candidate => candidate.Event.Name == "BookmarkRemoved"));
            Assert.Single(batch.Candidates, candidate => candidate.Event.Name == "ControlApplied");
            Assert.Equal(Enumerable.Range(0, batch.Candidates.Count), batch.Candidates.Select(candidate => candidate.CheckpointOrdinal));
            Assert.Equal(committed.Checkpoint.CheckpointId, batch.LogicalCheckpointId);
            Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(committed with { HistoryBatches = null }), batch.SourceFingerprint);
            var reader = fixture.Services.GetRequiredService<IRuntimeHistoryStore>();
            var head = await reader.FindHeadAsync(BulkEfRun);
            Assert.Equal(4097, await fixture.Context.RuntimeHistoryBookmarks.CountAsync());
            var inventory = await reader.ListBookmarksAsync(new(BulkEfRun, activityExecutionId: BulkEfOwner));
            Assert.Equal(4097, inventory.TotalCount); Assert.Equal(0, inventory.ActiveCount);
            var last = await reader.FindBookmarkAsync(BulkEfRun, BulkEfBookmark(4096).BookmarkId);
            Assert.Equal(RuntimeHistoryBookmarkStatus.Removed, last!.Status);
            Assert.NotNull(last.CreatedHistoryEventId); Assert.NotNull(last.EndedHistoryEventId);
            Assert.DoesNotContain(batch.Candidates, candidate => candidate.Event.Name == "BookmarkConsumed");
            var removed = await BulkEfEventsAsync(reader, "BookmarkRemoved");
            Assert.Equal(4097, removed.Count);
            Assert.All(removed, item => Assert.Equal(committed.CommitId, item.PhysicalCommitId));
            Assert.All(removed, item => Assert.Equal(committed.Checkpoint.CheckpointId, item.CheckpointId));
            Assert.Equal(4097, removed.Select(item => item.HistoryEventId).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(removed.Select(item => item.Sequence).Order(), removed.Select(item => item.Sequence));
            await using var restarted = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
                .UseSqlite(fixture.ConnectionString).Options);
            var access = new FixedAccessor("tenant-a");
            await new EfRuntimeCheckpointCommitStore(restarted, access).CommitAsync(committed, Decision());
            var reopened = HistoryStore(restarted, access);
            AssertBulkEfHeadEqual(head, await reopened.FindHeadAsync(BulkEfRun));
            Assert.Equal(last, await reopened.FindBookmarkAsync(BulkEfRun, last.Bookmark.Identity.BookmarkId));
            Assert.Equal(before + 1, await restarted.RuntimeCheckpointCommits.CountAsync());
            Assert.NotNull(await reopened.FindEventAsync(BulkEfRun, last.EndedHistoryEventId!));
        }
        else
        {
            Assert.Null(committed.HistoryBatches); Assert.Null(committed.HistorySourceFacts);
            Assert.Empty(await fixture.Context.RuntimeHistoryEvents.ToArrayAsync()); Assert.Equal(0, fixture.OptionalHistoryResolutions);
        }
        await fixture.CancelAsync();
        Assert.Equal(before + 1, await fixture.Context.RuntimeCheckpointCommits.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "RunHistory")]
    public async Task History_bulk_tail_source_or_candidate_refusal_preserves_every_atomic_participant(bool unsafeCandidate)
    {
        await using var fixture = await BulkEfFixture.CreateAsync(true);
        await fixture.SeedAsync();
        var reader = fixture.Services.GetRequiredService<IRuntimeHistoryStore>();
        var head = await reader.FindHeadAsync(BulkEfRun);
        var before = await fixture.Context.RuntimeCheckpointCommits.CountAsync();
        fixture.Probe.BeforeWrite = async commit =>
        {
            if (unsafeCandidate)
            {
                var batch = Assert.Single(commit.HistoryBatches!);
                var candidates = batch.Candidates.ToArray();
                var candidate = candidates[4096]; var e = candidate.Event;
                var metadata = new Dictionary<string, string>(e.Metadata) { ["history.reasonCode"] = new('x', 2049) };
                var invalid = new RuntimeHistoryEvent(e.HistoryEventId, e.WorkflowExecutionId, 0, e.Category, e.Name,
                    e.OccurredAt, e.ActivityExecutionId, e.ExecutableNodeId, e.CheckpointId, e.IncidentId, metadata: metadata)
                { Authority = e.Authority, BookmarkId = e.BookmarkId, Causation = e.Causation };
                candidates[4096] = new(invalid, candidate.CheckpointOrdinal, candidate.BookmarkMutation);
                _ = new RuntimeHistoryBatch(batch.Policy, batch.LogicalCheckpointId, batch.OccurredAt, batch.StartsAtRunStart, candidates);
            }
            else
            {
                // An actual native generation changes after immutable capture, before the shared transaction.
                var bookmark = (await fixture.Bookmarks.FindAsync(BulkEfRun, BulkEfBookmark(4096).BookmarkId))!;
                await fixture.Bookmarks.SaveAsync(new(bookmark.BookmarkId, bookmark.WorkflowExecutionId, bookmark.ActivityExecutionId,
                    bookmark.ExecutableNodeId, bookmark.ResumeTargetId, bookmark.StimulusType, "changed-native-generation",
                    bookmark.Payload, bookmark.Metadata, bookmark.CreatedAt, bookmark.ExpiresAt));
            }
        };

        if (unsafeCandidate)
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CancelAsync());
            Assert.Contains("metadata", failure.Message, StringComparison.OrdinalIgnoreCase);
        }
        else await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => fixture.CancelAsync());

        Assert.Equal(before, await fixture.Context.RuntimeCheckpointCommits.CountAsync());
        AssertBulkEfHeadEqual(head, await reader.FindHeadAsync(BulkEfRun));
        Assert.Equal(4097, await fixture.Context.Bookmarks.CountAsync());
        Assert.Equal(ActivityExecutionStatus.Suspended, (await fixture.Activities.FindAsync(BulkEfRun, BulkEfOwner))!.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.Executions.FindAsync(BulkEfRun))!.Status);
        var inventory = await reader.ListBookmarksAsync(new(BulkEfRun, activityExecutionId: BulkEfOwner));
        Assert.Equal(4097, inventory.TotalCount); Assert.Equal(4097, inventory.ActiveCount);
        Assert.Equal(RuntimeHistoryBookmarkStatus.Active, (await reader.FindBookmarkAsync(BulkEfRun, BulkEfBookmark(4096).BookmarkId))!.Status);
        Assert.Null((await reader.FindBookmarkAsync(BulkEfRun, BulkEfBookmark(4096).BookmarkId))!.EndedHistoryEventId);
    }

    private static void AssertBulkEfHeadEqual(RuntimeHistoryHead? expected, RuntimeHistoryHead? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.WorkflowExecutionId, actual.WorkflowExecutionId);
        Assert.Equal(expected.FirstSequence, actual.FirstSequence);
        Assert.Equal(expected.LastSequence, actual.LastSequence);
        Assert.Equal(expected.LastPhysicalCommitId, actual.LastPhysicalCommitId);
        Assert.Equal(expected.LastCommittedAt, actual.LastCommittedAt);
        Assert.Equal(expected.CapturedFromStart, actual.CapturedFromStart);
        Assert.Equal(expected.HasGaps, actual.HasGaps);
        Assert.Equal(expected.PurgedThroughSequence, actual.PurgedThroughSequence);
        Assert.Equal(expected.Policy.PolicyId, actual.Policy.PolicyId);
        Assert.Equal(expected.Policy.Version, actual.Policy.Version);
        Assert.Equal(expected.Policy.Required, actual.Policy.Required);
        Assert.Equal(expected.Policy.SupportedKinds.ToArray(), actual.Policy.SupportedKinds.ToArray());
    }

    private static BookmarkState BulkEfBookmark(int index) => new($"bulk-ef-bookmark-{index:D5}", BulkEfRun, BulkEfOwner,
        "node-a", "resume-a", "stimulus", "safe-hash", null, new Dictionary<string, string>(), OccurredAt, null);
    private static async Task<List<RuntimeHistoryEvent>> BulkEfEventsAsync(IRuntimeHistoryStore reader, string name)
    {
        var events = new List<RuntimeHistoryEvent>(); string? continuation = null;
        for (var window = 0; window < 200; window++)
        {
            var page = await reader.ReadPageAsync(new(BulkEfRun, 100, continuation, name: name));
            Assert.True(page.Items.Count <= 100); events.AddRange(page.Items);
            continuation = page.NextContinuationToken;
            if (continuation is null) return events;
        }
        throw new InvalidOperationException("Finite EF fixture exceeded its 200-window history budget.");
    }
    private static ActivityExecutionState BulkEfRoot() => new(new(BulkEfOwner, BulkEfRun, "node-a", "authored-a", "Test", "1"),
        ActivityExecutionStatus.Suspended, null, 1, OccurredAt, OccurredAt, null, null, null, null, null,
        ActivitySchedulingProvenance.From(BulkEfRun, null, null, null, null, null, BulkEfOwner, "test"), 0, [], [], 0, 0,
        new Dictionary<string, string>(), ExecutionScopeId: BulkEfOwner);

    private sealed class BulkEfFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly bool _capture;
        private readonly HistoryCommitCounter _counter;
        private readonly TemporarySqliteDatabase _database;
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal RuntimeDbContext Context { get; }
        internal IWorkflowExecutionStateStore Executions => Services.GetRequiredService<IWorkflowExecutionStateStore>();
        internal IActivityExecutionStateStore Activities => Services.GetRequiredService<IActivityExecutionStateStore>();
        internal IBookmarkStateStore Bookmarks => Services.GetRequiredService<IBookmarkStateStore>();
        internal RuntimeCheckpointCommitter Committer => Services.GetRequiredService<RuntimeCheckpointCommitter>();
        internal BulkEfProbe Probe { get; }
        internal int PhysicalCommits => _counter.PhysicalCommits;
        internal string ConnectionString => _database.ConnectionString;
        internal int OptionalHistoryResolutions { get; private set; }
        private BulkEfFixture(ServiceProvider provider, AsyncServiceScope scope, bool capture, BulkEfProbe probe, HistoryCommitCounter counter, TemporarySqliteDatabase database)
        { _provider = provider; _scope = scope; _capture = capture; Probe = probe; _counter = counter; _database = database; Context = Services.GetRequiredService<RuntimeDbContext>(); }
        internal static async Task<BulkEfFixture> CreateAsync(bool capture)
        {
            var database = new TemporarySqliteDatabase("history-bulk");
            ServiceProvider? provider = null;
            AsyncServiceScope? scope = null;
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton<IPersistenceAccessContextAccessor>(new FixedAccessor("tenant-a"));
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(OccurredAt));
                services.AddWorkflowRuntime();
                services.AddRuntimeHistory(options => options.Enabled = capture);
                var persistence = HistoryEfOptions();
                persistence.ConnectionString = database.ConnectionString;
                services.AddRuntimeEntityFrameworkCore(persistence);
                var counter = new HistoryCommitCounter();
                services.ConfigureDbContext<RuntimeSqliteDbContext>(options => options.AddInterceptors(counter));
                var probe = new BulkEfProbe();
                services.AddSingleton<IRuntimeCheckpointCommitEnricher>(probe);
                BulkEfFixture? fixture = null;
                if (!capture) services.AddScoped<IRuntimeHistoryStore>(_ =>
                {
                    fixture!.OptionalHistoryResolutions++;
                    throw new IOException("Uncaptured native controls must not resolve the optional history reader.");
                });
                provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                scope = provider.CreateAsyncScope();
                fixture = new(provider, scope.Value, capture, probe, counter, database);
                await fixture.Context.Database.OpenConnectionAsync(); await fixture.Context.Database.EnsureCreatedAsync();
                return fixture;
            }
            catch
            {
                try { if (scope is { } created) await created.DisposeAsync(); }
                finally
                {
                    try { if (provider is not null) await provider.DisposeAsync(); }
                    finally { await database.DisposeAsync(); }
                }
                throw;
            }
        }
        internal async Task SeedAsync()
        {
            var workflow = Execution(BulkEfRun, "tenant-a") with
            { HistoryCapturePolicy = _capture ? BulkEfPolicy : null, HistoryCaptureEnabledAtAdmission = _capture };
            // Establish the real immutable executable pin before the ordinary root-write lease.
            await Services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(new WorkflowExecutable(workflow.PinnedExecutable,
                new ExecutableNode("node-a", "authored-a", "Test", "1", "descriptor", JsonSerializer.SerializeToElement(new { }),
                    new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget>(), OccurredAt, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
            await Committer.CommitAsync(Raw("bulk-ef-start", RuntimeCheckpointNames.WorkflowStarted,
                new(new(BulkEfRun, RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()), null,
                    [new(BulkEfOwner, RuntimeStateChangeOperation.Upsert, BulkEfRoot(), new Dictionary<string, string>())], [], [], [], [])));
            foreach (var window in Enumerable.Range(0, 4097).Chunk(128))
            {
                var bookmarks = window.Select(BulkEfBookmark).Select(bookmark =>
                    new RuntimeStateChange<BookmarkState>(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, new Dictionary<string, string>())).ToArray();
                await Committer.CommitAsync(Raw("bulk-ef-created-" + window[0], RuntimeCheckpointNames.BookmarkCreated,
                    new(null, null, [new(BulkEfOwner, RuntimeStateChangeOperation.Upsert, BulkEfRoot(), new Dictionary<string, string>())], bookmarks, [], [], [])));
            }
        }
        private static RuntimeCheckpointCommit Raw(string id, string name, RuntimeCheckpointStateChangeSet changes)
        {
            var commit = Commit(id);
            return commit with { Checkpoint = commit.Checkpoint with { Name = name, ActivityExecutionIds = [BulkEfOwner] }, StateChanges = changes };
        }
        internal Task CancelAsync() => new WorkflowCancelActivityScopeSchedulerWorkHandler(Activities,
            new(Services.GetRequiredService<IActivityScopeCleanupStore>(), Services.GetRequiredService<IIncidentStateStore>()),
            Committer, Services.GetRequiredService<IRuntimeActivityExecutionInspectionAccumulator>(), new FakeTimeProvider(OccurredAt))
            .HandleAsync(new("bulk-ef-cancel", BulkEfRun, "bulk-ef-command", WorkflowExecutionCommandKind.CancelActivityScope,
                "bulk-ef-envelope", "bulk-ef-delivery", OccurredAt, OccurredAt,
                payload: JsonSerializer.SerializeToElement(new CancelActivityScopeCommand(BulkEfOwner, BulkEfOwner, "bulk cleanup")), executionScopeId: BulkEfOwner)).AsTask();
        public async ValueTask DisposeAsync()
        {
            try { await _scope.DisposeAsync(); }
            finally
            {
                try { await _provider.DisposeAsync(); }
                finally { await _database.DisposeAsync(); }
            }
        }
    }
    private sealed class BulkEfProbe : IRuntimeCheckpointCommitEnricher
    {
        public int Order => int.MaxValue;
        internal RuntimeCheckpointCommit? Last { get; private set; }
        internal Func<RuntimeCheckpointCommit, ValueTask>? BeforeWrite { get; set; }
        public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            Last = commit;
            if (commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityCancelled && BeforeWrite is { } before) await before(commit);
            return commit;
        }
    }
}

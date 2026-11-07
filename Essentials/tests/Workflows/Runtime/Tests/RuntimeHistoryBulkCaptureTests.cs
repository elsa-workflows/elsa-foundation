using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistoryBulkCaptureTests
{
    private const string Run = "bulk-run", Owner = "bulk-owner";
    private static readonly DateTimeOffset At = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true,
        [.. RuntimeHistoryCapture.Kinds, "ControlApplied", "IncidentSuppressed", "IncidentStrategyEvaluated"]);
    private static readonly WorkflowExecutableIdentity Executable = new("bulk-artifact", "bulk-definition", "bulk-version", "1", "bulk-hash");
    private static readonly IReadOnlyDictionary<string, string> Metadata = new Dictionary<string, string>();

    [Theory]
    [InlineData(false, 256, 0)]
    [InlineData(true, 256, 0)]
    [InlineData(false, 0, 4097)]
    [InlineData(true, 0, 4097)]
    [Trait("Feature", "RunHistory")]
    public async Task Actual_scope_cancellation_keeps_all_native_resources_and_one_physical_decision(bool capture, int incidentCount, int bookmarkCount)
    {
        using var fixture = new BulkFixture(capture);
        await fixture.SeedAsync(incidentCount, bookmarkCount);
        var before = fixture.Store.ListCommits().Count;

        await fixture.CancelAsync();

        Assert.Equal(before + 1, fixture.Store.ListCommits().Count);
        Assert.Equal(ActivityExecutionStatus.Cancelled, (await fixture.Activities.FindAsync(Run, Owner))!.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.Executions.FindAsync(Run))!.Status);
        Assert.Empty(await fixture.Bookmarks.ListAllBookmarkStatesAsync(Run));
        var incidents = await fixture.Incidents.ListAsync(Run);
        Assert.Equal(incidentCount, incidents.Count);
        Assert.All(incidents, incident => Assert.Equal(IncidentStatus.Suppressed, incident.Status));
        Assert.All(incidents, incident => Assert.Equal(IncidentResolutionActionKinds.SuppressIncident, incident.ResolutionOutcome!.ActionKind));
        var committed = fixture.Store.ListCommits().Last().Commit;
        if (capture)
        {
            var batch = Assert.Single(committed.HistoryBatches!);
            Assert.Equal(incidentCount + 1, committed.HistorySourceFacts!.Count);
            Assert.Equal(committed.Checkpoint.CheckpointId, batch.LogicalCheckpointId);
            Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(committed with { HistoryBatches = null }), batch.SourceFingerprint);
            Assert.Equal(Enumerable.Range(0, batch.Candidates.Count), batch.Candidates.Select(candidate => candidate.CheckpointOrdinal));
            Assert.Equal(incidentCount, batch.Candidates.Count(candidate => candidate.Event.Name == "IncidentSuppressed"));
            Assert.Equal(bookmarkCount, batch.Candidates.Count(candidate => candidate.Event.Name == "BookmarkRemoved"));
            Assert.Single(batch.Candidates, candidate => candidate.Event.Name == "ControlApplied");
            Assert.DoesNotContain(batch.Candidates, candidate => candidate.Event.Name == "BookmarkConsumed");
            var events = await fixture.EventsAsync();
            Assert.Equal(bookmarkCount, events.Count(item => item.Name == "BookmarkCreated"));
            Assert.Equal(bookmarkCount, events.Count(item => item.Name == "BookmarkRemoved"));
            Assert.Equal(Enumerable.Range(1, events.Count).Select(value => (long)value), events.Select(item => item.Sequence));
            if (bookmarkCount > 0)
            {
                var inventory = await fixture.Reader.ListBookmarksAsync(new(Run, activityExecutionId: Owner));
                Assert.Equal(bookmarkCount, inventory.TotalCount); Assert.Equal(0, inventory.ActiveCount);
                Assert.Equal(RuntimeHistoryBookmarkStatus.Removed, (await fixture.Reader.FindBookmarkAsync(Run, BookmarkId(bookmarkCount - 1)))!.Status);
            }
            var sequence = (await fixture.Reader.FindHeadAsync(Run))!.LastSequence;
            await fixture.Store.CommitAsync(committed, new(RuntimeCheckpointPersistenceMode.Immediate));
            Assert.Equal(sequence, (await fixture.Reader.FindHeadAsync(Run))!.LastSequence);
        }
        else
        {
            Assert.Null(committed.HistorySourceFacts); Assert.Null(committed.HistoryBatches);
            Assert.Empty(fixture.State.HistoryHeads); Assert.Equal(0, fixture.OptionalHistoryResolutions);
        }
        // The original command also becomes a native no-op after its owning scope is terminal.
        await fixture.CancelAsync();
        Assert.Equal(before + 1, fixture.Store.ListCommits().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "RunHistory")]
    public async Task Actual_quiescent_incident_drain_retains_all_257_evaluations_in_one_commit(bool capture)
    {
        using var fixture = new BulkFixture(capture);
        await fixture.SeedAsync(257, 0);
        var executable = new WorkflowExecutable(Executable,
            new ExecutableNode("node", "authored", "Test", "1", "descriptor", JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), At, Metadata, IncidentStrategyBuiltIns.FaultReference);
        var executables = new InMemoryWorkflowExecutableStore();
        await executables.SaveAsync(executable);
        var executor = new IncidentResolutionBatchExecutor(fixture.Executions, fixture.Activities, fixture.Incidents,
            executables, new BuiltInFaultResolver(), fixture.Committer, new FakeTimeProvider(At));
        var envelope = new WorkflowExecutionCommandEnvelope("bulk-envelope", Run,
            new("bulk-command", Run, WorkflowExecutionCommandKind.RunSchedulerWork, At, null, Metadata),
            "bulk-delivery", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, At);
        var before = fixture.Store.ListCommits().Count;

        await new IncidentStrategyResolutionDrainObserver(fixture.Incidents, executor)
            .OnDrainedAsync(envelope, new(Run, At, At, []));

        Assert.Equal(before + 1, fixture.Store.ListCommits().Count);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await fixture.Executions.FindAsync(Run))!.Status);
        Assert.Equal(257, (await fixture.Incidents.ListAsync(Run)).Count);
        Assert.All(await fixture.Incidents.ListAsync(Run), incident =>
            Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, incident.ResolutionOutcome!.ActionKind));
        var committed = fixture.Store.ListCommits().Last().Commit;
        if (capture)
        {
            Assert.Equal(257, committed.HistorySourceFacts!.Count);
            var candidates = Assert.Single(committed.HistoryBatches!).Candidates;
            Assert.Equal(257, candidates.Count(candidate => candidate.Event.Name == "IncidentStrategyEvaluated"));
            Assert.Single(candidates, candidate => candidate.Event.Name == "IncidentResolutionBatchApplied");
        }
        else
        {
            Assert.Null(committed.HistoryBatches); Assert.Equal(0, fixture.OptionalHistoryResolutions);
        }
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Actual_wide_cancellation_folds_prior_logical_history_without_an_extra_physical_boundary()
    {
        using var fixture = new BulkFixture(true);
        await fixture.SeedAsync(0, 4097);
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var decorated = new CoalescingRuntimeCheckpointCommitStore(new(fixture.Writer), sessions);
        var committer = fixture.NewCommitter(decorated);
        var factory = new RuntimeCoalescingDrainScopeFactory(sessions, committer, new(fixture.Queue), new(fixture.Store),
            new PersistenceModeOptions(), new FakeTimeProvider(At));
        var before = fixture.Store.ListCommits().Count;
        await using var scope = factory.Begin(Run, new(PersistenceMode.Balanced, true));
        var prior = await fixture.Capture.EnrichAsync(NativeCommit("bulk-prior", RuntimeCheckpointNames.ActivityStarted,
            activity: Root() with { Status = ActivityExecutionStatus.Running }));
        await decorated.CommitAsync(prior, new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.Equal(before, fixture.Store.ListCommits().Count);

        await fixture.CancelAsync(committer);

        var physical = fixture.Store.ListCommits().Last().Commit;
        Assert.Equal(before + 1, fixture.Store.ListCommits().Count);
        Assert.Equal(2, physical.HistoryBatches!.Count);
        Assert.Equal(prior.HistoryBatches![0].LogicalCheckpointId, physical.HistoryBatches[0].LogicalCheckpointId);
        Assert.Equal(4097, physical.HistoryBatches[1].Candidates.Count(candidate => candidate.Event.Name == "BookmarkRemoved"));
        Assert.All(physical.HistoryBatches, batch => Assert.Equal(Enumerable.Range(0, batch.Candidates.Count), batch.Candidates.Select(candidate => candidate.CheckpointOrdinal)));
        Assert.Empty(await fixture.Bookmarks.ListAllBookmarkStatesAsync(Run));
        var count = (await fixture.Reader.FindHeadAsync(Run))!.LastSequence;
        await fixture.Store.CommitAsync(physical, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Equal(count, (await fixture.Reader.FindHeadAsync(Run))!.LastSequence);
        Assert.Equal(before + 1, fixture.Store.ListCommits().Count);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Wide_cleanup_refuses_a_changed_last_native_source_before_any_participant_is_applied()
    {
        using var fixture = new BulkFixture(true);
        await fixture.SeedAsync(0, 4097);
        var before = fixture.Store.ListCommits().Count;
        var head = await fixture.Reader.FindHeadAsync(Run);
        fixture.Writer.BeforeWrite = async commit =>
        {
            if (commit.Checkpoint.Name != RuntimeCheckpointNames.ActivityCancelled) return;
            var bookmark = (await fixture.Bookmarks.FindAsync(Run, BookmarkId(4096)))!;
            await fixture.Bookmarks.SaveAsync(new(bookmark.BookmarkId, bookmark.WorkflowExecutionId, bookmark.ActivityExecutionId,
                bookmark.ExecutableNodeId, bookmark.ResumeTargetId, bookmark.StimulusType, "new-native-generation", bookmark.Payload,
                bookmark.Metadata, bookmark.CreatedAt, bookmark.ExpiresAt));
        };

        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => fixture.CancelAsync());

        Assert.Equal(before, fixture.Store.ListCommits().Count);
        Assert.Equal(head, await fixture.Reader.FindHeadAsync(Run));
        Assert.Equal(4097, (await fixture.Bookmarks.ListAllBookmarkStatesAsync(Run)).Count);
        Assert.Equal(ActivityExecutionStatus.Suspended, (await fixture.Activities.FindAsync(Run, Owner))!.Status);
        Assert.DoesNotContain(await fixture.EventsAsync(), item => item.Name is "BookmarkRemoved" or "ControlApplied");
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Unsafe_tail_candidate_is_refused_without_a_native_commit()
    {
        using var fixture = new BulkFixture(true);
        await fixture.SeedAsync(0, 4097);
        var before = fixture.Store.ListCommits().Count;
        var head = await fixture.Reader.FindHeadAsync(Run);
        fixture.Writer.BeforeWrite = commit =>
        {
            if (commit.Checkpoint.Name != RuntimeCheckpointNames.ActivityCancelled) return ValueTask.CompletedTask;
            var batch = Assert.Single(commit.HistoryBatches!);
            var candidates = batch.Candidates.ToArray();
            var candidate = candidates[4096]; var e = candidate.Event;
            var metadata = new Dictionary<string, string>(e.Metadata) { ["history.reasonCode"] = new('x', 2049) };
            var unsafeEvent = new RuntimeHistoryEvent(e.HistoryEventId, e.WorkflowExecutionId, 0, e.Category, e.Name, e.OccurredAt,
                e.ActivityExecutionId, e.ExecutableNodeId, e.CheckpointId, e.IncidentId, metadata: metadata)
            { Authority = e.Authority, BookmarkId = e.BookmarkId, Causation = e.Causation };
            candidates[4096] = new(unsafeEvent, candidate.CheckpointOrdinal, candidate.BookmarkMutation);
            // Immutable groups validate their complete contents, including beyond the former ceiling.
            _ = new RuntimeHistoryBatch(batch.Policy, batch.LogicalCheckpointId, batch.OccurredAt, batch.StartsAtRunStart, candidates);
            return ValueTask.CompletedTask;
        };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CancelAsync());

        Assert.Contains("metadata", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, fixture.Store.ListCommits().Count); Assert.Equal(head, await fixture.Reader.FindHeadAsync(Run));
        Assert.Equal(4097, (await fixture.Bookmarks.ListAllBookmarkStatesAsync(Run)).Count);
        Assert.Equal(ActivityExecutionStatus.Suspended, (await fixture.Activities.FindAsync(Run, Owner))!.Status);
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Small_capture_retains_the_original_single_group_serialized_fingerprint_shape()
    {
        using var fixture = new BulkFixture(true);
        await fixture.SeedAsync(0, 0);
        var bookmark = Bookmark(0);
        var raw = NativeCommit("small-bookmark", RuntimeCheckpointNames.BookmarkCreated, activity: Root(),
            bookmarks: [new(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, Metadata)]);
        var captured = await fixture.Capture.EnrichAsync(raw);
        var e = new RuntimeHistoryEvent(RuntimeHistoryIdentity.CreateId(raw.Checkpoint.CheckpointId, "BookmarkCreated", bookmark.BookmarkId),
            Run, 0, RuntimeHistoryEventCategory.BookmarkLifecycle, "BookmarkCreated", At, Owner, "node", raw.Checkpoint.CheckpointId,
            metadata: new Dictionary<string, string> { ["history.stimulusType"] = bookmark.StimulusType, ["history.resumeTargetId"] = bookmark.ResumeTargetId })
        { Authority = RuntimeHistoryAuthority.CommittedTransition, BookmarkId = bookmark.BookmarkId, ExecutionScopeId = Owner,
            ActivityAncestryKnown = true, Causation = new() };
        var expected = raw with { HistoryBatches = [new(Policy, raw.Checkpoint.CheckpointId, At, false,
            [new(e, 0, new(ActivityExecutionBookmarkSummary.From(bookmark), RuntimeHistoryBookmarkStatus.Active))])
            { SourceFingerprint = RuntimeCheckpointCommitFingerprint.Compute(raw) }] };
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(captured));
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(expected), RuntimeCheckpointCommitFingerprint.Compute(captured));
        Assert.Null(raw.HistoryBatches); Assert.Null(raw.HistorySourceFacts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "RunHistory")]
    public async Task Prior_duplicate_facts_outside_the_pinned_vocabulary_remain_untouched_until_their_key_is_contributed(bool touchesPrior)
    {
        using var fixture = new BulkFixture(true);
        await fixture.SeedAsync(0, 0);
        var prior = new RuntimeHistorySourceFact(RuntimeHistorySourceFactKind.CompensationRegistered, "omitted-compensation", At,
            Owner, compensationEntryId: "compensation-entry");
        var control = new RuntimeHistorySourceFact(RuntimeHistorySourceFactKind.ControlApplied, "new-control", At, Owner, reasonCode: "operator-control");
        var raw = NativeCommit("prior-duplicates", "OwnerDecision") with { HistorySourceFacts = [prior, prior] };
        Assert.DoesNotContain("CompensationRegistered", Policy.SupportedKinds);
        if (touchesPrior)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Committer.AddHistorySourceFactsAsync(raw, () => [prior]).AsTask());
        }
        else
        {
            var contributed = await fixture.Committer.AddHistorySourceFactsAsync(raw, () => [control]);
            var expected = raw with { HistorySourceFacts = [prior, prior, control] };
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(contributed));
            Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(expected), RuntimeCheckpointCommitFingerprint.Compute(contributed));
            var captured = await fixture.Capture.EnrichAsync(contributed);
            await fixture.Capture.ValidateFinalAsync(captured);
            var batch = Assert.Single(captured.HistoryBatches!);
            Assert.Single(batch.Candidates, candidate => candidate.Event.Name == "ControlApplied");
            Assert.DoesNotContain(batch.Candidates, candidate => candidate.Event.Name == "CompensationRegistered");
            Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(expected), batch.SourceFingerprint);
        }
        Assert.Equal(2, raw.HistorySourceFacts!.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Feature", "RunHistory")]
    public async Task Unique_prior_fact_keeps_identical_incoming_deduplication_and_conflicting_content_refusal(bool conflicting)
    {
        using var fixture = new BulkFixture(true);
        await fixture.SeedAsync(0, 0);
        var prior = new RuntimeHistorySourceFact(RuntimeHistorySourceFactKind.ControlApplied, "same-control", At, Owner, reasonCode: "original");
        var incoming = new RuntimeHistorySourceFact(RuntimeHistorySourceFactKind.ControlApplied, "same-control", At, Owner,
            reasonCode: conflicting ? "changed" : "original");
        var raw = NativeCommit("merge-prior", "OwnerDecision") with { HistorySourceFacts = [prior] };
        if (conflicting)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Committer.AddHistorySourceFactsAsync(raw, () => [incoming]).AsTask());
        else
        {
            var contributed = await fixture.Committer.AddHistorySourceFactsAsync(raw, () => [incoming, incoming]);
            Assert.Single(contributed.HistorySourceFacts!);
            Assert.Equal(JsonSerializer.Serialize(raw), JsonSerializer.Serialize(contributed));
            Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(raw), RuntimeCheckpointCommitFingerprint.Compute(contributed));
        }
    }

    private static string BookmarkId(int index) => $"bulk-bookmark-{index:D5}";
    private static BookmarkState Bookmark(int index) => new(BookmarkId(index), Run, Owner, "node", "continue", "task", "safe-hash", null, Metadata, At, null);
    private static ActivityExecutionState Root() => new(new(Owner, Run, "node", "authored", "Test", "1"),
        ActivityExecutionStatus.Suspended, null, 1, At, At, null, null, null, null, null,
        ActivitySchedulingProvenance.From(Run, null, null, null, null, null, Owner, "test"), 0, [], [], 0, 0, Metadata, ExecutionScopeId: Owner);
    private static RuntimeCheckpointCommit NativeCommit(string id, string name, WorkflowExecutionState? workflow = null,
        ActivityExecutionState? activity = null, IReadOnlyCollection<RuntimeStateChange<BookmarkState>>? bookmarks = null,
        IReadOnlyCollection<RuntimeStateChange<IncidentState>>? incidents = null) =>
        new(id, new("checkpoint-" + id, name, Run, At, activity is null ? [] : [Owner], Metadata),
            new(workflow is null ? null : new(Run, RuntimeStateChangeOperation.Upsert, workflow, Metadata), null,
                activity is null ? [] : [new(Owner, RuntimeStateChangeOperation.Upsert, activity, Metadata)], bookmarks ?? [], [], incidents ?? [], []), [], Metadata);

    private sealed class BulkFixture : IDisposable
    {
        private readonly bool _capture;
        private readonly ServiceProvider _provider;
        private readonly RuntimeHistorySourceAvailability _source;
        internal InMemoryRuntimeCheckpointStoreState State { get; } = new();
        internal InMemoryWorkflowExecutionStateStore Executions { get; }
        internal InMemoryActivityExecutionStateStore Activities { get; } = new();
        internal InMemoryIncidentStateStore Incidents { get; } = new();
        internal InMemoryBookmarkStateStore Bookmarks { get; } = new();
        internal InMemoryWorkflowSchedulerWorkQueue Queue { get; } = new();
        internal InMemoryActivityExecutionInspectionStore Inspections { get; } = new();
        internal ActivityScopeCleanupStore Cleanup { get; }
        internal InMemoryRuntimeCheckpointCommitStore Store { get; }
        internal InterceptingWriter Writer { get; }
        internal RuntimeHistoryCapture Capture { get; }
        internal InMemoryRuntimeHistoryStore Reader { get; }
        internal RuntimeCheckpointCommitter Committer { get; }
        internal int OptionalHistoryResolutions { get; private set; }
        internal BulkFixture(bool capture)
        {
            _capture = capture;
            Executions = new(State, null);
            var access = new BulkAccess();
            Cleanup = new(Bookmarks, new InMemoryDurableTimerStore(), Queue, Executions);
            Store = new(workflowExecutionStateStore: Executions, activityExecutionStateStore: Activities, bookmarkStateStore: Bookmarks,
                incidentStateStore: Incidents, activityExecutionInspectionWriter: Inspections, state: State,
                timeProvider: new FakeTimeProvider(At), activityScopeCleanupStore: Cleanup, persistenceAccessContextAccessor: access,
                rootWriteLeaseManager: new BulkLease());
            Writer = new(Store);
            Reader = new(State, access, new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
                { SigningKey = "bulk-capture-signing-key-at-least-32-bytes" })));
            Capture = new(Executions);
            _provider = new ServiceCollection().AddSingleton<IWorkflowExecutionStateStore>(Executions).BuildServiceProvider();
            _source = new(_provider);
            Committer = NewCommitter(Writer);
        }
        internal RuntimeCheckpointCommitter NewCommitter(IRuntimeCheckpointCommitStore writer) =>
            new(new ImmediateRuntimeCheckpointPersistencePolicy(), writer, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), _capture ? [Capture] : [], [])
            { HistoryStore = _capture ? Reader : null, HistoryFactContributor = (commit, facts, token) => _source.AddFactsAsync(commit, facts, token),
                HistoryStoreResolver = () => { OptionalHistoryResolutions++; throw new IOException("Optional history backend must not be resolved for uncaptured work."); } };
        internal async Task SeedAsync(int incidentCount, int bookmarkCount)
        {
            var workflow = new WorkflowExecutionState(Run, Executable, WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, "scope-a", Metadata)
            { HistoryCapturePolicy = _capture ? Policy : null, HistoryCaptureEnabledAtAdmission = _capture };
            await Committer.CommitAsync(NativeCommit("bulk-start", RuntimeCheckpointNames.WorkflowStarted, workflow, Root()));
            foreach (var window in Enumerable.Range(0, incidentCount).Chunk(128))
            {
                var incidents = window.Select(index => new IncidentState($"bulk-incident-{index:D5}", Run, Owner, "node", IncidentSeverity.Error,
                    IncidentStatus.Blocking, null, "TestFault", "native fixture incident", At, null, Metadata)).ToArray();
                var raw = NativeCommit("incidents-" + window[0], RuntimeCheckpointNames.IncidentRecorded,
                    incidents: incidents.Select(item => new RuntimeStateChange<IncidentState>(item.IncidentId, RuntimeStateChangeOperation.Upsert, item, Metadata)).ToArray());
                raw = await Committer.AddHistorySourceFactsAsync(raw, () => incidents.Select(incident => RuntimeHistoryIncidentFacts.Created(incident)));
                await Committer.CommitAsync(raw);
            }
            foreach (var window in Enumerable.Range(0, bookmarkCount).Chunk(128))
                await Committer.CommitAsync(NativeCommit("bookmarks-" + window[0], RuntimeCheckpointNames.BookmarkCreated, activity: Root(),
                    bookmarks: window.Select(Bookmark).Select(item => new RuntimeStateChange<BookmarkState>(item.BookmarkId, RuntimeStateChangeOperation.Upsert, item, Metadata)).ToArray()));
        }
        internal Task CancelAsync(RuntimeCheckpointCommitter? committer = null) =>
            new WorkflowCancelActivityScopeSchedulerWorkHandler(Activities, new(Cleanup, Incidents), committer ?? Committer,
                new RuntimeActivityExecutionInspectionAccumulator(Inspections, Executions, new RuntimeHistoryOptions()), new FakeTimeProvider(At))
                .HandleAsync(new("bulk-cancel-work", Run, "bulk-cancel-command", WorkflowExecutionCommandKind.CancelActivityScope,
                    "bulk-cancel-envelope", "bulk-cancel-idempotency", At, At,
                    payload: JsonSerializer.SerializeToElement(new CancelActivityScopeCommand(Owner, Owner, "bulk cancellation")), executionScopeId: Owner)).AsTask();
        internal async Task<List<RuntimeHistoryEvent>> EventsAsync()
        {
            var events = new List<RuntimeHistoryEvent>(); string? continuation = null;
            for (var window = 0; window < 200; window++)
            {
                var page = await Reader.ReadPageAsync(new(Run, 100, continuation));
                Assert.True(page.Items.Count <= 100);
                events.AddRange(page.Items); continuation = page.NextContinuationToken;
                if (continuation is null) return events;
            }
            throw new InvalidOperationException("Finite native fixture exceeded its 200-window history budget.");
        }
        public void Dispose() => _provider.Dispose();
    }
    private sealed class InterceptingWriter(IRuntimeCheckpointCommitStore inner) : IRuntimeCheckpointCommitStore
    {
        internal Func<RuntimeCheckpointCommit, ValueTask>? BeforeWrite { get; set; }
        public bool SupportsHistory => inner.SupportsHistory;
        public bool CanCommitHistory(IRuntimeHistoryStore reader) => inner.CanCommitHistory(reader);
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            if (BeforeWrite is { } before) await before(commit);
            return await inner.CommitAsync(commit, decision, cancellationToken);
        }
    }
    private sealed class BulkAccess : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("scope-a")); }
    private sealed class BulkLease : IWorkflowExecutableRootWriteLeaseManager
    { public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) => write(cancellationToken); }
    private sealed class BuiltInFaultResolver : IIncidentStrategyResolver
    { public IIncidentStrategy Resolve(IncidentStrategyReference reference) => new FaultIncidentStrategy(); }
}

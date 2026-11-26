using System.Text.Json;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

// Real native queue/handler/checkpoint/callback component proof. Not durable process/host acceptance.
public sealed partial class WorkflowResumeBookmarkSchedulerWorkHandlerTests
{
    private static readonly PersistenceScope ExactScope = new(PersistenceScope.DefaultValue);
    private sealed class ExactAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(ExactScope); }

    private async Task<(RuntimeSchedulerWorkItem Work, FakeTimeProvider Clock)> PrepareExactResume(bool testReceipt = false, bool missingActivity = false)
    {
        var clock = new FakeTimeProvider(_now); var ownership = new InMemoryExecutionLivenessStateStore();
        _exactWorkflowStates = CanonicalWorkflowStateTestData.EnsureRunning(
            new InMemoryWorkflowExecutionStateStore(_checkpointState, ownership), identity: NewIdentity());
        if (testReceipt)
        {
            var testScope = new WorkflowTestScope("test-scope", _now.AddMinutes(10), null, new("test-partition"));
            await new InMemoryWorkflowTestScopeStore(_checkpointState).CreateAsync(testScope, _now);
            await _exactWorkflowStates.SaveAsync((await _exactWorkflowStates.FindAsync("wfexec-1"))! with
                { RunKind = WorkflowRunKind.TestRun, Partition = testScope.Partition, TestScope = testScope });
        }
        _checkpointWriter = new(_executableStore, new InMemoryWorkflowExecutableSourceReferenceStore(_executableStore, clock),
            _exactWorkflowStates, _activityStateStore, _bookmarkStateStore, incidentStateStore: _incidentStateStore,
            operationalStateStore: ownership, activityExecutionInspectionWriter: _inspectionStore, state: _checkpointState,
            timeProvider: clock, persistenceAccessContextAccessor: new ExactAccess(),
            rootWriteLeaseManager: new WorkflowExecutableRootWriteLeaseManager(_executableStore, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock));
        var executable = NewTypedExecutable("original"); var type = Descriptor<ApprovalTrigger>();
        await _executableStore.SaveAsync(executable);
        if (!missingActivity) await _activityStateStore.SaveAsync(NewTypedTriggerState(executable.RootActivity.ActivityContract!, type));
        await SaveBookmarkAsync();
        var capture = StimulusResumeRecipient.From((await _bookmarkStateStore.ObserveAsync("wfexec-1", "bookmark-1"))!,
            (await _exactWorkflowStates.FindAsync("wfexec-1"))!, ExactScope);
        var original = NewResumeWorkItem(input: JsonSerializer.SerializeToElement(new ApprovalTrigger(true)), triggerDelivery: Delivery(type, "dedupe-42"));
        var payload = original.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
        var capturedPayload = new RuntimeResumeBookmarkCommandPayload(payload.PinnedExecutable, payload.BookmarkId,
            payload.ActivityExecutionId, payload.ExecutableNodeId, payload.ResumeTargetId, payload.StimulusType, payload.StimulusHash,
            payload.Input, payload.Reason, payload.TriggerDelivery,
            testReceipt ? new("test-scope", "c4c0377d-3c4b-4b21-bacc-0c6cdce13608", "bookmark-1", new string('a', 64)) : null) { ExpectedBookmarkResume = capture };
        return (new(original.WorkItemId, original.WorkflowExecutionId, original.CommandId, original.CommandKind,
            original.EnvelopeId, original.IdempotencyKey, original.EnqueuedAt, original.RecordedAt, original.Sequence,
            JsonSerializer.SerializeToElement(capturedPayload), original.CommandMetadata, original.EnvelopeMetadata), clock);
    }

    private WorkflowSchedulerDrainer ExactDrainer(ServiceProvider provider, FakeTimeProvider clock, InMemoryWorkflowSchedulerPoisonStore poison) =>
        new(_schedulerWorkQueue, [NewHandler(provider)], provider.GetRequiredService<IWorkflowExecutionStateStore>(), poison,
            new WorkflowSchedulerPauseGate(new RuntimePauseDecisionProvider(new InMemoryWorkflowHoldStateStore()), clock), clock,
            claimOptions: new RuntimeSchedulerWorkClaimOptions { UnavailableRetryDelay = TimeSpan.FromSeconds(5) });

    [Theory]
    [InlineData("matching")] [InlineData("rearmed")] [InlineData("completed")] [InlineData("cancelled")]
    [InlineData("superseded")] [InlineData("workflow-terminal")]
    public async Task Exact_queued_resume_invokes_only_the_matching_live_wait(string condition)
    {
        var (work, clock) = await PrepareExactResume();
        var state = (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!;
        if (condition == "rearmed") { await _bookmarkStateStore.DeleteAsync("wfexec-1", "bookmark-1"); await SaveBookmarkAsync(); }
        if (condition == "completed") await _activityStateStore.SaveAsync(state with { Status = ActivityExecutionStatus.Completed });
        if (condition == "cancelled") await _activityStateStore.SaveAsync(state with { Status = ActivityExecutionStatus.Cancelled });
        if (condition == "superseded") await _activityStateStore.SaveAsync(state with { Status = ActivityExecutionStatus.Superseded, SupersededByActivityExecutionId = "successor", SupersededAt = _now });
        if (condition == "workflow-terminal") await _exactWorkflowStates!.SaveAsync((await _exactWorkflowStates.FindAsync("wfexec-1"))! with { Status = WorkflowExecutionStatus.Completed });
        var generation = (await _bookmarkStateStore.ObserveAsync("wfexec-1", "bookmark-1"))!.Generation;
        var activator = new StatefulResumeActivator(); var observer = new RecordingBookmarkObserver(); var poison = new InMemoryWorkflowSchedulerPoisonStore();
        await _schedulerWorkQueue.EnqueueAsync(work);
        await using var provider = NewProvider(activator, observer);
        var drained = await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1));
        if (condition == "workflow-terminal")
        {
            Assert.Empty(drained.Items); // The real drain blocks a terminal workflow before dispatch.
            await NewHandler(provider).HandleAsync(work); // The independent handler preflight also refuses it.
        }
        else Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(drained.Items).Status);
        if (condition == "matching")
        {
            Assert.Single(activator.Requests); Assert.Equal(new ApprovalTrigger(true), Assert.Single(activator.Activities).ObservedTrigger);
            Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1")); Assert.Single(observer.Consumed);
            Assert.Equal(ActivityExecutionStatus.Completed, (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!.Status);
            Assert.Single(_checkpointWriter.ListCommits(), record => record.Commit.ExpectedBookmarkResume is not null);
        }
        else
        {
            Assert.Empty(activator.Requests); Assert.Empty(observer.Consumed); Assert.Empty(_checkpointWriter.ListCommits());
            Assert.Equal(generation, (await _bookmarkStateStore.ObserveAsync("wfexec-1", "bookmark-1"))!.Generation);
            Assert.Single((await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!.Attempts!);
        }
        Assert.Empty(await poison.ListAsync("wfexec-1"));
    }

    [Theory]
    [InlineData("observation")] [InlineData("checkpoint")] [InlineData("sibling")] [InlineData("qualification")] [InlineData("scope")]
    public async Task Exact_queued_unavailable_admission_retains_identical_work_and_retries_a_real_callback(string boundary)
    {
        var (work, clock) = await PrepareExactResume(); var poison = new InMemoryWorkflowSchedulerPoisonStore();
        var activator = new StatefulResumeActivator(); var observer = new RecordingBookmarkObserver();
        var books = new ObservedBookmarks(_bookmarkStateStore) { FailObservation = boundary == "observation" };
        var checkpoints = new AdmissionCut(_checkpointWriter, books) { FailCheckpoint = boundary == "checkpoint", FailQualification = boundary == "qualification" };
        if (boundary == "sibling")
        {
            var state = (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!; var first = state.TriggerRegistrations!.Single();
            var sibling = new ActivityTriggerRegistration("bookmark-2", first.InvocationId, first.ResumeTargetKey, first.PayloadType,
                first.StimulusType, first.StimulusHash, first.DeduplicationPolicy);
            await _activityStateStore.SaveAsync(state with { BookmarkIds = ["bookmark-1", "bookmark-2"], TriggerRegistrations = [first, sibling] });
            await _bookmarkStateStore.SaveAsync((await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"))! with { BookmarkId = "bookmark-2" });
            books.FailSibling = true;
        }
        await _schedulerWorkQueue.EnqueueAsync(work);
        await using (var provider = NewProvider(activator, observer, services =>
        {
            services.AddSingleton<IBookmarkStateStore>(books); services.AddSingleton<IRuntimeCheckpointCommitStore>(checkpoints);
            if (boundary == "scope") services.AddSingleton<IPersistenceAccessContextAccessor>(new CutAccess());
        }))
        {
            var result = await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1));
            Assert.True(result.StoppedOnDeferral); Assert.Equal(0, result.DrainedCount);
            var queued = Assert.Single(await _schedulerWorkQueue.ListAllAsync("wfexec-1"));
            Assert.Equal(work.Payload!.Value.GetRawText(), queued.Payload!.Value.GetRawText());
            Assert.Empty(activator.Requests); Assert.Empty(observer.Consumed); Assert.Empty(_checkpointWriter.ListCommits());
            Assert.Equal(ActivityExecutionStatus.Suspended, (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!.Status);
            Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1")); Assert.Empty(await poison.ListAsync("wfexec-1"));
        }
        clock.Advance(TimeSpan.FromSeconds(5));
        await using (var provider = NewProvider(activator, observer))
            Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1))).Items).Status);
        Assert.Single(activator.Requests);
        if (boundary == "sibling") Assert.Collection(observer.Consumed, first => Assert.Equal("bookmark-1", first.BookmarkId), second => Assert.Equal("bookmark-2", second.BookmarkId));
        else Assert.Single(observer.Consumed);
        Assert.Empty(await poison.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task Exact_test_receipt_root_lease_refusal_retains_work_then_commits_receipt_and_real_callback()
    {
        var (work, clock) = await PrepareExactResume(testReceipt: true);
        var guard = await _executableStore.TryBeginDeletionAsync("artifact-1", "peer-deletion", _now.AddMinutes(1), _now);
        Assert.NotNull(guard);
        var activator = new StatefulResumeActivator(); var observer = new RecordingBookmarkObserver(); var poison = new InMemoryWorkflowSchedulerPoisonStore();
        await _schedulerWorkQueue.EnqueueAsync(work);
        await using (var provider = NewProvider(activator, observer))
        {
            Assert.True((await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1))).StoppedOnDeferral);
            Assert.Empty(activator.Requests); Assert.Empty(observer.Consumed); Assert.Empty(_checkpointWriter.ListCommits());
            Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1")); Assert.Empty(await poison.ListAsync("wfexec-1"));
            Assert.DoesNotContain((await _exactWorkflowStates!.FindAsync("wfexec-1"))!.SystemMetadata.Keys, key => key.StartsWith(TestRunDeliveryReceipt.ReceiptPrefix, StringComparison.Ordinal));
            Assert.Equal(work.Payload!.Value.GetRawText(), Assert.Single(await _schedulerWorkQueue.ListAllAsync("wfexec-1")).Payload!.Value.GetRawText());
        }
        Assert.True(await _executableStore.CancelDeletionAsync(guard!)); clock.Advance(TimeSpan.FromSeconds(5));
        await using (var provider = NewProvider(activator, observer))
            await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1));
        Assert.Single(activator.Requests); Assert.Single(observer.Consumed); Assert.Empty(await poison.ListAsync("wfexec-1"));
        Assert.Contains((await _exactWorkflowStates!.FindAsync("wfexec-1"))!.SystemMetadata.Keys, key => key.StartsWith(TestRunDeliveryReceipt.ReceiptPrefix, StringComparison.Ordinal));
    }

    private sealed class CutAccess : IPersistenceAccessContextAccessor
    {
        private bool failed;
        public PersistenceAccessContext Current { get { if (!failed) { failed = true; throw new IOException("Constructed persistence access outage."); } return PersistenceAccessContext.Scoped(ExactScope); } }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Exact_missing_owner_or_material_keeps_work_until_authority_can_be_read(bool missingMaterial)
    {
        var (work, clock) = await PrepareExactResume(missingActivity: !missingMaterial);
        if (missingMaterial) Assert.True(await _executableStore.DeleteAsync("artifact-1"));
        var activator = new StatefulResumeActivator(); var poison = new InMemoryWorkflowSchedulerPoisonStore();
        await _schedulerWorkQueue.EnqueueAsync(work);
        await using (var provider = NewProvider(activator))
        {
            Assert.True((await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1))).StoppedOnDeferral);
            Assert.Single(await _schedulerWorkQueue.ListAllAsync("wfexec-1")); Assert.Empty(activator.Requests);
            Assert.Empty(_checkpointWriter.ListCommits()); Assert.NotNull(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
            Assert.Empty(await poison.ListAsync("wfexec-1"));
        }
        var executable = NewTypedExecutable("original");
        if (missingMaterial) await _executableStore.SaveAsync(executable);
        else await _activityStateStore.SaveAsync(NewTypedTriggerState(executable.RootActivity.ActivityContract!, Descriptor<ApprovalTrigger>()));
        clock.Advance(TimeSpan.FromSeconds(5));
        await using (var provider = NewProvider(activator))
            await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1));
        Assert.Single(activator.Requests); Assert.Empty(await poison.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task Exact_queued_rearm_between_preflight_and_initial_claim_preserves_the_replacement()
    {
        var (work, clock) = await PrepareExactResume(); var activator = new StatefulResumeActivator(); var observer = new RecordingBookmarkObserver();
        var original = (await _bookmarkStateStore.ObserveAsync("wfexec-1", "bookmark-1"))!.Generation;
        var cut = new AdmissionCut(_checkpointWriter, null) { BeforeClaim = async () => { await _bookmarkStateStore.DeleteAsync("wfexec-1", "bookmark-1"); await SaveBookmarkAsync(); } };
        var poison = new InMemoryWorkflowSchedulerPoisonStore(); await _schedulerWorkQueue.EnqueueAsync(work);
        await using var provider = NewProvider(activator, observer, services => services.AddSingleton<IRuntimeCheckpointCommitStore>(cut));
        await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1));
        Assert.Empty(activator.Requests); Assert.Empty(observer.Consumed); Assert.Empty(_checkpointWriter.ListCommits());
        Assert.NotEqual(original, (await _bookmarkStateStore.ObserveAsync("wfexec-1", "bookmark-1"))!.Generation);
        Assert.Empty(await poison.ListAsync("wfexec-1"));
    }

    [Fact]
    public async Task Exact_queued_redelivery_after_claim_uses_consumed_trigger_without_consuming_a_rearmed_wait()
    {
        var (work, clock) = await PrepareExactResume(); var activator = new UnavailableThenCompleteActivator(); var observer = new RecordingBookmarkObserver();
        var poison = new InMemoryWorkflowSchedulerPoisonStore(); await _schedulerWorkQueue.EnqueueAsync(work);
        await using (var provider = NewProvider(activator, observer))
        {
            Assert.True((await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1))).StoppedOnDeferral);
            Assert.Single(activator.Requests); Assert.Null(await _bookmarkStateStore.FindAsync("wfexec-1", "bookmark-1"));
        }
        await SaveBookmarkAsync(); var replacement = (await _bookmarkStateStore.ObserveAsync("wfexec-1", "bookmark-1"))!.Generation;
        clock.Advance(TimeSpan.FromSeconds(5));
        await using (var provider = NewProvider(activator, observer))
            await ExactDrainer(provider, clock, poison).DrainAsync(new("wfexec-1", maxWorkItems: 1));
        Assert.Equal(2, activator.Requests.Count); Assert.Equal(replacement, (await _bookmarkStateStore.ObserveAsync("wfexec-1", "bookmark-1"))!.Generation);
        Assert.Single(_checkpointWriter.ListCommits(), record => record.Commit.ExpectedBookmarkResume is not null);
        Assert.Single(observer.Consumed); Assert.Empty(await poison.ListAsync("wfexec-1"));
    }

    // Explicit transparent provider test adapter: qualifies only this exact observation wrapper over its physical
    // bookmark store. The injected cuts exercise scheduler classification; EF SQL cuts have separate provider tests.
    private sealed class ObservedBookmarks(IBookmarkStateStore inner) : IBookmarkStateStore
    {
        public IBookmarkStateStore Inner => inner; public bool FailObservation; public bool FailSibling;
        public ValueTask<BookmarkStateObservation?> ObserveAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
        { if (FailObservation) { FailObservation = false; throw new InvalidOperationException("Constructed contextual read boundary.", new IOException("Constructed unavailable observation.")); } return inner.ObserveAsync(workflowExecutionId, bookmarkId, cancellationToken); }
        public ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default) => inner.SaveAsync(state, cancellationToken);
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) => inner.DeleteAsync(workflowExecutionId, bookmarkId, cancellationToken);
        public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default)
        { if (FailSibling && bookmarkId == "bookmark-2") { FailSibling = false; throw new IOException("Constructed sibling read outage."); } return inner.FindAsync(workflowExecutionId, bookmarkId, cancellationToken); }
        public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(BookmarkStatePageQuery query, CancellationToken cancellationToken = default) => inner.ListPageAsync(query, cancellationToken);
    }
    private sealed class AdmissionCut(IRuntimeCheckpointCommitStore inner, ObservedBookmarks? books) : IRuntimeCheckpointCommitStore
    {
        public bool FailCheckpoint; public bool FailQualification; public Func<Task>? BeforeClaim;
        public bool SupportsExactBookmarkResume => inner.SupportsExactBookmarkResume;
        public bool CanCommitExactBookmarkResume(IBookmarkStateStore bookmarks, IWorkflowExecutionStateStore executions, IActivityExecutionStateStore activities)
        { if (FailQualification) { FailQualification = false; throw new IOException("Constructed capability read outage."); } return inner.CanCommitExactBookmarkResume(books is not null && ReferenceEquals(books, bookmarks) ? books.Inner : bookmarks, executions, activities); }
        public async ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            if (commit.ExpectedBookmarkResume is not null)
            {
                if (FailCheckpoint) { FailCheckpoint = false; throw new RuntimeSchedulerWorkUnavailableException("test.unavailable", "Constructed admission outage.", new IOException("Constructed marker read outage.")); }
                if (BeforeClaim is { } cut) { BeforeClaim = null; await cut(); }
            }
            return await inner.CommitAsync(commit, decision, cancellationToken);
        }
    }
}

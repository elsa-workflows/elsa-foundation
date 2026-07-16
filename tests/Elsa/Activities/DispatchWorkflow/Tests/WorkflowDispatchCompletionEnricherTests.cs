using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed class WorkflowDispatchCompletionEnricherTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompletedWait_AppendsOneSafeDeterministicResumeIntent()
    {
        var outputSource = new StubOutputSource([
            new RuntimeWorkflowOutput("disclosed", JsonSerializer.SerializeToElement(42), false, null, new("clr", "System.Int32", null), Now),
            new RuntimeWorkflowOutput("secret", null, true, "sensitive", new("clr", "System.String", null), Now)
        ]);
        var enricher = NewEnricher(outputSource, new NullLookupStore());

        var enriched = await enricher.EnrichAsync(NewCompletedCommit());

        var intent = Assert.Single(enriched.PostCommitIntents);
        var identity = Identity();
        Assert.Equal(identity.ParentResumeIntentId, intent.IntentId);
        Assert.Equal(DispatchWorkflowConstants.ResumeParentIntentKind, intent.Kind);
        Assert.Equal(identity.ParentResumeIdempotencyKey, intent.IdempotencyKey);
        var payload = intent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(JsonOptions())!;
        Assert.Equal(identity.WaitBookmarkId, payload.BookmarkId);
        Assert.Equal(identity.WaitStimulusHash, payload.StimulusHash);
        var outputs = payload.Result.Outputs.OrderBy(x => x.Name).ToArray();
        Assert.Equal(2, outputs.Length);
        Assert.Equal(42, outputs[0].Value!.Value.GetInt32());
        Assert.True(outputs[1].IsRedacted);
        Assert.Null(outputs[1].Value);
        Assert.DoesNotContain("sensitive", intent.Payload.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replay_ReusesExactCommittedIntentWithoutRecapturingOutputs()
    {
        var first = await NewEnricher(
            new StubOutputSource([new RuntimeWorkflowOutput("value", JsonSerializer.SerializeToElement("first"), false, null, new("json", "string", null), Now)]),
            new NullLookupStore()).EnrichAsync(NewCompletedCommit());
        var committedIntent = Assert.Single(first.PostCommitIntents);
        var committedItem = new RuntimePostCommitOutboxItem(
            Identity().ParentResumeOutboxItemId(first.CommitId),
            committedIntent,
            RuntimePostCommitOutboxStatus.Delivered,
            committedIntent.RecordedAt,
            availableAt: null,
            deliveredAt: Now.AddSeconds(1));
        var throwingSource = new ThrowingOutputSource();
        var replay = await NewEnricher(
            throwingSource,
            new FixedLookupStore(committedItem)).EnrichAsync(NewCompletedCommit());

        Assert.Same(committedIntent, Assert.Single(replay.PostCommitIntents));
        Assert.Equal(0, throwingSource.ReadCount);
    }

    [Fact]
    public async Task UncommittedSameIdIntentWithForeignOutput_IsRejectedBeforePersistence()
    {
        var identity = Identity();
        var unsafeResult = new DispatchWorkflowResult(
            identity.ChildWorkflowExecutionId,
            WorkflowDispatchStatus.Completed,
            [new DispatchWorkflowOutput("secret", JsonSerializer.SerializeToElement("raw-secret"), "System.String", false)]);
        var unsafePayload = new WorkflowDispatchParentResumePayload(
            identity.DispatchId,
            "parent-resume",
            "activity-resume",
            identity.ChildWorkflowExecutionId,
            identity.WaitBookmarkId,
            DispatchWorkflowConstants.WaitStimulusType,
            identity.WaitStimulusHash,
            unsafeResult);
        var foreignIntent = new RuntimePostCommitIntent(
            identity.ParentResumeIntentId,
            "parent-resume",
            DispatchWorkflowConstants.ResumeParentIntentKind,
            Now,
            "activity-resume",
            identity.ParentResumeIdempotencyKey,
            JsonSerializer.SerializeToElement(unsafePayload, JsonOptions()),
            new Dictionary<string, string>
            {
                ["runtime.dispatchId"] = identity.DispatchId,
                ["runtime.childWorkflowExecutionId"] = identity.ChildWorkflowExecutionId
            });
        var commit = NewCompletedCommit() with { PostCommitIntents = [foreignIntent] };
        var safeSource = new StubOutputSource([
            new RuntimeWorkflowOutput("secret", null, true, "sensitive", new("clr", "System.String", null), Now)
        ]);
        var enricher = NewEnricher(safeSource, new NullLookupStore());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => enricher.EnrichAsync(commit).AsTask());

        Assert.Contains("policy-safe completed result", exception.Message, StringComparison.Ordinal);
        Assert.Contains("raw-secret", foreignIntent.Payload!.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Faulted, WorkflowDispatchStatus.Faulted)]
    [InlineData(WorkflowExecutionStatus.Cancelled, WorkflowDispatchStatus.Cancelled)]
    public async Task NonCompletedChild_DoesNotAppendParentResumeIntent(
        WorkflowExecutionStatus childStatus,
        WorkflowDispatchStatus dispatchStatus)
    {
        var outputSource = new ThrowingOutputSource();
        var enricher = NewEnricher(outputSource, new NullLookupStore());

        var enriched = await enricher.EnrichAsync(NewTerminalCommit(childStatus, dispatchStatus));

        Assert.Empty(enriched.PostCommitIntents);
        Assert.Equal(0, outputSource.ReadCount);
    }

    [Fact]
    public async Task CompletedChildWithoutLinkedDispatch_AppendsNoIntentOrReadsOutputs()
    {
        var outputSource = new ThrowingOutputSource();
        var commit = NewCompletedCommit() with
        {
            StateChanges = NewCompletedCommit().StateChanges.WithWorkflowDispatches([])
        };

        var enriched = await NewEnricher(outputSource, new NullLookupStore()).EnrichAsync(commit);

        Assert.Empty(enriched.PostCommitIntents);
        Assert.Empty(enriched.StateChanges.WorkflowDispatches);
        Assert.Equal(0, outputSource.ReadCount);
    }

    [Fact]
    public async Task CompletionEnrichment_DeclaresASeparatePostLifecyclePhase()
    {
        var completion = NewEnricher(new StubOutputSource([]), new NullLookupStore());

        Assert.True(completion.Order > ((IRuntimeCheckpointCommitEnricher)new BasePhaseEnricher()).Order);
    }

    [Fact]
    public async Task EquivalentUncommittedIntent_WithReorderedMetadata_IsAccepted()
    {
        var outputs = new StubOutputSource([]);
        var canonical = Assert.Single((await NewEnricher(outputs, new NullLookupStore())
            .EnrichAsync(NewCompletedCommit())).PostCommitIntents);
        var reordered = CopyIntent(canonical, metadata: new Dictionary<string, string>
        {
            ["runtime.childWorkflowExecutionId"] = Identity().ChildWorkflowExecutionId,
            ["runtime.dispatchId"] = Identity().DispatchId
        });
        var commit = NewCompletedCommit() with { PostCommitIntents = [reordered] };

        var enriched = await NewEnricher(outputs, new NullLookupStore()).EnrichAsync(commit);

        Assert.Same(reordered, Assert.Single(enriched.PostCommitIntents));
    }

    [Fact]
    public async Task ConflictingCommittedAndUncommittedIntents_AreRejected()
    {
        var outputs = new StubOutputSource([]);
        var canonical = Assert.Single((await NewEnricher(outputs, new NullLookupStore())
            .EnrichAsync(NewCompletedCommit())).PostCommitIntents);
        var conflicting = CopyIntent(canonical, recordedAt: canonical.RecordedAt.AddSeconds(1));
        var committedItem = new RuntimePostCommitOutboxItem(
            Identity().ParentResumeOutboxItemId(NewCompletedCommit().CommitId),
            conflicting,
            RuntimePostCommitOutboxStatus.Pending,
            conflicting.RecordedAt,
            availableAt: null);
        var commit = NewCompletedCommit() with { PostCommitIntents = [canonical] };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewEnricher(outputs, new FixedLookupStore(committedItem)).EnrichAsync(commit).AsTask());

        Assert.Contains("committed outbox item", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidCommittedIntent_IsRejectedByStableFieldValidation()
    {
        var canonical = Assert.Single((await NewEnricher(new StubOutputSource([]), new NullLookupStore())
            .EnrichAsync(NewCompletedCommit())).PostCommitIntents);
        var invalid = CopyIntent(canonical, recordedAt: canonical.RecordedAt.AddSeconds(1));
        var committedItem = new RuntimePostCommitOutboxItem(
            Identity().ParentResumeOutboxItemId(NewCompletedCommit().CommitId),
            invalid,
            RuntimePostCommitOutboxStatus.Delivered,
            invalid.RecordedAt,
            availableAt: null,
            deliveredAt: invalid.RecordedAt);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewEnricher(new ThrowingOutputSource(), new FixedLookupStore(committedItem))
                .EnrichAsync(NewCompletedCommit()).AsTask());

        Assert.Contains("conflicts with dispatch", exception.Message, StringComparison.Ordinal);
    }

    private static RuntimeCheckpointCommit NewCompletedCommit() =>
        NewTerminalCommit(WorkflowExecutionStatus.Completed, WorkflowDispatchStatus.Completed);

    private static RuntimeCheckpointCommit NewTerminalCommit(
        WorkflowExecutionStatus childStatus,
        WorkflowDispatchStatus dispatchStatus)
    {
        var dispatch = NewDispatch().TransitionTo(dispatchStatus, Now);
        var execution = new WorkflowExecutionState(
            Identity().ChildWorkflowExecutionId,
            dispatch.ChildExecutable,
            childStatus,
            null,
            Now.AddMinutes(-1),
            Now.AddMinutes(-1),
            Now,
            Now,
            null,
            "parent-resume",
            null,
            new Dictionary<string, string>());
        return new RuntimeCheckpointCommit(
            "commit-child-completed",
            new RuntimeCheckpoint("checkpoint-child-completed", "Completed", execution.WorkflowExecutionId, Now, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(execution.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, execution, new Dictionary<string, string>()),
                null,
                [], [], [], [], [],
                [new RuntimeStateChange<WorkflowDispatchRecord>(dispatch.DispatchId, RuntimeStateChangeOperation.Upsert, dispatch, new Dictionary<string, string>())]),
            [],
            new Dictionary<string, string>());
    }

    private static WorkflowDispatchRecord NewDispatch()
    {
        var identity = Identity();
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            "parent-resume",
            "activity-resume",
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-child", "definition-child", "version-child", "1.0.0", "sha256:child"),
            new WorkflowExecutableSourceProvenance("source-child", "WorkflowDefinitionVersion", "version-child", "1.0.0", "definition-child", "version-child", "1.0.0", "publication-child", "slot-child"),
            WorkflowDispatchMode.WaitForCompletion,
            WorkflowDispatchStatus.Started,
            null,
            null,
            new WorkflowExecutionPartition("partition-1"),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot("parent-resume", "initiator"),
            [],
            Now.AddMinutes(-2),
            Now.AddMinutes(-1));
    }

    private static WorkflowDispatchIdentity Identity() => new("parent-resume", "activity-resume");
    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);

    private static WorkflowDispatchCompletionEnricher NewEnricher(
        IWorkflowOutputSource outputSource,
        IPostCommitOutboxLookupStore lookupStore) =>
        new(outputSource, lookupStore);

    private static RuntimePostCommitIntent CopyIntent(
        RuntimePostCommitIntent source,
        DateTimeOffset? recordedAt = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            source.IntentId,
            source.WorkflowExecutionId,
            source.Kind,
            recordedAt ?? source.RecordedAt,
            source.ActivityExecutionId,
            source.IdempotencyKey,
            source.Payload,
            metadata ?? source.Metadata,
            source.DependsOnWaitRegistrationId,
            source.WaitFailurePolicy);

    private sealed class StubOutputSource(IReadOnlyCollection<RuntimeWorkflowOutput> outputs) : IWorkflowOutputSource
    {
        public ValueTask<IReadOnlyCollection<RuntimeWorkflowOutput>> ReadAsync(string workflowExecutionId, IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(outputs);
    }

    private sealed class ThrowingOutputSource : IWorkflowOutputSource
    {
        public int ReadCount { get; private set; }
        public ValueTask<IReadOnlyCollection<RuntimeWorkflowOutput>> ReadAsync(string workflowExecutionId, IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            throw new InvalidOperationException("must not recapture");
        }
    }

    private sealed class NullLookupStore : IPostCommitOutboxLookupStore
    {
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default) => ValueTask.FromResult<RuntimePostCommitOutboxItem?>(null);
    }

    private sealed class FixedLookupStore(RuntimePostCommitOutboxItem item) : IPostCommitOutboxLookupStore
    {
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimePostCommitOutboxItem?>(StringComparer.Ordinal.Equals(outboxItemId, item.OutboxItemId) ? item : null);
    }

    private sealed class BasePhaseEnricher : IRuntimeCheckpointCommitEnricher
    {
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(
            RuntimeCheckpointCommit commit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(commit);
    }
}

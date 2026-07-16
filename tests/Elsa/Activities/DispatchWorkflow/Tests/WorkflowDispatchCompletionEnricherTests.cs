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

    [Theory]
    [InlineData(WorkflowExecutionStatus.Faulted, WorkflowDispatchStatus.Faulted)]
    [InlineData(WorkflowExecutionStatus.Cancelled, WorkflowDispatchStatus.Cancelled)]
    public async Task NonSuccessWait_AppendsSafeDeterministicResumeIntentWithoutReadingOutputs(
        WorkflowExecutionStatus childStatus,
        WorkflowDispatchStatus dispatchStatus)
    {
        var incidents = new StubIncidentStore([
            NewIncident("incident-z", "secret failure type z", "secret message z"),
            NewIncident("incident-a", "secret failure type a", "secret message a")
        ]);
        var outputSource = new ThrowingOutputSource();

        var enriched = await NewEnricher(outputSource, new NullLookupStore(), incidents)
            .EnrichAsync(NewTerminalCommit(childStatus, dispatchStatus));

        var intent = Assert.Single(enriched.PostCommitIntents);
        var payload = intent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(JsonOptions())!;
        Assert.Equal(dispatchStatus, payload.Result.Status);
        Assert.Empty(payload.Result.Outputs);
        Assert.Equal(0, outputSource.ReadCount);
        Assert.Equal(dispatchStatus == WorkflowDispatchStatus.Faulted ? 1 : 0, incidents.ListBlockingCount);
        Assert.Equal(DispatchWorkflowDiagnostics.Category, payload.Result.DiagnosticMetadata[DispatchWorkflowDiagnostics.CategoryKey]);
        Assert.Equal(
            dispatchStatus == WorkflowDispatchStatus.Faulted ? DispatchWorkflowDiagnostics.FaultedCode : DispatchWorkflowDiagnostics.CancelledCode,
            payload.Result.DiagnosticMetadata[DispatchWorkflowDiagnostics.CodeKey]);
        Assert.Equal(
            dispatchStatus == WorkflowDispatchStatus.Faulted
                ? DispatchWorkflowDiagnostics.FaultedSummary
                : DispatchWorkflowDiagnostics.CancelledSummary,
            payload.Result.DiagnosticMetadata[DispatchWorkflowDiagnostics.SummaryKey]);

        if (dispatchStatus == WorkflowDispatchStatus.Faulted)
        {
            Assert.Equal("2", payload.Result.DiagnosticMetadata[DispatchWorkflowDiagnostics.IncidentCountKey]);
            Assert.Equal("false", payload.Result.DiagnosticMetadata[DispatchWorkflowDiagnostics.IncidentIdsTruncatedKey]);
            Assert.Equal("incident-a", payload.Result.DiagnosticMetadata["incidentId.000"]);
            Assert.Equal("incident-z", payload.Result.DiagnosticMetadata["incidentId.001"]);
        }
        else
            Assert.Equal(3, payload.Result.DiagnosticMetadata.Count);

        var rawPayload = intent.Payload.Value.GetRawText();
        Assert.DoesNotContain("secret", rawPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failure type", rawPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", rawPayload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FaultResult_DeduplicatesSortsAndTruncatesIncidentIdsWithInvariantMetadata()
    {
        var incidents = Enumerable.Range(0, DispatchWorkflowDiagnostics.MaximumIncidentIds + 3)
            .Reverse()
            .Select(index => NewIncident($"incident-{index:D3}", "secret-type", "secret-message"))
            .Append(NewIncident("incident-001", "duplicate-secret-type", "duplicate-secret-message"))
            .ToArray();

        var enriched = await NewEnricher(
                new ThrowingOutputSource(),
                new NullLookupStore(),
                new StubIncidentStore(incidents))
            .EnrichAsync(NewTerminalCommit(WorkflowExecutionStatus.Faulted, WorkflowDispatchStatus.Faulted));

        var payload = Assert.Single(enriched.PostCommitIntents).Payload!.Value;
        var result = payload.Deserialize<WorkflowDispatchParentResumePayload>(JsonOptions())!.Result;
        Assert.Equal("35", result.DiagnosticMetadata[DispatchWorkflowDiagnostics.IncidentCountKey]);
        Assert.Equal("true", result.DiagnosticMetadata[DispatchWorkflowDiagnostics.IncidentIdsTruncatedKey]);
        Assert.Equal("incident-000", result.DiagnosticMetadata["incidentId.000"]);
        Assert.Equal("incident-031", result.DiagnosticMetadata["incidentId.031"]);
        Assert.DoesNotContain("incidentId.032", result.DiagnosticMetadata.Keys);
        Assert.DoesNotContain("secret", payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FaultReplay_ReusesExactCommittedIntentWithoutRecapturingIncidentsOrOutputs()
    {
        var first = await NewEnricher(
                new ThrowingOutputSource(),
                new NullLookupStore(),
                new StubIncidentStore([NewIncident("incident-a", "secret-type", "secret-message")]))
            .EnrichAsync(NewTerminalCommit(WorkflowExecutionStatus.Faulted, WorkflowDispatchStatus.Faulted));
        var committedIntent = Assert.Single(first.PostCommitIntents);
        var committedItem = new RuntimePostCommitOutboxItem(
            Identity().ParentResumeOutboxItemId(first.CommitId),
            committedIntent,
            RuntimePostCommitOutboxStatus.Delivered,
            committedIntent.RecordedAt,
            availableAt: null,
            deliveredAt: Now.AddSeconds(1));
        var outputSource = new ThrowingOutputSource();
        var incidentStore = new ThrowingIncidentStore();

        var replay = await NewEnricher(outputSource, new FixedLookupStore(committedItem), incidentStore)
            .EnrichAsync(NewTerminalCommit(WorkflowExecutionStatus.Faulted, WorkflowDispatchStatus.Faulted));

        Assert.Same(committedIntent, Assert.Single(replay.PostCommitIntents));
        Assert.Equal(0, outputSource.ReadCount);
        Assert.Equal(0, incidentStore.ListBlockingCount);
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

        Assert.Contains("policy-safe terminal result", exception.Message, StringComparison.Ordinal);
        Assert.Contains("raw-secret", foreignIntent.Payload!.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MismatchedTerminalProjection_DoesNotAppendParentResumeIntent()
    {
        var outputSource = new ThrowingOutputSource();
        var incidents = new ThrowingIncidentStore();
        var enricher = NewEnricher(outputSource, new NullLookupStore(), incidents);

        var enriched = await enricher.EnrichAsync(NewTerminalCommit(
            WorkflowExecutionStatus.Faulted,
            WorkflowDispatchStatus.Cancelled));

        Assert.Empty(enriched.PostCommitIntents);
        Assert.Equal(0, outputSource.ReadCount);
        Assert.Equal(0, incidents.ListBlockingCount);
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

    [Fact]
    public async Task CommittedIntentWithMismatchedTerminalStatus_IsRejected()
    {
        var canonical = Assert.Single((await NewEnricher(new StubOutputSource([]), new NullLookupStore())
            .EnrichAsync(NewCompletedCommit())).PostCommitIntents);
        var identity = Identity();
        var faultedResult = new DispatchWorkflowResult(
            identity.ChildWorkflowExecutionId,
            WorkflowDispatchStatus.Faulted,
            diagnosticMetadata: new Dictionary<string, string>
            {
                [DispatchWorkflowDiagnostics.CodeKey] = DispatchWorkflowDiagnostics.FaultedCode,
                [DispatchWorkflowDiagnostics.CategoryKey] = DispatchWorkflowDiagnostics.Category,
                [DispatchWorkflowDiagnostics.SummaryKey] = DispatchWorkflowDiagnostics.FaultedSummary,
                [DispatchWorkflowDiagnostics.IncidentCountKey] = "0",
                [DispatchWorkflowDiagnostics.IncidentIdsTruncatedKey] = "false"
            });
        var mismatchedPayload = new WorkflowDispatchParentResumePayload(
            identity.DispatchId,
            "parent-resume",
            "activity-resume",
            identity.ChildWorkflowExecutionId,
            identity.WaitBookmarkId,
            DispatchWorkflowConstants.WaitStimulusType,
            identity.WaitStimulusHash,
            faultedResult);
        var mismatched = CopyIntent(
            canonical,
            payload: JsonSerializer.SerializeToElement(mismatchedPayload, JsonOptions()));
        var committedItem = new RuntimePostCommitOutboxItem(
            identity.ParentResumeOutboxItemId(NewCompletedCommit().CommitId),
            mismatched,
            RuntimePostCommitOutboxStatus.Delivered,
            mismatched.RecordedAt,
            availableAt: null,
            deliveredAt: mismatched.RecordedAt);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewEnricher(new ThrowingOutputSource(), new FixedLookupStore(committedItem))
                .EnrichAsync(NewCompletedCommit()).AsTask());

        Assert.Contains("payload conflicts with dispatch", exception.Message, StringComparison.Ordinal);
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
        IPostCommitOutboxLookupStore lookupStore,
        IIncidentStateStore? incidentStore = null) =>
        new(outputSource, lookupStore, incidentStore ?? new StubIncidentStore([]));

    private static IncidentState NewIncident(string incidentId, string failureType, string message) =>
        new(
            incidentId,
            Identity().ChildWorkflowExecutionId,
            "activity-child",
            "node-child",
            IncidentSeverity.Error,
            IncidentStatus.Blocking,
            IncidentResolutionAction.FaultWorkflow,
            failureType,
            message,
            Now,
            null,
            new Dictionary<string, string> { ["unsafe"] = "secret metadata" });

    private static RuntimePostCommitIntent CopyIntent(
        RuntimePostCommitIntent source,
        DateTimeOffset? recordedAt = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        JsonElement? payload = null) =>
        new(
            source.IntentId,
            source.WorkflowExecutionId,
            source.Kind,
            recordedAt ?? source.RecordedAt,
            source.ActivityExecutionId,
            source.IdempotencyKey,
            payload ?? source.Payload,
            metadata ?? source.Metadata,
            source.DependsOnWaitRegistrationId,
            source.WaitFailurePolicy);

    private sealed class StubOutputSource(IReadOnlyCollection<RuntimeWorkflowOutput> outputs) : IWorkflowOutputSource
    {
        public int ReadCount { get; private set; }

        public ValueTask<IReadOnlyCollection<RuntimeWorkflowOutput>> ReadAsync(string workflowExecutionId, IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Read());

        private IReadOnlyCollection<RuntimeWorkflowOutput> Read()
        {
            ReadCount++;
            return outputs;
        }
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

    private sealed class StubIncidentStore(IReadOnlyCollection<IncidentState> incidents) : IIncidentStateStore
    {
        public int ListBlockingCount { get; private set; }

        public ValueTask<bool> TryAddAsync(IncidentState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IncidentState> SaveAsync(IncidentState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IncidentState?> FindAsync(string workflowExecutionId, string incidentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            ListBlockingCount++;
            return ValueTask.FromResult(incidents);
        }
    }

    private sealed class ThrowingIncidentStore : IIncidentStateStore
    {
        public int ListBlockingCount { get; private set; }

        public ValueTask<bool> TryAddAsync(IncidentState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IncidentState> SaveAsync(IncidentState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IncidentState?> FindAsync(string workflowExecutionId, string incidentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyCollection<IncidentState>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyCollection<IncidentState>> ListBlockingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            ListBlockingCount++;
            throw new InvalidOperationException("must not recapture incidents");
        }
    }

    private sealed class BasePhaseEnricher : IRuntimeCheckpointCommitEnricher
    {
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(
            RuntimeCheckpointCommit commit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(commit);
    }
}

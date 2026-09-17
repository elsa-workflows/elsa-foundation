using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Microsoft.Extensions.Time.Testing;
using Elsa.Testing;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimePostCommitOutboxProcessorTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Processor_DispatchesDeliverableItemsAndRecordsDelivered()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher();
        var processor = NewProcessor(store, dispatcher, _now.AddSeconds(5));

        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-2", "intent-2", "wfexec-1", availableAt: _now.AddSeconds(-1)));
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(-2)));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        Assert.Equal(2, result.AttemptedCount);
        Assert.Equal(2, result.DeliveredCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(["intent-1", "intent-2"], dispatcher.Intents.Select(intent => intent.IntentId));
        Assert.Equal([RuntimePostCommitOutboxStatus.Delivered, RuntimePostCommitOutboxStatus.Delivered], result.Items.Select(item => item.RequestedDeliveryResultStatus));
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddMinutes(1), limit: 10)));
    }

    [Fact]
    public async Task Processor_RecordsRetryableFailureWhenDispatchFails()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-1", failure: new InvalidOperationException("Dispatch failed."));
        var processor = NewProcessor(store, dispatcher, _now);

        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1"));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, item.RequestedDeliveryResultStatus);
        Assert.Equal("System.InvalidOperationException: Dispatch failed.", item.FailureMessage);
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(5), limit: 10)));

        var retryable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(11), limit: 10));
        var retryableItem = Assert.Single(retryable);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, retryableItem.Status);
        Assert.Equal(1, retryableItem.DeliveryAttemptCount);
        Assert.Equal("System.InvalidOperationException: Dispatch failed.", retryableItem.LastFailureMessage);
    }

    [Fact]
    public async Task Processor_ResultReportsRequestedFailedStatusWhenStoreNormalizesToFinal()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-1", failure: new InvalidOperationException("Dispatch failed."));
        var processor = NewProcessor(store, dispatcher, _now);

        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", retryPolicy: new RuntimePostCommitRetryPolicy(1, TimeSpan.FromSeconds(10))));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        var processed = Assert.Single(result.Items);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, processed.RequestedDeliveryResultStatus);
        Assert.Equal(1, result.FailedCount);
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(11), limit: 10)));
    }

    [Fact]
    public async Task Processor_PermanentDeliveryFailureBypassesRemainingRetriesWithSafeStatusAndLogs()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var logger = new RecordingLogger<RuntimePostCommitOutboxProcessor>();
        var failure = new RuntimePostCommitDeliveryException(
            PostCommitFailureKind.Permanent,
            "child-start-delivery-failed",
            "The child workflow could not be started.",
            new InvalidOperationException("provider-secret stack-secret"));
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-1", failure: failure);
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            dispatcher,
            new FakeTimeProvider(_now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            workflowDispatchStore: null,
            logger);
        await store.AddPendingForTestingAsync(NewOutboxItem(
            "outbox-1",
            "intent-1",
            "wfexec-1",
            retryPolicy: new RuntimePostCommitRetryPolicy(4, TimeSpan.FromSeconds(10)),
            kind: "Elsa.Activities.DispatchWorkflow.StartChild"));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

        var processed = Assert.Single(result.Items);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, processed.RequestedDeliveryResultStatus);
        Assert.Equal("The child workflow could not be started.", processed.FailureMessage);
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddYears(1), 10)));
        Assert.Collection(
            logger.Entries,
            attempt =>
            {
                Assert.Equal(new EventId(68101, "RuntimePostCommitDeliveryAttemptFailed"), attempt.EventId);
                AssertCarriesFailure(attempt, failure);
            },
            final =>
            {
                Assert.Equal(new EventId(68103, "RuntimePostCommitDeliveryFailedFinal"), final.EventId);
                Assert.Equal("child-start-delivery-failed", final.Fields["FailureCode"]);
                Assert.Equal(1, final.Fields["DeliveryAttemptCount"]);
                Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, final.Fields["EffectiveStatus"]);
                AssertCarriesFailure(final, failure);
            });
    }

    [Fact]
    public async Task Processor_TransientDeliveryFailureLogsAttemptAndPositiveRetryScheduleSafely()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var logger = new RecordingLogger<RuntimePostCommitOutboxProcessor>();
        var failure = new RuntimePostCommitDeliveryException(
            PostCommitFailureKind.Transient,
            "child-start-delivery-failed",
            "The child workflow could not be started.",
            new InvalidOperationException("provider-secret stack-secret"));
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-1", failure: failure);
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            dispatcher,
            new FakeTimeProvider(_now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            workflowDispatchStore: null,
            logger);
        await store.AddPendingForTestingAsync(NewOutboxItem(
            "outbox-1",
            "intent-1",
            "wfexec-1",
            retryPolicy: new RuntimePostCommitRetryPolicy(4, TimeSpan.FromSeconds(10)),
            kind: "Elsa.Activities.DispatchWorkflow.StartChild"));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

        var processed = Assert.Single(result.Items);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, processed.RequestedDeliveryResultStatus);
        Assert.Equal("The child workflow could not be started.", processed.FailureMessage);
        var retry = Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(10), 10)));
        Assert.Equal(1, retry.DeliveryAttemptCount);
        Assert.Equal(_now.AddSeconds(10), retry.AvailableAt);
        Assert.Collection(
            logger.Entries,
            attempt =>
            {
                Assert.Equal(new EventId(68101, "RuntimePostCommitDeliveryAttemptFailed"), attempt.EventId);
                AssertCarriesFailure(attempt, failure);
            },
            scheduled =>
            {
                Assert.Equal(new EventId(68102, "RuntimePostCommitRetryScheduled"), scheduled.EventId);
                Assert.Equal(_now.AddSeconds(10), scheduled.Fields["NextAvailableAt"]);
                AssertCarriesFailure(scheduled, failure);
            });
    }

    [Fact]
    public async Task Processor_ReclaimsExpiredAttemptAndItsHigherFenceRejectsTheStaleWriter()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher();
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1"));
        var staleClaim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "crashed-worker",
            _now,
            TimeSpan.FromMinutes(1),
            10)));
        var processor = NewProcessor(store, dispatcher, _now.AddMinutes(1).AddSeconds(1));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, Assert.Single(result.Items).RequestedDeliveryResultStatus);
        Assert.Equal(["intent-1"], dispatcher.Intents.Select(intent => intent.IntentId));
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => store.RecordDeliveryResultAsync(
            staleClaim,
            new RuntimePostCommitOutboxDeliveryResult(
                "outbox-1",
                RuntimePostCommitOutboxStatus.FailedRetryable,
                _now.AddMinutes(1).AddSeconds(2),
                "stale failure")).AsTask());
    }

    [Fact]
    public async Task Processor_LogsPayloadSafeStructuredWarningForRecordedUnboundedRetry()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var logger = new RecordingLogger<RuntimePostCommitOutboxProcessor>();
        var failure = new InvalidOperationException("exception-secret stack-secret");
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-resume", failure: failure);
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            dispatcher,
            new FakeTimeProvider(_now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            workflowDispatchStore: null,
            logger);
        await store.AddPendingForTestingAsync(NewOutboxItem(
            "outbox-resume",
            "intent-resume",
            "wfexec-1",
            retryPolicy: RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(15)),
            kind: "Elsa.Activities.DispatchWorkflow.ResumeParent",
            metadata: new Dictionary<string, string> { ["runtime.dispatchId"] = "dispatch-safe" }));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

        const string safeFailureMessage = "Runtime post-commit intent delivery deferred pending acknowledgement.";
        var processed = Assert.Single(result.Items);
        Assert.Equal(safeFailureMessage, processed.FailureMessage);
        var persisted = Assert.Single(await store.GetDeliverableAsync(
            new RuntimePostCommitOutboxQuery(_now.AddSeconds(15), 10)));
        Assert.Equal(safeFailureMessage, persisted.LastFailureMessage);
        Assert.DoesNotContain("exception-secret", persisted.LastFailureMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack-secret", persisted.LastFailureMessage, StringComparison.OrdinalIgnoreCase);

        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal(new EventId(67901, "RuntimePostCommitRetryDeferred"), warning.EventId);
        // The cause reaches the log as the entry's exception; the persisted failure message above stays exception-free.
        Assert.Same(failure, warning.Exception);
        Assert.Equal("outbox-resume", warning.Fields["OutboxItemId"]);
        Assert.Equal("intent-resume", warning.Fields["IntentId"]);
        Assert.Equal("Elsa.Activities.DispatchWorkflow.ResumeParent", warning.Fields["IntentKind"]);
        Assert.Equal("dispatch-safe", warning.Fields["DispatchId"]);
        Assert.Equal(1, warning.Fields["DeliveryAttemptCount"]);
        Assert.Equal(_now.AddSeconds(15), warning.Fields["NextAvailableAt"]);
        var serializedWarning = string.Join(" ", warning.Fields.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.DoesNotContain("signal", serializedWarning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sent", serializedWarning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception-secret", serializedWarning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack-secret", serializedWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Processor_LogsExpectedDeferralWithoutTheException()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var logger = new RecordingLogger<RuntimePostCommitOutboxProcessor>();
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-resume", failure: new ExpectedDeferralException());
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            dispatcher,
            new FakeTimeProvider(_now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            workflowDispatchStore: null,
            logger);
        await store.AddPendingForTestingAsync(NewOutboxItem(
            "outbox-resume",
            "intent-resume",
            "wfexec-1",
            retryPolicy: RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(15)),
            kind: "Elsa.Activities.DispatchWorkflow.ResumeParent"));

        await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

        var warning = Assert.Single(logger.Entries);
        Assert.Equal(new EventId(67901, "RuntimePostCommitRetryDeferred"), warning.EventId);
        Assert.Null(warning.Exception);
    }

    private sealed class ExpectedDeferralException() : Exception("waiting"), IRuntimePostCommitDeferral;

    /// <summary>
    /// A permanent failure ends delivery under a retry-until-acknowledged policy too, so it is logged as final like any
    /// other final failure, never as a deferred retry that will not happen.
    /// </summary>
    [Fact]
    public async Task Processor_LogsAPermanentFailureUnderRetryUntilAcknowledgedAsFinal()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var logger = new RecordingLogger<RuntimePostCommitOutboxProcessor>();
        var failure = new RuntimePostCommitDeliveryException(
            PostCommitFailureKind.Permanent,
            "parent-resume-refused",
            "The parent workflow could not be resumed.",
            new InvalidOperationException("provider-secret stack-secret"));
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            new RecordingDispatcher(failOnIntentId: "intent-resume", failure: failure),
            new FakeTimeProvider(_now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            workflowDispatchStore: null,
            logger);
        await store.AddPendingForTestingAsync(NewOutboxItem(
            "outbox-resume",
            "intent-resume",
            "wfexec-1",
            retryPolicy: RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(15)),
            kind: "Elsa.Activities.DispatchWorkflow.ResumeParent"));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, Assert.Single(result.Items).RequestedDeliveryResultStatus);
        Assert.Collection(
            logger.Entries,
            attempt =>
            {
                Assert.Equal(new EventId(68101, "RuntimePostCommitDeliveryAttemptFailed"), attempt.EventId);
                AssertCarriesFailure(attempt, failure);
            },
            final =>
            {
                Assert.Equal(new EventId(68103, "RuntimePostCommitDeliveryFailedFinal"), final.EventId);
                Assert.Equal(nameof(PostCommitFailureKind.Permanent), final.Fields["FailureKind"]);
                Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, final.Fields["EffectiveStatus"]);
                AssertCarriesFailure(final, failure);
            });
    }

    [Fact]
    public async Task Processor_UnsupportedKindUsesExistingPolicySelectedFinalFailurePath()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var services = new ServiceCollection();
        services.AddScoped<IRuntimePostCommitIntentDispatcher, RuntimePostCommitIntentDispatcher>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            scope.ServiceProvider.GetRequiredService<IRuntimePostCommitIntentDispatcher>(),
            new FakeTimeProvider(_now));
        await store.AddPendingForTestingAsync(NewOutboxItem(
            "outbox-unsupported",
            "intent-unsupported",
            "wfexec-1",
            retryPolicy: RuntimePostCommitRetryPolicy.None,
            kind: "Unsupported.Intent"));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        var processed = Assert.Single(result.Items);
        Assert.Equal(1, result.FailedCount);
        Assert.NotEqual(RuntimePostCommitOutboxStatus.Delivered, processed.RequestedDeliveryResultStatus);
        Assert.Contains("Unsupported.Intent", processed.FailureMessage);
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddYears(1), limit: 10)));
    }

    [Fact]
    public async Task Processor_AtomicallyProjectsDispatchFailedWhenChildStartFailureBecomesFinal()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var dispatchStore = new InMemoryWorkflowDispatchStore(state);
        var store = new InMemoryRuntimeCheckpointCommitStore(state: state, workflowDispatchStore: dispatchStore);
        var dispatch = NewDispatchRecord();
        var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
        await dispatchStore.SaveAsync(dispatch);
        await store.AddPendingForTestingAsync(NewOutboxItem(
            "outbox-dispatch",
            identity.StartIntentId,
            "wfexec-1",
            retryPolicy: RuntimePostCommitRetryPolicy.None,
            kind: WorkflowDispatchLifecycle.StartChildIntentKind,
            metadata: new Dictionary<string, string> { ["runtime.dispatchId"] = dispatch.DispatchId }));
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            new RecordingDispatcher(identity.StartIntentId, new InvalidOperationException("start rejected")),
            new FakeTimeProvider(_now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            dispatchStore);

        await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddYears(1), 10)));
    }

    /// <summary>
    /// #1780: a store that finds the child already started persists the failed start as delivered and discards the
    /// DispatchFailed projection and its parent resume, so logging that incident and that resume, or a final failure, would
    /// report events that never happened. The delivery attempt itself did fail, so it is still logged, with the status the
    /// store persisted.
    /// </summary>
    [Theory]
    [InlineData(RuntimePostCommitOutboxClaimCompletionOutcome.Persisted, new[] { 68105, 68106, 68101, 68103 }, RuntimePostCommitOutboxStatus.FailedFinal)]
    [InlineData(RuntimePostCommitOutboxClaimCompletionOutcome.DeliveredOnChildEvidence, new[] { 68101 }, RuntimePostCommitOutboxStatus.Delivered)]
    public async Task Processor_LogsOnlyWhatTheStorePersisted(
        RuntimePostCommitOutboxClaimCompletionOutcome outcome,
        int[] expectedEventIds,
        RuntimePostCommitOutboxStatus persistedStatus)
    {
        var dispatch = NewDispatchRecord(WorkflowDispatchMode.WaitForCompletion);
        var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
        var inner = new InMemoryRuntimeCheckpointCommitStore();
        await inner.AddPendingForTestingAsync(NewOutboxItem(
            "outbox-start",
            identity.StartIntentId,
            dispatch.ParentWorkflowExecutionId,
            kind: WorkflowDispatchLifecycle.StartChildIntentKind,
            metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId }));
        var store = new ReportingClaimCompletionStore(inner, outcome);
        var logger = new RecordingLogger<RuntimePostCommitOutboxProcessor>();
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            new RecordingDispatcher(identity.StartIntentId, new RuntimePostCommitDeliveryException(
                PostCommitFailureKind.Permanent,
                "child-start-delivery-failed",
                "The child workflow could not be started.")),
            new FakeTimeProvider(_now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            workflowDispatchStore: null,
            [new WaitedDispatchFailureProjector(dispatch)],
            logger);

        await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

        var completion = Assert.Single(store.Completions);
        Assert.NotNull(completion.WorkflowDispatch);
        Assert.NotNull(completion.FollowUpOutboxItem);
        Assert.Equal(expectedEventIds, logger.Entries.Select(entry => entry.EventId.Id));
        Assert.All(
            logger.Entries.Where(entry => entry.EventId.Id is 68101 or 68103),
            entry => Assert.Equal(persistedStatus, entry.Fields["EffectiveStatus"]));
    }

    [Fact]
    public async Task Processor_UnsupportedKindWithDispatchMetadata_UsesSafeOutboxFailureWithoutMutatingDispatch()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var dispatchStore = new InMemoryWorkflowDispatchStore(state);
        var store = new InMemoryRuntimeCheckpointCommitStore(state: state, workflowDispatchStore: dispatchStore);
        var dispatch = NewDispatchRecord();
        var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
        await dispatchStore.SaveAsync(dispatch);
        await store.AddPendingForTestingAsync(NewOutboxItem(
            "outbox-unsupported-dispatch",
            identity.StartIntentId,
            dispatch.ParentWorkflowExecutionId,
            retryPolicy: RuntimePostCommitRetryPolicy.None,
            kind: "Unsupported.DispatchIntent",
            metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId }));
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            new RecordingDispatcher(identity.StartIntentId, new InvalidOperationException("unsupported failure")),
            new FakeTimeProvider(_now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            dispatchStore);

        await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10));

        Assert.Equal(WorkflowDispatchStatus.Pending, (await dispatchStore.FindAsync(dispatch.DispatchId))!.Status);
        var persisted = await store.FindAsync("outbox-unsupported-dispatch");
        Assert.NotNull(persisted);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, persisted.Status);
    }

    [Fact]
    public async Task Processor_UsesWorkflowExecutionFilterAndLimit()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher();
        var processor = NewProcessor(store, dispatcher, _now);

        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(-3)));
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-2", "intent-2", "wfexec-1", availableAt: _now.AddSeconds(-2)));
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-3", "intent-3", "wfexec-2", availableAt: _now.AddSeconds(-1)));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 1, workflowExecutionId: "wfexec-1"));

        var processed = Assert.Single(result.Items);
        Assert.Equal("outbox-1", processed.OutboxItemId);
        Assert.Equal(["intent-1"], dispatcher.Intents.Select(intent => intent.IntentId));

        var remainingWorkflowItems = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now, limit: 10, workflowExecutionId: "wfexec-1"));
        Assert.Equal(["outbox-2"], remainingWorkflowItems.Select(item => item.OutboxItemId));
    }

    [Fact]
    public async Task Processor_UsesIntentKindFilter()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher();
        var processor = NewProcessor(store, dispatcher, _now);

        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", kind: "DispatchSignal"));
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-2", "intent-2", "wfexec-1", kind: "OtherIntent"));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10, intentKind: "DispatchSignal"));

        var processed = Assert.Single(result.Items);
        Assert.Equal("outbox-1", processed.OutboxItemId);
        Assert.Equal(["intent-1"], dispatcher.Intents.Select(intent => intent.IntentId));

        var remaining = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now, limit: 10));
        Assert.Equal(["outbox-2"], remaining.Select(item => item.OutboxItemId));
    }

    [Fact]
    public async Task Processor_ReturnsEmptyResultWhenNoItemsAreDeliverable()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher();
        var processor = NewProcessor(store, dispatcher, _now);

        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", availableAt: _now.AddSeconds(1)));

        var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.AttemptedCount);
        Assert.Empty(dispatcher.Intents);

        var futureItems = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddSeconds(1), limit: 10));
        Assert.Equal(["outbox-1"], futureItems.Select(item => item.OutboxItemId));
    }

    [Fact]
    public async Task Processor_PreservesDispatchFailureWhenFailedResultRecordingFails()
    {
        var dispatchFailure = new InvalidOperationException("Dispatch failed.");
        var resultRecordingFailure = new InvalidOperationException("Result recording failed.");
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");
        var store = new ThrowingResultStore(item, resultRecordingFailure);
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-1", failure: dispatchFailure);
        var processor = NewProcessor(store, dispatcher, _now);

        var exception = await Assert.ThrowsAsync<OutboxProcessingException>(async () =>
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10)));

        Assert.Equal("outbox-1", exception.OutboxItemId);
        Assert.Equal("intent-1", exception.IntentId);
        Assert.Same(dispatchFailure, exception.InnerException);
        Assert.Same(resultRecordingFailure, exception.DeliveryResultRecordingException);
    }

    [Fact]
    public async Task Processor_PropagatesCancellationWhenFailedResultRecordingIsCanceled()
    {
        var dispatchFailure = new InvalidOperationException("Dispatch failed.");
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");
        var store = new ThrowingResultStore(item, new OperationCanceledException());
        var dispatcher = new RecordingDispatcher(failOnIntentId: "intent-1", failure: dispatchFailure);
        var processor = NewProcessor(store, dispatcher, _now);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10)));
    }

    [Fact]
    public async Task Processor_SurfacesDeliveredResultRecordingFailureAfterSuccessfulDispatch()
    {
        var resultRecordingFailure = new InvalidOperationException("Result recording failed.");
        var item = NewOutboxItem("outbox-1", "intent-1", "wfexec-1");
        var store = new ThrowingResultStore(item, resultRecordingFailure);
        var dispatcher = new RecordingDispatcher();
        var processor = NewProcessor(store, dispatcher, _now);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(limit: 10)));

        Assert.Same(resultRecordingFailure, exception);
        Assert.Equal(["intent-1"], dispatcher.Intents.Select(intent => intent.IntentId));
    }

    [Fact]
    public void ProcessRequest_RejectsInvalidArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimePostCommitOutboxProcessRequest(limit: 0));
        Assert.Throws<ArgumentException>(() => new RuntimePostCommitOutboxProcessRequest(limit: 10, workflowExecutionId: " "));
        Assert.Throws<ArgumentException>(() => new RuntimePostCommitOutboxProcessRequest(limit: 10, intentKind: " "));
    }

    [Fact]
    public async Task Processor_LiveDrainMarker_DeliversEnqueueSchedulerWorkInMemory_WithoutClaimRoundTrip()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var processor = NewLiveDrainProcessor(store, dispatcher, _now, liveDrain);
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope("wfexec-1")))
        {
            var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                limit: 10, workflowExecutionId: "wfexec-1", intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

            Assert.Equal(1, result.DeliveredCount);
        }

        Assert.Equal(["intent-1"], dispatcher.Intents.Select(intent => intent.IntentId));
        var delivered = await store.FindAsync("outbox-1");
        Assert.NotNull(delivered);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        Assert.Equal(1, delivered.DeliveryAttemptCount);
        // The fenced claim path bumps the fencing token to 1 (Pending -> Delivering -> Delivered). In-memory delivery
        // skips the claim, so a token still at 0 proves the durable claim round-trip was not taken.
        Assert.Equal(0, delivered.DeliveryFencingToken);
    }

    [Fact]
    public async Task Processor_CoalescingSessionActive_DoesNotEngageInMemoryFastPath()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var coalescing = new FixedCoalescingSessionAccessor(new RuntimeCoalescingSession(
            "wfexec-1", new InMemoryWorkflowSchedulerWorkQueue(), new CoalescingRuntimeCheckpointPersistenceOptions()));
        var processor = NewLiveDrainProcessor(store, dispatcher, _now, liveDrain, coalescing);
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope("wfexec-1")))
        {
            var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                limit: 10, workflowExecutionId: "wfexec-1", intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

            Assert.Equal(1, result.DeliveredCount);
        }

        // Coalescing overlay is authoritative: the durable claim path runs, so the fencing token advanced.
        var delivered = await store.FindAsync("outbox-1");
        Assert.NotNull(delivered);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        Assert.Equal(1, delivered.DeliveryFencingToken);
    }

    [Fact]
    public async Task Processor_LiveDrainMarker_LeavesNonEnqueueSchedulerWorkIntentsOnClaimPath()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var processor = NewLiveDrainProcessor(store, dispatcher, _now, liveDrain);
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", kind: "OtherIntent"));

        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope("wfexec-1")))
        {
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                limit: 10, workflowExecutionId: "wfexec-1", intentKind: "OtherIntent"));
        }

        var delivered = await store.FindAsync("outbox-1");
        Assert.NotNull(delivered);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        Assert.Equal(1, delivered.DeliveryFencingToken);
    }

    [Fact]
    public async Task Processor_LiveDrainMarkerForDifferentExecution_UsesClaimPath()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var processor = NewLiveDrainProcessor(store, dispatcher, _now, liveDrain);
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope("wfexec-OTHER")))
        {
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                limit: 10, workflowExecutionId: "wfexec-1", intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        }

        var delivered = await store.FindAsync("outbox-1");
        Assert.NotNull(delivered);
        Assert.Equal(1, delivered.DeliveryFencingToken);
    }

    private static RuntimePostCommitOutboxProcessor NewLiveDrainProcessor(
        IRuntimePostCommitOutboxStore store,
        RecordingDispatcher dispatcher,
        DateTimeOffset now,
        IRuntimeLiveDrainDeliveryAccessor liveDrainDeliveryAccessor,
        IRuntimeCoalescingSessionAccessor? coalescingSessionAccessor = null) =>
        new(
            store,
            dispatcher,
            new FakeTimeProvider(now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            workflowDispatchStore: null,
            logger: null,
            liveDrainDeliveryAccessor,
            coalescingSessionAccessor);

    private sealed class FixedCoalescingSessionAccessor(RuntimeCoalescingSession session) : IRuntimeCoalescingSessionAccessor
    {
        public RuntimeCoalescingSession? Current => session;

        public IDisposable Push(RuntimeCoalescingSession? pushedSession) =>
            throw new NotSupportedException("The processor test provides a fixed ambient coalescing session.");
    }

    private static RuntimePostCommitOutboxProcessor NewProcessor(
        IRuntimePostCommitOutboxStore store,
        RecordingDispatcher dispatcher,
        DateTimeOffset now) =>
        new(store, dispatcher, new FakeTimeProvider(now));

    private RuntimePostCommitOutboxItem NewOutboxItem(
        string outboxItemId,
        string intentId,
        string workflowExecutionId,
        DateTimeOffset? availableAt = null,
        RuntimePostCommitRetryPolicy? retryPolicy = null,
        string kind = "DispatchSignal",
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            outboxItemId: outboxItemId,
            intent: NewIntent(intentId, workflowExecutionId, kind, metadata),
            status: RuntimePostCommitOutboxStatus.Pending,
            recordedAt: _now,
            availableAt: availableAt ?? _now,
            retryPolicy: retryPolicy ?? new RuntimePostCommitRetryPolicy(3, TimeSpan.FromSeconds(10)));

    private RuntimePostCommitIntent NewIntent(
        string intentId,
        string workflowExecutionId,
        string kind,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        using var document = JsonDocument.Parse("""{"signal":"sent"}""");

        var isWorkflowDispatch = metadata?.ContainsKey("runtime.dispatchId") == true;
        var dispatchIdentity = isWorkflowDispatch
            ? new WorkflowDispatchIdentity(workflowExecutionId, "actexec-1")
            : null;
        return new RuntimePostCommitIntent(
            intentId: intentId,
            workflowExecutionId: workflowExecutionId,
            kind: kind,
            recordedAt: _now,
            activityExecutionId: "actexec-1",
            idempotencyKey: dispatchIdentity?.StartIdempotencyKey ?? $"checkpoint-1:{intentId}",
            payload: document.RootElement.Clone(),
            metadata: metadata ?? new Dictionary<string, string>());
    }

    private WorkflowDispatchRecord NewDispatchRecord(WorkflowDispatchMode mode = WorkflowDispatchMode.FireAndForget)
    {
        var identity = new WorkflowDispatchIdentity("wfexec-1", "actexec-1");
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            "wfexec-1",
            "actexec-1",
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-child", "definition-child", "version-child", "1.0.0", "sha256:child"),
            new WorkflowExecutableSourceProvenance(
                "source-child", "WorkflowDefinitionVersion", "version-child", "1.0.0",
                "definition-child", "version-child", "1.0.0", "publication-child", "slot-child"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            null,
            new WorkflowExecutionPartition("partition-1"),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot("wfexec-1", "initiator-1"),
            [],
            _now,
            _now);
    }

    private sealed class RecordingDispatcher(
        string? failOnIntentId = null,
        Exception? failure = null) : IRuntimePostCommitIntentDispatcher
    {
        public List<RuntimePostCommitIntent> Intents { get; } = [];

        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            if (intent.IntentId == failOnIntentId)
                throw failure ?? new InvalidOperationException($"Intent {intent.IntentId} failed.");

            Intents.Add(intent);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Claims through an in-memory store, but completes a claim by reporting a fixed outcome instead of persisting it.</summary>
    private sealed class ReportingClaimCompletionStore(
        InMemoryRuntimeCheckpointCommitStore inner,
        RuntimePostCommitOutboxClaimCompletionOutcome outcome)
        : IRuntimePostCommitOutboxStore, IRuntimePostCommitOutboxClaimStore, IRuntimePostCommitOutboxClaimCompletionStore
    {
        public List<RuntimePostCommitOutboxClaimCompletion> Completions { get; } = [];

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default) =>
            inner.GetDeliverableAsync(query, cancellationToken);

        public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(result, cancellationToken);

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(RuntimePostCommitOutboxClaimRequest request, CancellationToken cancellationToken = default) =>
            inner.ClaimAsync(request, cancellationToken);

        public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxClaim claim, RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(claim, result, cancellationToken);

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(RuntimePostCommitOutboxClaimCompletion completion, CancellationToken cancellationToken = default)
        {
            Completions.Add(completion);
            return ValueTask.FromResult(outcome);
        }
    }

    /// <summary>The DispatchWorkflow projection of a waited child's final start failure: DispatchFailed plus the parent resume.</summary>
    private sealed class WaitedDispatchFailureProjector(WorkflowDispatchRecord dispatch) : IPostCommitFailureProjector
    {
        public ValueTask<PostCommitFailureProjection?> ProjectAsync(
            RuntimePostCommitOutboxItem item,
            RuntimePostCommitOutboxDeliveryResult finalResult,
            CancellationToken cancellationToken = default)
        {
            var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
            var recordedAt = finalResult.RecordedAt;
            var resume = new RuntimePostCommitIntent(
                identity.ParentResumeIntentId,
                dispatch.ParentWorkflowExecutionId,
                WorkflowDispatchLifecycle.ResumeParentIntentKind,
                recordedAt,
                dispatch.ParentActivityExecutionId,
                identity.ParentResumeIdempotencyKey,
                payload: null,
                metadata: new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                    [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
                });
            return ValueTask.FromResult<PostCommitFailureProjection?>(new PostCommitFailureProjection(
                WorkflowDispatchLifecycle.TransitionToDispatchFailed(dispatch, item.OutboxItemId, 0, 1, recordedAt, recordedAt),
                new RuntimePostCommitOutboxItem(
                    identity.WaitFailureResumeOutboxItemId(0),
                    resume,
                    RuntimePostCommitOutboxStatus.Pending,
                    recordedAt,
                    recordedAt,
                    RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)))));
        }
    }

    private sealed class ThrowingResultStore(
        RuntimePostCommitOutboxItem item,
        Exception exception) : IRuntimePostCommitOutboxStore
    {
        public ValueTask AddPendingForTestingAsync(RuntimePostCommitOutboxItem item, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<RuntimePostCommitOutboxItem>>([item]);

        public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) =>
            throw exception;
    }

    /// <summary>
    /// A delivery-failure event carries the delivery exception itself, so its message and stack trace reach the log, while
    /// its structured fields stay identifiers and classifications with no exception text or intent payload.
    /// </summary>
    private static void AssertCarriesFailure(LogEntry entry, Exception failure)
    {
        Assert.Same(failure, entry.Exception);
        var serialized = string.Join(" ", entry.Fields.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.DoesNotContain("provider-secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack-secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signal", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sent", serialized, StringComparison.OrdinalIgnoreCase);
    }
}

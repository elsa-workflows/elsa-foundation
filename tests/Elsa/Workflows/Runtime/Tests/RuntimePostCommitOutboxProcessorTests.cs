using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Microsoft.Extensions.Time.Testing;
using Elsa.Testing;

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

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) =>
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

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) =>
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

    // ---------------------------------------------------------------------------------------------------------------
    // Issue #1798 - concurrent post-commit outbox delivery contention.
    //
    // The live drain skips the durable claim round-trip; the resumption sweep claims across EVERY execution with no
    // filter, on a timer. So the sweep can take an item between the live drain's read and its record. That used to throw
    // out of the store, escape the drain orchestrator and surface as an HTTP 500 on workflow start.
    //
    // StealingOutboxStore reproduces exactly that interleaving deterministically: it hands the live drain the items it
    // asked for, then immediately claims them under a different owner - so by the time the drain records, the item is
    // owned by someone else. No threads, no timing budget, no flake.
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Processor_LiveDrain_ItemStolenBetweenReadAndRecord_DoesNotThrow()
    {
        var inner = new InMemoryRuntimeCheckpointCommitStore();
        var store = new StealingOutboxStore(inner, _now, stealingOwnerId: "sweep-worker");
        var dispatcher = new RecordingDispatcher();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var processor = NewLiveDrainProcessor(store, dispatcher, _now, liveDrain);
        await inner.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope("wfexec-1")))
        {
            // Before the fix this call threw InvalidOperationException("... is claimed; its owner and fencing token are
            // required."), which became the 500. The assertion that matters most is simply that it returns.
            var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                limit: 10, workflowExecutionId: "wfexec-1", intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

            Assert.Equal(1, result.AttemptedCount);
            // Neither delivered nor failed: this deliverer persisted nothing, and the owning deliverer will record the
            // item's real terminal state. Counting it as delivered would inflate the drain's continuation signal.
            Assert.Equal(0, result.DeliveredCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal(1, result.SupersededCount);
            Assert.True(Assert.Single(result.Items).IsSuperseded);
        }

        Assert.True(store.DidSteal);
    }

    [Fact]
    public async Task Processor_LiveDrain_SupersededRecording_LeavesTheOwningClaimIntact()
    {
        var inner = new InMemoryRuntimeCheckpointCommitStore();
        var store = new StealingOutboxStore(inner, _now, stealingOwnerId: "sweep-worker");
        var dispatcher = new RecordingDispatcher();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var processor = NewLiveDrainProcessor(store, dispatcher, _now, liveDrain);
        await inner.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope("wfexec-1")))
        {
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                limit: 10, workflowExecutionId: "wfexec-1", intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        }

        // Write NOTHING when superseded: status, owner, fence, attempt count and delivery timestamp all belong to the
        // stealing owner's claim and must be untouched, or the owner's own completion would be rejected as stale.
        var current = await inner.FindAsync("outbox-1");
        Assert.NotNull(current);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, current.Status);
        Assert.Equal("sweep-worker", current.DeliveringOwnerId);
        Assert.Equal(1, current.DeliveryFencingToken);
        Assert.Equal(0, current.DeliveryAttemptCount);
        Assert.Null(current.DeliveredAt);
    }

    [Fact]
    public async Task Processor_LiveDrain_SupersededItem_StaysRecoverableByClaimExpiry()
    {
        var inner = new InMemoryRuntimeCheckpointCommitStore();
        var store = new StealingOutboxStore(inner, _now, stealingOwnerId: "sweep-worker");
        var dispatcher = new RecordingDispatcher();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var processor = NewLiveDrainProcessor(store, dispatcher, _now, liveDrain);
        await inner.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope("wfexec-1")))
        {
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                limit: 10, workflowExecutionId: "wfexec-1", intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        }

        // Tolerating supersession must not weaken the crash backstop: if the stealing owner dies without completing,
        // the claim expires and the item is reclaimable with a higher fence, exactly as before this change.
        var reclaimed = await inner.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "recovery-worker", _now.AddMinutes(5), TimeSpan.FromMinutes(1), 10));

        Assert.Equal("outbox-1", Assert.Single(reclaimed).OutboxItemId);
        Assert.Equal(2, Assert.Single(reclaimed).FencingToken);
    }

    [Fact]
    public async Task Processor_LiveDrain_SupersededRecording_IsLoggedAsSupersededNotAsFailure()
    {
        var inner = new InMemoryRuntimeCheckpointCommitStore();
        var store = new StealingOutboxStore(inner, _now, stealingOwnerId: "sweep-worker");
        var dispatcher = new RecordingDispatcher();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        var logger = new RecordingLogger<RuntimePostCommitOutboxProcessor>();
        var processor = new RuntimePostCommitOutboxProcessor(
            store,
            dispatcher,
            new FakeTimeProvider(_now),
            DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            workflowDispatchStore: null,
            logger,
            liveDrain);
        await inner.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1", kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope("wfexec-1")))
        {
            await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                limit: 10, workflowExecutionId: "wfexec-1", intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));
        }

        // This enum has no exhaustive switch anywhere and the build is warnings-only, so a new value reaches the
        // comparison sites silently. Pin the observable result: superseded is reported as its own event, at Information,
        // and never as a delivery success or a delivery failure.
        var superseded = Assert.Single(logger.Entries, entry => entry.EventId.Id == 68110);
        Assert.Equal(LogLevel.Information, superseded.Level);
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id == 68104);
        Assert.DoesNotContain(logger.Entries, entry => entry.EventId.Id is 68101 or 68103);
    }

    /// <summary>
    /// Hands out the deliverable items the caller asked for, then immediately claims them under a different owner - the
    /// exact interleaving of issue #1798, where the resumption sweep takes an item between a live drain's read and its
    /// record.
    /// </summary>
    private sealed class StealingOutboxStore(
        InMemoryRuntimeCheckpointCommitStore inner,
        DateTimeOffset now,
        string stealingOwnerId) : IRuntimePostCommitOutboxStore
    {
        public bool DidSteal { get; private set; }

        public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
            RuntimePostCommitOutboxQuery query,
            CancellationToken cancellationToken = default)
        {
            var items = await inner.GetDeliverableAsync(query, cancellationToken);
            if (items.Count > 0)
            {
                var stolen = await inner.ClaimAsync(
                    new RuntimePostCommitOutboxClaimRequest(stealingOwnerId, now, TimeSpan.FromMinutes(1), items.Count),
                    cancellationToken);
                DidSteal = stolen.Count > 0;
            }

            return items;
        }

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(result, cancellationToken);
    }

    /// <summary>
    /// Issue #1798, second trigger - deterministic, no concurrency involved.
    ///
    /// The fencing token never resets: every claim increments it, and a claim that expires or fails retryably returns the
    /// item to a deliverable state with the incremented token still on it. The claim-less live-drain path then picked it
    /// up and tried to record without a fence, which was rejected on the FIRST attempt, every time. One start meeting one
    /// expired claim was enough - no race required.
    ///
    /// The exclusion lives in the claim-less branch rather than in the store's deliverable query, because that query is
    /// shared with retry visibility and with the migration quiescence probe. The two tests below pin both halves of that:
    /// the live drain skips the fenced item, and the shared query still reports it.
    /// </summary>
    [Fact]
    public async Task Processor_LiveDrain_SkipsItemsAlreadyCarryingAFencingToken()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        var dispatcher = new RecordingDispatcher();
        var liveDrain = new AsyncLocalRuntimeLiveDrainDeliveryAccessor();
        await store.AddPendingForTestingAsync(NewOutboxItem(
            "outbox-1", "intent-1", "wfexec-1",
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            retryPolicy: new RuntimePostCommitRetryPolicy(5, TimeSpan.FromSeconds(10))));

        // Build the actually-poisoned state: claimed (fence -> 1), then released back with a retryable failure. The item
        // is now DELIVERABLE again - Pending/FailedRetryable, past its retry delay - while still carrying the fence.
        // Claiming alone would not reproduce it: a Delivering item is already excluded by the deliverable query, so a
        // test that stops there passes with or without this fix and proves nothing.
        var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "worker-1", _now, TimeSpan.FromMinutes(1), 10)));
        await store.RecordDeliveryResultAsync(claim, new RuntimePostCommitOutboxDeliveryResult(
            "outbox-1", RuntimePostCommitOutboxStatus.FailedRetryable, _now.AddSeconds(1), "transient"));

        var later = _now.AddMinutes(5);
        var processor = NewLiveDrainProcessor(store, dispatcher, later, liveDrain);

        var beforeDrain = await store.FindAsync("outbox-1");
        Assert.NotNull(beforeDrain);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, beforeDrain.Status);
        Assert.True(beforeDrain.DeliveryFencingToken > 0);
        // Deliverable AND fenced - the state that threw deterministically on the first attempt, every time.
        Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(later, 10)));

        using (liveDrain.Push(new RuntimeLiveDrainDeliveryScope("wfexec-1")))
        {
            var result = await processor.ProcessAsync(new RuntimePostCommitOutboxProcessRequest(
                limit: 10, workflowExecutionId: "wfexec-1", intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork));

            // Not attempted at all: it belongs to the claim path, the only path that can present the fence its
            // completion requires. Without the skip this item is dispatched and then recorded as superseded.
            Assert.Equal(0, result.AttemptedCount);
        }

        Assert.Empty(dispatcher.Intents);
    }

    [Fact]
    public async Task FencedItem_StaysVisibleToTheSharedDeliverableQueryForRetryAndQuiescence()
    {
        var store = new InMemoryRuntimeCheckpointCommitStore();
        await store.AddPendingForTestingAsync(NewOutboxItem("outbox-1", "intent-1", "wfexec-1"));
        await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest("worker-1", _now, TimeSpan.FromMinutes(1), 10));
        await store.RecordDeliveryResultAsync(
            new RuntimePostCommitOutboxClaim(
                (await store.FindAsync("outbox-1"))!, "worker-1", 1, _now, _now.AddMinutes(1)),
            new RuntimePostCommitOutboxDeliveryResult(
                "outbox-1", RuntimePostCommitOutboxStatus.FailedRetryable, _now.AddSeconds(1), "transient"));

        // The item is now FailedRetryable and fenced. The shared query MUST still surface it once its retry delay has
        // elapsed: it is the retry-visibility query, and the migration quiescence probe reads it to decide whether outbox
        // work is still outstanding. Hiding fenced items here would strand retries and let a migration run over live work.
        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddMinutes(5), 10));

        var item = Assert.Single(deliverable);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, item.Status);
        Assert.True(item.DeliveryFencingToken > 0);
    }
}

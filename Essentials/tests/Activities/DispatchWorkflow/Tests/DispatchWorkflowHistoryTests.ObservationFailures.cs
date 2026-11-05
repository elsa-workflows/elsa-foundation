using System.Globalization;
using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed partial class DispatchWorkflowHistoryTests
{
    [Theory]
    [InlineData("attempt", "integrity")] [InlineData("disposition", "integrity")]
    [InlineData("attempt", "io")] [InlineData("disposition", "io")]
    [InlineData("attempt", "timeout")] [InlineData("disposition", "timeout")]
    [InlineData("attempt", "requested-cancellation")] [InlineData("disposition", "requested-cancellation")]
    [InlineData("attempt", "unrequested-cancellation")] [InlineData("disposition", "unrequested-cancellation")]
    [Trait("Feature", "RunHistory")]
    public async Task Owner_observation_writer_failure_preserves_source_boundary_and_effect_stage(string stage, string failureKind)
    {
        using var stop = new CancellationTokenSource();
        var fault = new ChildObservationWriterFailure(stage, failureKind, stop);
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync(history: true, configureServices: services =>
        {
            var original = services.Single(item => item.ServiceType == typeof(IRuntimeHistoryObservationWriter));
            services.Remove(original);
            services.AddScoped<IRuntimeHistoryObservationWriter>(sp => new FailingChildObservationWriter(
                (IRuntimeHistoryObservationWriter)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), fault));
        });
        var run = await fixture.StartParentAsync("observation-" + stage + "-" + failureKind,
            "observation-parent-" + stage + "-" + failureKind, "correlation", waitForCompletion: true);
        var start = Assert.Single(CheckpointCommitIntents.IntentsOf(run.CompletionCommit),
            intent => intent.Kind == DispatchWorkflowConstants.StartChildIntentKind);
        var sourceId = RuntimePostCommitOutboxItems.OutboxItemId(run.CompletionCommit.CommitId, start);
        await using var scope = fixture.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var lookup = services.GetRequiredService<IPostCommitOutboxLookupStore>();
        var originalSource = (await lookup.FindDurableAsync(sourceId))!;
        var history = services.GetRequiredService<IRuntimeHistoryStore>();
        var before = await history.ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
        var dispatchBefore = JsonSerializer.Serialize(await services.GetRequiredService<IWorkflowDispatchStore>().FindAsync(run.Identity.DispatchId));
        var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
        var firstAttemptAt = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var expectedMetadata = originalSource.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        expectedMetadata.Add(RuntimePostCommitOutboxClaimTransitions.FirstDeliveryAttemptedAtMetadataKey,
            firstAttemptAt.ToString("O", CultureInfo.InvariantCulture));

        var failure = await Record.ExceptionAsync(async () =>
        {
            await processor.ProcessAsync(new(10, run.Start.WorkflowExecutionId, DispatchWorkflowConstants.StartChildIntentKind), stop.Token);
        });
        Assert.NotNull(fault.Cause);
        if (failureKind == "requested-cancellation")
        {
            var cancellation = Assert.IsType<OperationCanceledException>(failure);
            Assert.Same(fault.Cause, cancellation);
            Assert.Equal(stop.Token, cancellation.CancellationToken);
            Assert.True(stop.IsCancellationRequested);
        }
        else
        {
            var boundary = Assert.IsType<RuntimePostCommitSourceBoundaryException>(failure);
            Assert.Equal(failureKind != "integrity", boundary.IsUnavailable);
            Assert.Equal(sourceId, boundary.SourceOutboxItemId);
            Assert.Equal(start.Kind, boundary.IntentKind);
            Assert.Same(fault.Cause, boundary.InnerException);
            Assert.False(stop.IsCancellationRequested);
            if (failureKind == "unrequested-cancellation")
                Assert.Equal(stop.Token, Assert.IsType<OperationCanceledException>(boundary.InnerException).CancellationToken);
            if (failureKind == "integrity")
            {
                var integrity = Assert.IsType<RuntimeHistoryIntegrityException>(boundary.InnerException);
                var parse = Assert.IsType<JsonException>(integrity.InnerException);
                Assert.Same(fault.ParseCause, parse);
                Assert.Equal("$.version", parse.Path);
                Assert.True(parse.BytePositionInLine > 0);
                Assert.False(string.IsNullOrWhiteSpace(parse.StackTrace));
                Assert.DoesNotContain("unreadable-policy-version", boundary.Message);
            }
        }
        Assert.Equal(failureKind is "io" or "timeout" ? 2 : 1, fault.Writes.Count);
        Assert.All(fault.Writes, observation =>
        {
            Assert.Same(fault.Writes[0], observation);
            Assert.Equal(run.Start.WorkflowExecutionId, observation.Event.WorkflowExecutionId);
            Assert.Equal(sourceId, observation.Event.Causation!.DeliveryId);
            Assert.Equal(run.Start.WorkflowExecutionId, observation.Event.Causation.WorkflowExecutionId);
            Assert.Equal(start.HistoryCausation!.HistoryEventId, observation.Event.Causation.HistoryEventId);
        });

        // Read with an uncancelled token: neither a hard refusal nor unavailable evidence settles the source.
        var held = (await lookup.FindDurableAsync(sourceId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, held.Status);
        Assert.Equal(0, held.DeliveryAttemptCount);
        Assert.Equal(originalSource.LastFailureMessage, held.LastFailureMessage);
        Assert.Equal(firstAttemptAt, RuntimePostCommitOutboxClaimTransitions.ReadFirstDeliveryAttemptedAt(held));
        Assert.Equal(expectedMetadata.OrderBy(item => item.Key, StringComparer.Ordinal), held.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal));
        Assert.Null(held.DeliveredAt);
        Assert.Equal(originalSource.StimulusDeliverySourceScope, held.StimulusDeliverySourceScope);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeIntent(originalSource.Intent), RuntimeCheckpointCommitFingerprint.ComputeIntent(held.Intent));
        var after = await history.ReadPageAsync(new(run.Start.WorkflowExecutionId, 500));
        Assert.DoesNotContain(after.Items, item => item.Name == "ChildStartDispositionObserved");
        if (stage == "attempt")
        {
            Assert.Equal(before.Head, after.Head);
            Assert.Equal(before.Items.Select(item => item.HistoryEventId), after.Items.Select(item => item.HistoryEventId));
            Assert.Empty(fixture.ChildProbe.Observations);
            Assert.Equal(dispatchBefore, JsonSerializer.Serialize(await services.GetRequiredService<IWorkflowDispatchStore>().FindAsync(run.Identity.DispatchId)));
            Assert.Null(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
            Assert.DoesNotContain(fixture.Actors.Envelopes, item => item.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId);
            Assert.DoesNotContain(fixture.Actors.Activations, item => item.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId);
        }
        else
        {
            Assert.Single(after.Items, item => item.Name == "ChildStartAttemptObserved");
            Assert.Single(fixture.ChildProbe.Observations);
            Assert.NotNull(await fixture.FindWorkflowAsync(run.Identity.ChildWorkflowExecutionId));
            Assert.Single(fixture.Actors.Envelopes, item => item.WorkflowExecutionId == run.Identity.ChildWorkflowExecutionId &&
                item.Command.Kind == WorkflowExecutionCommandKind.Start);
        }
    }

    private sealed class ChildObservationWriterFailure(string stage, string failureKind, CancellationTokenSource stop)
    {
        public string EventName { get; } = stage == "attempt" ? "ChildStartAttemptObserved" : "ChildStartDispositionObserved";
        public Exception? Cause { get; private set; }
        public JsonException? ParseCause { get; private set; }
        public List<RuntimeHistoryObservation> Writes { get; } = [];

        public Exception Fail(RuntimeHistoryObservation observation, CancellationToken cancellationToken)
        {
            Writes.Add(observation);
            if (Cause is not null) return Cause;
            if (failureKind == "integrity")
            {
                ParseCause = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RuntimeHistoryCapturePolicy>(
                    "{\"policyId\":\"recorded-policy\",\"version\":\"unreadable-policy-version\",\"required\":true,\"supportedKinds\":[]}",
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return Cause = new RuntimeHistoryIntegrityException(observation.Event.WorkflowExecutionId,
                    "The retained owner history policy cannot be interpreted.", ParseCause);
            }
            if (failureKind == "requested-cancellation") stop.Cancel();
            return Cause = failureKind switch
            {
                "io" => new IOException("Constructed owner-observation provider failure."),
                "timeout" => new TimeoutException("Constructed owner-observation provider timeout."),
                "requested-cancellation" or "unrequested-cancellation" => new OperationCanceledException(
                    "Constructed owner-observation provider cancellation.", null, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(failureKind))
            };
        }
    }

    private sealed class FailingChildObservationWriter(IRuntimeHistoryObservationWriter inner, ChildObservationWriterFailure fault)
        : IRuntimeHistoryObservationWriter
    {
        public bool CanRecordHistory(IRuntimeHistoryStore selectedStore) => inner.CanRecordHistory(selectedStore);
        public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default) =>
            observation.Event.Name == fault.EventName
                ? ValueTask.FromException<RuntimeHistoryObservationResult>(fault.Fail(observation, cancellationToken))
                : inner.RecordAsync(observation, cancellationToken);
    }
}

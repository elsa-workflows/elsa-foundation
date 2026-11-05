using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class PublishStimulusSourcePipelineTests
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
        var fault = new RoutingObservationWriterFailure(stage, failureKind, stop);
        await using var harness = new Harness(history: true, configureServices: services =>
        {
            var original = services.Single(item => item.ServiceType == typeof(IRuntimeHistoryObservationWriter));
            services.Remove(original);
            services.AddScoped<IRuntimeHistoryObservationWriter>(sp => new FailingRoutingObservationWriter(
                (IRuntimeHistoryObservationWriter)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), fault));
        });
        await harness.SeedMaterial(); await harness.Bind("one-start");
        var originalSource = await harness.Commit();
        var before = await harness.History.ReadPageAsync(new("publisher"));
        var firstAttemptAt = harness.Clock.GetUtcNow();
        var expectedMetadata = originalSource.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        expectedMetadata.Add(RuntimePostCommitOutboxClaimTransitions.FirstDeliveryAttemptedAtMetadataKey,
            firstAttemptAt.ToString("O", CultureInfo.InvariantCulture));

        var failure = await Record.ExceptionAsync(async () =>
        {
            await harness.Processor.ProcessAsync(new(10, "publisher", PublishStimulusConstants.PublishStimulusIntentKind), stop.Token);
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
            Assert.Equal(originalSource.OutboxItemId, boundary.SourceOutboxItemId);
            Assert.Equal(originalSource.Intent.Kind, boundary.IntentKind);
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
            Assert.Equal("publisher", observation.Event.WorkflowExecutionId);
            Assert.Equal(originalSource.OutboxItemId, observation.Event.Causation!.DeliveryId);
            Assert.Equal("publisher", observation.Event.Causation.WorkflowExecutionId);
            Assert.Equal(originalSource.Intent.HistoryCausation!.HistoryEventId, observation.Event.Causation.HistoryEventId);
        });

        // A disposition failure follows acceptance; it cannot undo or acknowledge that recipient effect.
        var held = (await harness.Find(originalSource.OutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, held.Status);
        Assert.Equal(0, held.DeliveryAttemptCount);
        Assert.Equal(originalSource.LastFailureMessage, held.LastFailureMessage);
        Assert.Equal(firstAttemptAt, RuntimePostCommitOutboxClaimTransitions.ReadFirstDeliveryAttemptedAt(held));
        Assert.Equal(expectedMetadata.OrderBy(item => item.Key, StringComparer.Ordinal), held.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal));
        Assert.Null(held.DeliveredAt);
        Assert.Equal(originalSource.StimulusDeliverySourceScope, held.StimulusDeliverySourceScope);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeIntent(originalSource.Intent), RuntimeCheckpointCommitFingerprint.ComputeIntent(held.Intent));
        var after = await harness.History.ReadPageAsync(new("publisher"));
        Assert.DoesNotContain(after.Items, item => item.Name == "StimulusRoutingDispositionObserved");
        if (stage == "attempt")
        {
            Assert.Equal(before.Head, after.Head);
            Assert.Equal(before.Items.Select(item => item.HistoryEventId), after.Items.Select(item => item.HistoryEventId));
            Assert.Null(held.RecipientSnapshot);
            Assert.Empty(harness.Actors.Accepted);
            Assert.Empty(harness.Actors.Attempts);
            Assert.Equal(0, harness.Lookup.Reads);
        }
        else
        {
            Assert.Single(after.Items, item => item.Name == "StimulusRoutingAttemptObserved");
            Assert.NotNull(held.RecipientSnapshot);
            Assert.Single(harness.Actors.Accepted);
            Assert.Single(harness.Actors.Attempts);
            Assert.True(harness.Lookup.Reads > 0);
        }
    }

    private sealed class RoutingObservationWriterFailure(string stage, string failureKind, CancellationTokenSource stop)
    {
        public string EventName { get; } = stage == "attempt" ? "StimulusRoutingAttemptObserved" : "StimulusRoutingDispositionObserved";
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

    private sealed class FailingRoutingObservationWriter(IRuntimeHistoryObservationWriter inner, RoutingObservationWriterFailure fault)
        : IRuntimeHistoryObservationWriter
    {
        public bool CanRecordHistory(IRuntimeHistoryStore selectedStore) => inner.CanRecordHistory(selectedStore);
        public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default) =>
            observation.Event.Name == fault.EventName
                ? ValueTask.FromException<RuntimeHistoryObservationResult>(fault.Fail(observation, cancellationToken))
                : inner.RecordAsync(observation, cancellationToken);
    }
}

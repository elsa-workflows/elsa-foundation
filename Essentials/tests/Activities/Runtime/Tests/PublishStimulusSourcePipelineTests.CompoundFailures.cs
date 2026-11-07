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
    [InlineData(false)] [InlineData(true)]
    [Trait("Feature", "RunHistory")]
    public async Task Compound_failure_preserves_publication_permanent_or_hard_source_classification_and_owed_budget(bool hardSource)
    {
        var original = hardSource ? (Exception)new InvalidDataException("Constructed contradictory routing source.")
            : new RuntimeCheckpointCommitValidationException("Constructed permanent routed checkpoint refusal.");
        var secondary = new IOException("Constructed failure-observation outage.");
        var router = new CompoundFailingRouter(original, hardSource);
        var writes = new List<RuntimeHistoryObservation>();
        await using var harness = new Harness(history: true, configureServices: services =>
        {
            services.AddScoped<IStimulusRouter>(_ => router);
            var writer = services.Single(item => item.ServiceType == typeof(IRuntimeHistoryObservationWriter));
            services.Remove(writer);
            services.AddScoped<IRuntimeHistoryObservationWriter>(sp => new CompoundRoutingWriter(
                (IRuntimeHistoryObservationWriter)ActivatorUtilities.CreateInstance(sp, writer.ImplementationType!), secondary, writes));
        });
        await harness.SeedMaterial();
        var source = await harness.Commit();

        var boundary = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            harness.Processor.ProcessAsync(new(10, "publisher", PublishStimulusConstants.PublishStimulusIntentKind)).AsTask());

        Assert.False(boundary.IsUnavailable);
        Assert.Equal(source.OutboxItemId, boundary.SourceOutboxItemId);
        Assert.Equal(source.Intent.Kind, boundary.IntentKind);
        var compound = Assert.IsType<AggregateException>(boundary.InnerException);
        Assert.Equal(2, compound.InnerExceptions.Count);
        if (hardSource)
        {
            var primary = Assert.IsType<RuntimePostCommitSourceBoundaryException>(compound.InnerExceptions[0]);
            Assert.False(primary.IsUnavailable); Assert.Same(router.Failure, primary.InnerException);
            Assert.Same(original, primary.InnerException!.InnerException);
        }
        else
        {
            var primary = Assert.IsType<RuntimePostCommitDeliveryException>(compound.InnerExceptions[0]);
            Assert.Equal(PostCommitFailureKind.Permanent, primary.Kind);
            Assert.Same(original, primary.InnerException);
            Assert.True(RuntimeCheckpointCommitValidationException.IsCauseOf(primary));
        }
        var observation = Assert.IsType<RuntimePostCommitSourceBoundaryException>(compound.InnerExceptions[1]);
        Assert.True(observation.IsUnavailable); Assert.Same(secondary, observation.InnerException);
        Assert.Equal(2, writes.Count); Assert.Same(writes[0], writes[1]);
        Assert.Equal(1, router.Calls);
        var held = (await harness.Find(source.OutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, held.Status);
        Assert.Equal(0, held.DeliveryAttemptCount); Assert.Null(held.DeliveredAt);
        Assert.Null(held.RecipientSnapshot);
        Assert.Empty(harness.Actors.Attempts); Assert.Empty(harness.Actors.Accepted);
        var recorded = await harness.History.ReadPageAsync(new("publisher"));
        Assert.Single(recorded.Items, item => item.Name == "StimulusRoutingAttemptObserved");
        Assert.DoesNotContain(recorded.Items, item => item.Name is "StimulusRoutingDispositionObserved" or "ActivityFaulted");
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task Compound_failure_retains_known_capacity_shed_semantic_primary_and_unchanged_source_budget()
    {
        var secondary = new IOException("Constructed capacity-disposition write outage.");
        var writes = new List<RuntimeHistoryObservation>();
        await using var harness = new Harness(history: true, configureServices: services =>
        {
            var writer = services.Single(item => item.ServiceType == typeof(IRuntimeHistoryObservationWriter)); services.Remove(writer);
            services.AddScoped<IRuntimeHistoryObservationWriter>(sp => new CompoundRoutingWriter(
                (IRuntimeHistoryObservationWriter)ActivatorUtilities.CreateInstance(sp, writer.ImplementationType!), secondary, writes));
        });
        await harness.SeedMaterial(); await harness.Bind("capacity-start");
        var source = await harness.Commit(); harness.Actors.ShedStarts = 1;
        var boundary = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() =>
            harness.Processor.ProcessAsync(new(10, "publisher", PublishStimulusConstants.PublishStimulusIntentKind)).AsTask());
        Assert.True(boundary.IsUnavailable);
        var compound = Assert.IsType<AggregateException>(boundary.InnerException);
        var primary = Assert.IsType<RuntimePostCommitDeliveryException>(compound.InnerExceptions[0]);
        Assert.Equal(PostCommitFailureKind.Transient, primary.Kind); Assert.Equal("publish-stimulus-start-shed", primary.Code);
        Assert.Equal("1 start(s) were refused by runtime admission at capacity and will be retried.", primary.SafeSummary);
        Assert.Same(secondary, Assert.IsType<RuntimePostCommitSourceBoundaryException>(compound.InnerExceptions[1]).InnerException);
        Assert.Single(harness.Actors.Attempts); Assert.Empty(harness.Actors.Accepted);
        var held = (await harness.Find(source.OutboxItemId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, held.Status); Assert.Equal(0, held.DeliveryAttemptCount);
        Assert.NotNull(held.RecipientSnapshot); Assert.Equal(2, writes.Count); Assert.Same(writes[0], writes[1]);
    }

    private sealed class CompoundFailingRouter(Exception cause, bool hardSource) : IStimulusRouter
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; private set; }
        public ValueTask<StimulusRoutingResult> RouteSourceAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        {
            Calls++;
            Failure = hardSource ? new StimulusDeliveryBoundaryException(source.SourceOutboxItemId, cause, false) : cause;
            return ValueTask.FromException<StimulusRoutingResult>(Failure);
        }
        public ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Legacy routing must not run.");
    }

    private sealed class CompoundRoutingWriter(IRuntimeHistoryObservationWriter inner, Exception failure, List<RuntimeHistoryObservation> writes)
        : IRuntimeHistoryObservationWriter
    {
        public bool CanRecordHistory(IRuntimeHistoryStore reader) => inner.CanRecordHistory(reader);
        public ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
        {
            if (observation.Event.Name == "StimulusRoutingDispositionObserved")
            { writes.Add(observation); return ValueTask.FromException<RuntimeHistoryObservationResult>(failure); }
            return inner.RecordAsync(observation, cancellationToken);
        }
    }
}

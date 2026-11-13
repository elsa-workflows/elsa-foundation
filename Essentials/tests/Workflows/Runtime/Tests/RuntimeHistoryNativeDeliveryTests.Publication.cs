using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeHistoryNativeDeliveryTests
{
    private static string PublicationStimulusType => EventStimulus.StimulusType;
    private static string PublicationStimulusHash => EventStimulus.Hash("approval");

    [Theory]
    [InlineData("complete", false)] [InlineData("complete", true)]
    [InlineData("returned-fault", false)] [InlineData("returned-fault", true)]
    [InlineData("throw", false)] [InlineData("throw", true)]
    public async Task Actual_publication_processor_saved_resume_claim_and_callback_keep_their_causal_chain_and_original_target_choice(string outcome, bool captured)
    {
        using var f = await Fixture.CreateAsync(outcome, publication: true, captured: captured);
        await f.SeedPublisherAsync();
        var pending = await f.PublishAsync();
        var published = Assert.Single((await f.History.ReadPageAsync(new("publisher"))).Items, item => item.Name == "StimulusPublicationCommitted");
        Assert.Equal(published.HistoryEventId, pending.Intent.HistoryCausation!.HistoryEventId);
        Assert.Equal(1, (await f.ProcessPublicationsAsync()).DeliveredCount);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await f.PublicationWriter.FindDurableAsync(pending.OutboxItemId))!.Status);
        var before = await f.History.ReadPageAsync(new(Run));
        RuntimeHistoryEvent? receipt = null;
        if (captured)
        {
            receipt = Assert.Single(before.Items, item => item.Name == "DeliveryReceived");
            Assert.Equal(published.HistoryEventId, receipt.Causation!.HistoryEventId);
            Assert.Equal("publisher", receipt.Causation.WorkflowExecutionId);
            Assert.Equal(pending.OutboxItemId, receipt.Causation.DeliveryId);
            Assert.Single(before.Items, item => item.Name == "DeliveryDispatched");
        }
        else { Assert.Null(before.Head); Assert.Empty(before.Items); }
        Assert.Equal(0, f.Activator.Calls); Assert.NotNull(await f.Bookmarks.FindAsync(Run, Wait));
        var queued = Assert.Single(await f.Queue.ListAllAsync(new(Run)));
        var cause = queued.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!.HistoryCausation!;
        Assert.Equal(captured ? receipt!.HistoryEventId : published.HistoryEventId, cause.HistoryEventId);
        var drained = await f.DrainAsync();
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(drained.Items).Status);
        Assert.Equal(1, f.Activator.Calls); Assert.Null(await f.Bookmarks.FindAsync(Run, Wait));
        var page = await f.History.ReadPageAsync(new(Run, limit: 200));
        if (captured)
        {
            var consumed = Assert.Single(page.Items, item => item.Name == "BookmarkConsumed");
            var entered = Assert.Single(page.Items, item => item.Name == "ActivityContinuationStarted");
            Assert.Equal(receipt!.HistoryEventId, consumed.Causation!.HistoryEventId);
            Assert.Equal(receipt.HistoryEventId, entered.Causation!.HistoryEventId);
            Assert.True(receipt.Sequence < consumed.Sequence); Assert.True(consumed.Sequence < entered.Sequence);
            Assert.Equal(RuntimeHistoryBookmarkStatus.Consumed, (await f.History.FindBookmarkAsync(Run, Wait))!.Status);
            if (outcome == "complete")
            {
                Assert.Single(page.Items, item => item.Name == "ActivityContinuationCompleted");
                Assert.DoesNotContain(page.Items, item => item.Name == "ActivityContinuationFaulted");
            }
            else
            {
                var fault = Assert.Single(page.Items, item => item.Name == "ActivityContinuationFaulted");
                Assert.Equal(receipt.HistoryEventId, fault.Causation!.HistoryEventId);
                Assert.True(consumed.Sequence < fault.Sequence); Assert.Single(page.Items, item => item.Name == "IncidentRecorded");
            }
            Assert.DoesNotContain(page.Items, item => item.Name == "DeliveryFaulted");
            Assert.DoesNotContain("private", JsonSerializer.Serialize(page.Items));
            Assert.False(page.Head!.CapturedFromStart);
        }
        else
        {
            Assert.Null(page.Head); Assert.Empty(page.Items);
            var state = (await f.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(Run))!;
            Assert.False(state.HistoryCaptureEnabledAtAdmission); Assert.Null(state.HistoryCapturePolicy);
        }
        var publisher = (await f.History.ReadPageAsync(new("publisher"))).Items;
        Assert.Single(publisher, item => item.Name == "StimulusRoutingAttemptObserved");
        Assert.Single(publisher, item => item.Name == "StimulusRoutingDispositionObserved");
    }

    [Fact]
    public async Task Replaying_the_same_native_publication_commit_does_not_reopen_a_delivered_source_or_enqueue_another_resume()
    {
        using var f = await Fixture.CreateAsync("complete", publication: true);
        await f.SeedPublisherAsync(); var original = await f.PublishAsync();
        Assert.Equal(1, (await f.ProcessPublicationsAsync()).DeliveredCount);
        var replay = await f.PublishAsync();
        Assert.Equal(original.OutboxItemId, replay.OutboxItemId); Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, replay.Status);
        Assert.Empty((await f.ProcessPublicationsAsync()).Items);
        Assert.Single(await f.Queue.ListAllAsync(new(Run)));
        await f.DrainAsync(); Assert.Equal(1, f.Activator.Calls);
        var publisher = (await f.History.ReadPageAsync(new("publisher"))).Items;
        Assert.Single(publisher, item => item.Name == "StimulusPublicationCommitted");
        Assert.Single(publisher, item => item.Name == "StimulusRoutingAttemptObserved");
        Assert.Single((await f.History.ReadPageAsync(new(Run))).Items, item => item.Name == "BookmarkConsumed");
        // Exact commit replay is not an actual second transport invocation or a second business stimulus.
    }

    [Fact]
    public async Task Two_distinct_publications_can_handoff_to_one_generation_but_only_one_native_claim_enters_the_callback()
    {
        using var f = await Fixture.CreateAsync("complete", publication: true);
        await f.SeedPublisherAsync(); var first = await f.PublishAsync();
        Assert.Equal(1, (await f.ProcessPublicationsAsync()).DeliveredCount); f.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await f.PublishAsync("publication-two", "publisher-activity-two");
        Assert.Equal(1, (await f.ProcessPublicationsAsync()).DeliveredCount);
        Assert.NotEqual(first.OutboxItemId, second.OutboxItemId);
        Assert.NotEqual(first.Intent.IdempotencyKey, second.Intent.IdempotencyKey);
        Assert.Equal(2, (await f.Queue.ListAllAsync(new(Run))).Count);
        Assert.Equal(0, f.Activator.Calls);
        f.Clock.Advance(TimeSpan.FromSeconds(1)); // Follow-up work from the winner is later than both queued resumes.
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Equal(1, f.Activator.Calls); Assert.Null(await f.Bookmarks.FindAsync(Run, Wait));
        var events = (await f.History.ReadPageAsync(new(Run, limit: 200))).Items;
        Assert.Equal(2, events.Count(item => item.Name == "DeliveryReceived"));
        Assert.Equal(2, events.Count(item => item.Name == "DeliveryDispatched"));
        Assert.Single(events, item => item.Name == "BookmarkConsumed");
        Assert.Single(events, item => item.Name == "ActivityContinuationStarted");
        var consumed = Assert.Single(events, item => item.Name == "BookmarkConsumed");
        Assert.Contains(events, item => item.Name == "DeliveryReceived" && item.HistoryEventId == consumed.Causation!.HistoryEventId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await f.PublicationWriter.FindDurableAsync(second.OutboxItemId))!.Status);
    }

    [Theory]
    [InlineData("removed")] [InlineData("replaced")]
    public async Task Publication_handoff_does_not_authorize_consuming_a_later_wait_generation(string scenario)
    {
        using var f = await Fixture.CreateAsync("complete", publication: true);
        await f.SeedPublisherAsync(); await f.PublishAsync(); Assert.Equal(1, (await f.ProcessPublicationsAsync()).DeliveredCount);
        var original = (await f.Bookmarks.FindAsync(Run, Wait))!;
        await f.Bookmarks.DeleteAsync(Run, Wait);
        if (scenario == "replaced") await f.Bookmarks.SaveAsync(original); // Same fields, a new native generation.
        var drained = await f.DrainAsync();
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single(drained.Items).Status);
        Assert.Empty(await f.Queue.ListAllAsync(new(Run))); Assert.Empty(await f.Poison.ListAsync(Run)); Assert.Equal(0, f.Activator.Calls);
        var events = (await f.History.ReadPageAsync(new(Run))).Items;
        Assert.Single(events, item => item.Name == "DeliveryReceived"); Assert.Single(events, item => item.Name == "DeliveryDispatched");
        Assert.DoesNotContain(events, item => item.Name is "BookmarkConsumed" or "ActivityContinuationStarted" or "ActivityContinuationCompleted");
        if (scenario == "replaced") Assert.NotNull(await f.Bookmarks.FindAsync(Run, Wait));
    }

    private static void ConfigurePublication(IServiceCollection services)
    {
        new ActivitiesPrimitivesFeature().ConfigureServices(services);
        services.AddSingleton<ISchemaDormancyCheck, PublicationSchema>();
    }

    private sealed partial class Fixture
    {
        internal IServiceProvider Services => scope.ServiceProvider;
        internal Microsoft.Extensions.Time.Testing.FakeTimeProvider Clock => clock;
        internal InMemoryRuntimeCheckpointCommitStore PublicationWriter => scope.ServiceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        internal async Task SeedPublisherAsync()
        {
            var now = clock.GetUtcNow();
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(new("publisher", Pin, WorkflowExecutionStatus.Suspended,
                null, now, now, now, null, null, null, "native-history", new Dictionary<string, string>())
                { HistoryCapturePolicy = RuntimeHistoryRegistration.ResolvePolicy(scope.ServiceProvider, "publisher"), HistoryCaptureEnabledAtAdmission = true,
                  Partition = new("native-history"), RunKind = WorkflowRunKind.PublishedRun });
        }
        internal async Task<RuntimePostCommitOutboxItem> PublishAsync(string publicationId = "publication", string activityId = "publisher-activity")
        {
            var sp = scope.ServiceProvider; var now = clock.GetUtcNow();
            sp.GetRequiredService<IPublishStimulusStager>().StagePublishStimulus(new("publisher", activityId, "approval", null,
                JsonSerializer.SerializeToElement(new Approval(true)), false));
            var intent = Assert.Single(sp.GetRequiredService<IPublishStimulusStagingAccessor>().TakePublishStimuli("publisher", activityId));
            var commit = new RuntimeCheckpointCommit(publicationId, new(publicationId + ":checkpoint", RuntimeCheckpointNames.ActivityCompleted,
                "publisher", now, [activityId], new Dictionary<string, string> { [RuntimeMetadataKeys.CommandId] = publicationId + ":command" }),
                new(null, null, [], [], [], [], []), [intent], new Dictionary<string, string>());
            Assert.True((await sp.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit)).Succeeded);
            return (await PublicationWriter.FindDurableAsync(RuntimePostCommitOutboxItems.OutboxItemId(publicationId, intent)))!;
        }
        internal ValueTask<RuntimePostCommitOutboxProcessResult> ProcessPublicationsAsync() =>
            scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, intentKind: PublishStimulusConstants.PublishStimulusIntentKind));
    }

    // Explicit component schema availability; this is not migration/provider/fleet acceptance.
    private sealed class PublicationSchema : ISchemaDormancyCheck
    {
        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements) => SchemaAvailability.Available;
        public ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default) => ValueTask.FromResult(Evaluate(requirements));
        public ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public IReadOnlyList<SchemaFamilyObservation> Observe() => [];
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<SchemaFamilyObservation>>([]);
    }
}

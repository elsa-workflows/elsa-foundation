using System.Text.Json;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeHistoryNativeDeliveryTests
{
    [Theory]
    [InlineData("removed", false)] [InlineData("removed", true)]
    [InlineData("replaced", false)] [InlineData("replaced", true)]
    [InlineData("expired", false)] [InlineData("expired", true)]
    [InlineData("workflow-terminal", false)] [InlineData("workflow-terminal", true)]
    [InlineData("activity-terminal", false)] [InlineData("activity-terminal", true)]
    public async Task Atomic_saved_admission_loss_is_observed_without_consumption_callback_or_inferred_winner(string change, bool captured)
    {
        var race = new ClaimRaceControl(change);
        using var f = await Fixture.CreateAsync("complete", publication: true, captured: captured, configure: race.Configure,
            expiresAt: change == "expired" ? At.AddSeconds(2) : null);
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync();
        var work = Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run)));
        var origin = work.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!.HistoryCausation!;
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Equal(1, race.Claims); Assert.Equal(0, f.Activator.Calls);
        Assert.Empty(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))); Assert.Empty(await f.Poison.ListAsync(Run));
        var page = await f.History.ReadPageAsync(new(Run));
        if (!captured) { Assert.Null(page.Head); Assert.Empty(page.Items); return; }
        var skipped = Assert.Single(page.Items, IsReceiverObservation);
        Assert.Equal("receiver-admission-superseded", skipped.Metadata["history.reasonCode"]);
        Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, skipped.Authority);
        Assert.Equal(f.Clock.GetUtcNow(), skipped.OccurredAt);
        Assert.Equal(ActivityId, skipped.ActivityExecutionId); Assert.Equal(Node, skipped.ExecutableNodeId); Assert.Equal(Wait, skipped.BookmarkId);
        Assert.Equal(origin.HistoryEventId, skipped.Causation!.HistoryEventId);
        Assert.Equal(work.CommandId, skipped.Causation.CommandId); Assert.Equal(work.WorkItemId, skipped.Causation.SchedulerWorkItemId);
        Assert.DoesNotContain(page.Items, item => item.Name is "BookmarkConsumed" or "ActivityContinuationStarted" or "ActivityContinuationCompleted");
        if (change != "removed") Assert.NotNull(await f.Bookmarks.FindAsync(Run, Wait));
        Assert.Equal(RuntimeHistoryBookmarkStatus.Active, (await f.History.FindBookmarkAsync(Run, Wait))!.Status);
    }

    [Fact]
    public async Task Unchanged_atomic_admission_still_consumes_and_calls_back_without_a_receiver_mismatch_observation()
    {
        var race = new ClaimRaceControl("unchanged");
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: race.Configure);
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync();
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Equal(1, race.Claims); Assert.Equal(1, f.Activator.Calls); Assert.Null(await f.Bookmarks.FindAsync(Run, Wait));
        var events = (await f.History.ReadPageAsync(new(Run))).Items;
        Assert.DoesNotContain(events, IsReceiverObservation);
        Assert.Single(events, item => item.Name == "BookmarkConsumed");
        Assert.Single(events, item => item.Name == "ActivityContinuationCompleted");
    }

    [Theory]
    [InlineData("before-commit")] [InlineData("after-commit")] [InlineData("false-ack")]
    public async Task Unacknowledged_atomic_loss_observation_defers_and_reentry_records_the_then_current_receiver_decision(string failure)
    {
        var race = new ClaimRaceControl("removed"); var recording = new ReceiverControl { Failure = failure };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: services =>
        { race.Configure(services); recording.Configure(services); });
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync();
        var work = Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run)));
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Deferred, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Equal(work.WorkItemId, Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))).WorkItemId);
        Assert.Equal("receiver-admission-superseded", Assert.Single(recording.Attempts).Event.Metadata["history.reasonCode"]);
        Assert.Equal(failure == "after-commit" ? 1 : 0, (await f.History.ReadPageAsync(new(Run))).Items.Count(IsReceiverObservation));
        Assert.Empty(await f.Poison.ListAsync(Run)); Assert.Equal(0, f.Activator.Calls);
        f.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Empty(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))); Assert.Equal(0, f.Activator.Calls); Assert.Equal(1, race.Claims);
        Assert.Equal(2, recording.Attempts.Count);
        var observations = (await f.History.ReadPageAsync(new(Run))).Items.Where(IsReceiverObservation).ToArray();
        Assert.Equal(failure == "after-commit" ? 2 : 1, observations.Length);
        Assert.Equal("receiver-bookmark-missing", observations[^1].Metadata["history.reasonCode"]);
        Assert.Equal(observations.Length, observations.Select(item => item.HistoryEventId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(observations, item => Assert.Equal(work.WorkItemId, item.Causation!.SchedulerWorkItemId));
    }

    [Fact]
    public async Task Scope_drift_at_atomic_admission_cannot_move_the_saved_decision_into_another_history_scope()
    {
        var access = new ReceiverAccess(); var original = access.Current;
        var race = new ClaimRaceControl("removed") { AfterChange = () => access.Current = PersistenceAccessContext.Scoped(new("foreign")) };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: services =>
        { services.AddSingleton<IPersistenceAccessContextAccessor>(access); race.Configure(services); });
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync();
        var work = Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run)));
        var handler = new WorkflowResumeBookmarkSchedulerWorkHandler(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Clock);
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => handler.HandleAsync(work).AsTask());
        access.Current = original;
        Assert.Equal("runtime.history.capture-unavailable", failure.Code);
        Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))); Assert.Equal(0, f.Activator.Calls);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new(Run))).Items, IsReceiverObservation);
    }

    // Inject a concurrent native state change immediately before the real consumption service's atomic
    // checkpoint. The actual coherent native stores, committer, predicate and rejection remain in use.
    private sealed class ClaimRaceControl(string change)
    {
        internal string Change { get; } = change;
        internal int Claims; internal Action? AfterChange;
        internal void Configure(IServiceCollection services)
        {
            Assert.Equal(typeof(BookmarkConsumptionCheckpointService),
                services.Last(descriptor => descriptor.ServiceType == typeof(IBookmarkConsumptionCheckpointService)).ImplementationType);
            services.AddScoped<IBookmarkConsumptionCheckpointService>(sp => new ClaimRaceService(
                ActivatorUtilities.CreateInstance<BookmarkConsumptionCheckpointService>(sp), sp, this));
        }
        private sealed class ClaimRaceService(IBookmarkConsumptionCheckpointService inner, IServiceProvider services,
            ClaimRaceControl owner) : IBookmarkConsumptionCheckpointService
        {
            public async ValueTask<BookmarkConsumptionCheckpointResult> CommitAsync(BookmarkConsumptionCheckpointRequest request,
                CancellationToken cancellationToken = default)
            {
                if (request.TransitionKind == BookmarkResumeCheckpointTransitionKind.InitialTriggerClaim && owner.Claims++ == 0)
                {
                    var bookmarks = services.GetRequiredService<IBookmarkStateStore>();
                    switch (owner.Change)
                    {
                        case "removed": case "replaced":
                            var original = (await bookmarks.FindAsync(Run, Wait, cancellationToken))!;
                            await bookmarks.DeleteAsync(Run, Wait, cancellationToken);
                            if (owner.Change == "replaced") await bookmarks.SaveAsync(original, cancellationToken);
                            break;
                        case "expired":
                            ((FakeTimeProvider)services.GetRequiredService<TimeProvider>()).Advance(TimeSpan.FromSeconds(3));
                            break;
                        case "workflow-terminal":
                            var workflows = services.GetRequiredService<IWorkflowExecutionStateStore>();
                            var workflow = (await workflows.FindAsync(Run, cancellationToken))!;
                            await workflows.SaveAsync(workflow with { Status = WorkflowExecutionStatus.Completed }, cancellationToken);
                            break;
                        case "activity-terminal":
                            var activities = services.GetRequiredService<IActivityExecutionStateStore>();
                            var activity = (await activities.FindAsync(Run, ActivityId, cancellationToken))!;
                            await activities.SaveAsync(activity with { Status = ActivityExecutionStatus.Completed }, cancellationToken);
                            break;
                        case "unchanged": break;
                        default: throw new ArgumentOutOfRangeException(nameof(owner.Change));
                    }
                    owner.AfterChange?.Invoke();
                }
                return await inner.CommitAsync(request, cancellationToken);
            }
        }
    }
}

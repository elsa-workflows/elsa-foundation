using System.Text.Json;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeHistoryNativeDeliveryTests
{
    [Theory]
    [InlineData("removed", false)] [InlineData("removed", true)]
    [InlineData("replaced", false)] [InlineData("replaced", true)]
    [InlineData("expired", false)] [InlineData("expired", true)]
    public async Task Receiver_records_why_an_exact_saved_wait_cannot_be_claimed_before_acknowledging_without_a_callback(string change, bool captured)
    {
        using var f = await Fixture.CreateAsync("complete", publication: true, captured: captured,
            expiresAt: change == "expired" ? At.AddSeconds(2) : null);
        await f.SeedPublisherAsync(); await f.PublishAsync(); Assert.Equal(1, (await f.ProcessPublicationsAsync()).DeliveredCount);
        var queued = Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run)));
        var origin = queued.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!.HistoryCausation!;
        await ChangeWaitAsync(f, change);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Empty(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))); Assert.Equal(0, f.Activator.Calls);
        var page = await f.History.ReadPageAsync(new(Run));
        if (!captured) { Assert.Null(page.Head); Assert.Empty(page.Items); return; }
        var skipped = Assert.Single(page.Items, IsReceiverObservation);
        Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, skipped.Authority);
        Assert.Equal(Wait, skipped.BookmarkId); Assert.Equal(ActivityId, skipped.ActivityExecutionId); Assert.Equal(Node, skipped.ExecutableNodeId);
        Assert.Equal(origin.HistoryEventId, skipped.Causation!.HistoryEventId);
        Assert.Equal(queued.CommandId, skipped.Causation.CommandId); Assert.Equal(queued.WorkItemId, skipped.Causation.SchedulerWorkItemId);
        Assert.Equal(f.Clock.GetUtcNow(), skipped.OccurredAt);
        Assert.Equal(change switch { "removed" => "receiver-bookmark-missing", "expired" => "receiver-bookmark-expired", _ => "receiver-wait-no-longer-eligible" },
            skipped.Metadata["history.reasonCode"]);
        Assert.Single(page.Items, item => item.Name == "DeliveryDispatched");
        Assert.DoesNotContain(page.Items, item => item.Name is "BookmarkConsumed" or "ActivityContinuationStarted");
        if (change != "removed") Assert.NotNull(await f.Bookmarks.FindAsync(Run, Wait));
    }

    [Theory]
    [InlineData("before-commit")] [InlineData("after-commit")] [InlineData("false-ack")]
    public async Task Unacknowledged_receiver_history_keeps_native_work_owed_and_actual_reentry_is_another_observation(string failure)
    {
        var control = new ReceiverControl { Failure = failure };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: control.Configure);
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync(); await ChangeWaitAsync(f, "removed");
        var queued = Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run)));
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Deferred, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Equal(queued.WorkItemId, Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))).WorkItemId);
        Assert.Equal(failure == "after-commit" ? 1 : 0, (await f.History.ReadPageAsync(new(Run))).Items.Count(IsReceiverObservation));
        Assert.Empty(await f.Poison.ListAsync(Run)); Assert.Equal(0, f.Activator.Calls);
        f.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Empty(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))); Assert.Equal(0, f.Activator.Calls);
        var observations = (await f.History.ReadPageAsync(new(Run))).Items.Where(IsReceiverObservation).ToArray();
        Assert.Equal(failure == "after-commit" ? 2 : 1, observations.Length);
        Assert.Equal(observations.Length, observations.Select(item => item.HistoryEventId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(observations, item => Assert.Equal(queued.WorkItemId, item.Causation!.SchedulerWorkItemId));
        Assert.Equal(2, control.Attempts.Count);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(observations));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task An_expired_replacement_cannot_supply_the_original_saved_recipients_expiry_reason(bool differentOwner)
    {
        using var f = await Fixture.CreateAsync("complete", publication: true);
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync();
        var original = (await f.Bookmarks.FindAsync(Run, Wait))!;
        await f.Bookmarks.DeleteAsync(Run, Wait);
        var replacement = original with { ActivityExecutionId = differentOwner ? "replacement-activity" : original.ActivityExecutionId,
            ExpiresAt = f.Clock.GetUtcNow().AddSeconds(-1) };
        await f.Bookmarks.SaveAsync(replacement);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainAsync()).Items).Status);
        var skipped = Assert.Single((await f.History.ReadPageAsync(new(Run))).Items, IsReceiverObservation);
        Assert.Equal(ActivityId, skipped.ActivityExecutionId);
        Assert.Equal("receiver-wait-no-longer-eligible", skipped.Metadata["history.reasonCode"]);
        var retained = (await f.Bookmarks.FindAsync(Run, Wait))!;
        Assert.Equal(replacement.ActivityExecutionId, retained.ActivityExecutionId); Assert.Equal(replacement.ExpiresAt, retained.ExpiresAt);
        Assert.Equal(0, f.Activator.Calls);
    }

    [Fact]
    public async Task A_prior_profile_without_receiver_unmatched_kind_is_not_widened_by_installing_this_owner()
    {
        using var f = await Fixture.CreateAsync("complete", publication: true, transformPolicy: policy => new(policy.PolicyId, policy.Version, policy.Required,
            policy.SupportedKinds.Where(kind => kind != "DeliveryUnmatched").ToArray()));
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync(); await ChangeWaitAsync(f, "removed");
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, Assert.Single((await f.DrainAsync()).Items).Status);
        var page = await f.History.ReadPageAsync(new(Run));
        Assert.DoesNotContain("DeliveryUnmatched", page.Head!.Policy.SupportedKinds); Assert.DoesNotContain(page.Items, IsReceiverObservation);
        Assert.Equal(0, f.Activator.Calls);
    }

    [Fact]
    public async Task A_removed_required_capture_owner_cannot_turn_an_unrecorded_receiver_decision_into_acknowledgement()
    {
        var missing = false;
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: services =>
            services.AddScoped<IRuntimeHistorySourceExtension>(sp => missing ? null! : new DefaultRuntimeHistorySourceExtension(sp.GetRequiredService<IWorkflowExecutionStateStore>())));
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync(); await ChangeWaitAsync(f, "removed");
        missing = true;
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Deferred, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))); Assert.Empty(await f.Poison.ListAsync(Run)); Assert.Equal(0, f.Activator.Calls);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new(Run))).Items, IsReceiverObservation);
    }

    [Fact]
    public async Task Requested_cancellation_during_receiver_recording_preserves_the_original_cancellation_and_does_not_acknowledge()
    {
        using var cancellation = new CancellationTokenSource();
        OperationCanceledException? original = null;
        var control = new ReceiverControl { Before = token => { cancellation.Cancel(); original = new OperationCanceledException("controlled cancellation", token); throw original; } };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: control.Configure);
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync(); await ChangeWaitAsync(f, "removed");
        var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.DrainAsync(cancellation.Token).AsTask());
        Assert.Same(original, caught); Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))); Assert.Equal(0, f.Activator.Calls);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new(Run))).Items, IsReceiverObservation);
    }

    [Fact]
    public async Task A_matching_requested_cancellation_inside_the_provider_domain_failure_is_restored_for_a_direct_receiver_caller()
    {
        using var cancellation = new CancellationTokenSource(); OperationCanceledException? original = null;
        var control = new ReceiverControl { Before = token =>
        {
            cancellation.Cancel(); original = new OperationCanceledException("controlled wrapped cancellation", token);
            throw new RuntimeHistoryUnavailableException(Run, "record-observation", original);
        } };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: control.Configure);
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync(); await ChangeWaitAsync(f, "removed");
        var work = Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run)));
        var handler = new WorkflowResumeBookmarkSchedulerWorkHandler(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Clock);
        var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(work, cancellation.Token).AsTask());
        Assert.Same(original, caught); Assert.Equal(cancellation.Token, caught.CancellationToken);
        Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))); Assert.Equal(0, f.Activator.Calls);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new(Run))).Items, IsReceiverObservation);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Unrequested_or_unrelated_wrapped_cancellation_keeps_its_unavailable_domain_cause(bool unrelated)
    {
        using var caller = new CancellationTokenSource(); using var other = new CancellationTokenSource();
        RuntimeHistoryUnavailableException? wrapped = null;
        var control = new ReceiverControl { Before = token =>
        {
            if (unrelated) caller.Cancel();
            wrapped = new(Run, "record-observation", new OperationCanceledException("other cancellation", unrelated ? other.Token : token));
            throw wrapped;
        } };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: control.Configure);
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync(); await ChangeWaitAsync(f, "removed");
        var work = Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run)));
        var handler = new WorkflowResumeBookmarkSchedulerWorkHandler(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Clock);
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => handler.HandleAsync(work, caller.Token).AsTask());
        Assert.Same(wrapped, failure.InnerException); Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))); Assert.Equal(0, f.Activator.Calls);
    }

    [Fact]
    public async Task A_matching_requested_cancellation_cannot_hide_hard_malformed_data_in_the_same_provider_chain()
    {
        using var cancellation = new CancellationTokenSource(); RuntimeHistoryUnavailableException? wrapped = null;
        var control = new ReceiverControl { Before = token =>
        {
            cancellation.Cancel(); wrapped = new(Run, "record-observation", new InvalidDataException("malformed", new OperationCanceledException(token)));
            throw wrapped;
        } };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: control.Configure);
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync(); await ChangeWaitAsync(f, "removed");
        var work = Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run)));
        var handler = new WorkflowResumeBookmarkSchedulerWorkHandler(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Clock);
        var failure = await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => handler.HandleAsync(work, cancellation.Token).AsTask());
        Assert.Same(wrapped, failure.InnerException); Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))); Assert.Equal(0, f.Activator.Calls);
    }

    [Fact]
    public async Task Access_drift_after_an_observed_append_does_not_acknowledge_native_work_or_authorize_a_callback()
    {
        var access = new ReceiverAccess(); var original = access.Current;
        var control = new ReceiverControl { After = () => access.Current = PersistenceAccessContext.Scoped(new("foreign")) };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: services =>
        { services.AddSingleton<IPersistenceAccessContextAccessor>(access); control.Configure(services); });
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync(); await ChangeWaitAsync(f, "removed");
        var work = Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run)));
        var handler = new WorkflowResumeBookmarkSchedulerWorkHandler(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Clock);
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => handler.HandleAsync(work).AsTask());
        access.Current = original; Assert.Equal("runtime.history.capture-unavailable", failure.Code);
        Assert.Single(await f.Queue.ListAllAsync(new RuntimeSchedulerWorkQuery(Run))); Assert.Equal(0, f.Activator.Calls);
        Assert.Single((await f.History.ReadPageAsync(new(Run))).Items, IsReceiverObservation);
    }

    [Fact]
    public async Task Malformed_receiver_history_with_nested_io_remains_a_hard_failure_instead_of_a_retryable_write()
    {
        var control = new ReceiverControl { Failure = "hard" };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: control.Configure);
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync(); await ChangeWaitAsync(f, "removed");
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Equal(0, f.Activator.Calls); Assert.DoesNotContain((await f.History.ReadPageAsync(new(Run))).Items, IsReceiverObservation);
    }

    private static bool IsReceiverObservation(RuntimeHistoryEvent item) => item.Name == "DeliveryUnmatched" &&
        item.Metadata.GetValueOrDefault("history.reasonCode")?.StartsWith("receiver-", StringComparison.Ordinal) == true;
    private static async Task ChangeWaitAsync(Fixture f, string change)
    {
        if (change == "expired") { f.Clock.Advance(TimeSpan.FromSeconds(3)); return; }
        var original = (await f.Bookmarks.FindAsync(Run, Wait))!;
        await f.Bookmarks.DeleteAsync(Run, Wait);
        if (change == "replaced") await f.Bookmarks.SaveAsync(original);
    }
    private sealed class ReceiverAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("native-history")); }
    private sealed class ReceiverControl
    {
        internal string? Failure; internal Action<CancellationToken>? Before; internal Action? After;
        internal Func<RuntimeHistoryEvent, bool>? Match; internal bool PairUnavailable;
        internal List<RuntimeHistoryObservation> Attempts { get; } = [];
        internal void Configure(IServiceCollection services)
        {
            var native = services.Last(descriptor => descriptor.ServiceType == typeof(IRuntimeHistoryObservationWriter));
            services.AddScoped<IRuntimeHistoryObservationWriter>(sp => new ReceiverWriter(
                (IRuntimeHistoryObservationWriter)native.ImplementationFactory!(sp), this));
        }
        private sealed class ReceiverWriter(IRuntimeHistoryObservationWriter inner, ReceiverControl owner) : IRuntimeHistoryObservationWriter
        {
            public bool CanRecordHistory(IRuntimeHistoryStore selectedStore) => !owner.PairUnavailable && inner.CanRecordHistory(selectedStore);
            public async ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
            {
                if (!(owner.Match?.Invoke(observation.Event) ?? IsReceiverObservation(observation.Event))) return await inner.RecordAsync(observation, cancellationToken);
                owner.Attempts.Add(observation); owner.Before?.Invoke(cancellationToken);
                if (owner.Failure == "hard") throw new InvalidDataException("private malformed history", new IOException("private nested IO"));
                if (owner.Attempts.Count == 1 && owner.Failure == "before-commit") throw new IOException("private before-commit IO");
                if (owner.Attempts.Count == 1 && owner.Failure == "false-ack") return new(false, null);
                var result = await inner.RecordAsync(observation, cancellationToken); owner.After?.Invoke();
                if (owner.Attempts.Count == 1 && owner.Failure == "after-commit") throw new IOException("private ambiguous acknowledgement");
                return result;
            }
        }
    }
}

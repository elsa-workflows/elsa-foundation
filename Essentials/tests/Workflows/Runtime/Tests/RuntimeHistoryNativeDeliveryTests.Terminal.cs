using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeHistoryNativeDeliveryTests
{
    [Theory]
    [InlineData("before-commit")] [InlineData("after-commit")] [InlineData("false-ack")]
    public async Task A_terminal_claim_race_keeps_failed_history_owed_until_the_actual_terminal_purger_records_its_discard(string failure)
    {
        var race = new ClaimRaceControl("workflow-terminal"); var recording = new ReceiverControl { Failure = failure };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: services =>
        { race.Configure(services); recording.Configure(services); });
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync();
        var work = Assert.Single(await f.Queue.ListAllAsync(new(Run)));
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Deferred, Assert.Single((await f.DrainAsync()).Items).Status);
        Assert.Equal(work.WorkItemId, Assert.Single(await f.Queue.ListAllAsync(new(Run))).WorkItemId);
        f.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Empty((await f.DrainAsync()).Items); // The normal drainer still forbids terminal activity execution.
        await f.SweepTerminalAsync();
        Assert.Empty(await f.Queue.ListAllAsync(new(Run))); Assert.Equal(0, f.Activator.Calls);
        var events = (await f.History.ReadPageAsync(new(Run))).Items;
        Assert.Equal(failure == "after-commit" ? 1 : 0, events.Count(IsReceiverObservation));
        var discarded = Assert.Single(events, IsTerminalResumeDecision);
        Assert.Equal(work.CommandId, discarded.Causation!.CommandId); Assert.Equal(work.WorkItemId, discarded.Causation.SchedulerWorkItemId);
        Assert.Equal(f.Clock.GetUtcNow(), discarded.OccurredAt);
        Assert.DoesNotContain(events, item => item.Name is "BookmarkConsumed" or "ActivityContinuationStarted");
    }

    [Theory]
    [InlineData("before-commit")] [InlineData("after-commit")] [InlineData("false-ack")]
    public async Task Failed_terminal_discard_recording_keeps_source_work_before_purge_and_reentry_is_another_actual_observation(string failure)
    {
        var recording = new ReceiverControl { Failure = failure, Match = IsTerminalResumeDecision };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: recording.Configure);
        await QueueTerminalResumeAsync(f);
        var work = Assert.Single(await f.Queue.ListAllAsync(new(Run)));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.SweepTerminalAsync());
        Assert.Equal(work.WorkItemId, Assert.Single(await f.Queue.ListAllAsync(new(Run))).WorkItemId);
        Assert.Equal(failure == "after-commit" ? 1 : 0, (await f.History.ReadPageAsync(new(Run))).Items.Count(IsTerminalResumeDecision));
        await f.SweepTerminalAsync();
        Assert.Empty(await f.Queue.ListAllAsync(new(Run))); Assert.Equal(0, f.Activator.Calls);
        var observations = (await f.History.ReadPageAsync(new(Run))).Items.Where(IsTerminalResumeDecision).ToArray();
        Assert.Equal(failure == "after-commit" ? 2 : 1, observations.Length); Assert.Equal(2, recording.Attempts.Count);
        Assert.Equal(observations.Length, observations.Select(item => item.HistoryEventId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(observations, item => Assert.Equal(work.WorkItemId, item.Causation!.SchedulerWorkItemId));
    }

    [Fact]
    public async Task The_original_resumption_constructor_cannot_waive_retained_terminal_delivery_history()
    {
        using var f = await Fixture.CreateAsync("complete", publication: true);
        await QueueTerminalResumeAsync(f);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.SweepTerminalAsync(recording: false));
        Assert.Single(await f.Queue.ListAllAsync(new(Run))); Assert.Equal(0, f.Activator.Calls);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new(Run))).Items, IsTerminalResumeDecision);
        await f.SweepTerminalAsync();
        Assert.Empty(await f.Queue.ListAllAsync(new(Run)));
        Assert.Single((await f.History.ReadPageAsync(new(Run))).Items, IsTerminalResumeDecision);
    }

    [Fact]
    public async Task A_replaced_unpaired_terminal_writer_cannot_turn_recording_into_residual_source_deletion()
    {
        var recording = new ReceiverControl { Match = IsTerminalResumeDecision };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: recording.Configure);
        await QueueTerminalResumeAsync(f); recording.PairUnavailable = true;
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.SweepTerminalAsync());
        Assert.Single(await f.Queue.ListAllAsync(new(Run))); Assert.Empty(recording.Attempts);
        recording.PairUnavailable = false; await f.SweepTerminalAsync();
        Assert.Empty(await f.Queue.ListAllAsync(new(Run))); Assert.Equal(0, f.Activator.Calls);
        Assert.Single((await f.History.ReadPageAsync(new(Run))).Items, IsTerminalResumeDecision);
    }

    [Fact]
    public async Task A_prior_vocabulary_does_not_gain_terminal_discard_observations()
    {
        using var f = await Fixture.CreateAsync("complete", publication: true, transformPolicy: policy => new(
            policy.PolicyId, policy.Version, policy.Required, policy.SupportedKinds.Where(kind => kind != "DeliveryUnmatched").ToArray()));
        await QueueTerminalResumeAsync(f); await f.SweepTerminalAsync();
        Assert.Empty(await f.Queue.ListAllAsync(new(Run))); Assert.Equal(0, f.Activator.Calls);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new(Run))).Items, IsTerminalResumeDecision);
    }

    [Fact]
    public async Task Malformed_terminal_history_stays_hard_and_does_not_purge_its_source()
    {
        var recording = new ReceiverControl { Failure = "hard", Match = IsTerminalResumeDecision };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: recording.Configure);
        await QueueTerminalResumeAsync(f);
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => f.SweepTerminalAsync());
        Assert.Single(await f.Queue.ListAllAsync(new(Run))); Assert.Equal(0, f.Activator.Calls);
        Assert.DoesNotContain((await f.History.ReadPageAsync(new(Run))).Items, IsTerminalResumeDecision);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Requested_terminal_observation_cancellation_preserves_original_identity_and_queued_work(bool wrapped)
    {
        using var source = new CancellationTokenSource();
        var original = new OperationCanceledException("private cancellation", source.Token);
        var recording = new ReceiverControl { Match = IsTerminalResumeDecision, Before = _ =>
        { source.Cancel(); if (wrapped) throw new RuntimeHistoryUnavailableException(Run, "terminal-observation", original); throw original; } };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: recording.Configure);
        await QueueTerminalResumeAsync(f);
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.SweepTerminalAsync(cancellationToken: source.Token));
        Assert.Same(original, failure); Assert.Equal(source.Token, failure.CancellationToken);
        Assert.Single(await f.Queue.ListAllAsync(new(Run))); Assert.Equal(0, f.Activator.Calls);
    }

    [Fact]
    public async Task Access_drift_after_terminal_append_stops_source_purge_under_the_changed_context()
    {
        var access = new ReceiverAccess(); var original = access.Current;
        var recording = new ReceiverControl { Match = IsTerminalResumeDecision,
            After = () => access.Current = PersistenceAccessContext.Scoped(new("foreign")) };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: services =>
        { services.AddSingleton<IPersistenceAccessContextAccessor>(access); recording.Configure(services); });
        await QueueTerminalResumeAsync(f);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => f.SweepTerminalAsync());
        access.Current = original;
        Assert.Single(await f.Queue.ListAllAsync(new(Run))); Assert.Equal(0, f.Activator.Calls);
        Assert.Single((await f.History.ReadPageAsync(new(Run))).Items, IsTerminalResumeDecision);
    }

    private static bool IsTerminalResumeDecision(RuntimeHistoryEvent item) => item.Name == "DeliveryUnmatched" &&
        item.Metadata.GetValueOrDefault("history.reasonCode") == "terminal-resume-ineligible";
    private static async Task QueueTerminalResumeAsync(Fixture f)
    {
        await f.SeedPublisherAsync(); await f.PublishAsync(); await f.ProcessPublicationsAsync();
        var workflows = f.Services.GetRequiredService<IWorkflowExecutionStateStore>();
        var execution = (await workflows.FindAsync(Run))!;
        await workflows.SaveAsync(execution with { Status = WorkflowExecutionStatus.Completed });
        Assert.Empty((await f.DrainAsync()).Items); Assert.Equal(0, f.Activator.Calls);
    }
    private sealed partial class Fixture
    {
        internal async Task SweepTerminalAsync(bool recording = true, CancellationToken cancellationToken = default)
        {
            var sp = scope.ServiceProvider;
            var service = recording ? ActivatorUtilities.CreateInstance<RuntimeResumptionService>(sp) : new RuntimeResumptionService(
                sp.GetRequiredService<IRuntimePostCommitOutboxProcessor>(), Queue, sp.GetRequiredService<IRuntimeRecoveryScanner>(),
                sp.GetRequiredService<IWorkflowExecutionActorProvider>(), sp.GetRequiredService<IRuntimeExecutionIdGenerator>(), clock,
                sp.GetRequiredService<IWorkflowExecutionStateStore>(), sp.GetRequiredService<IWorkflowSchedulerPauseGate>(),
                sp.GetRequiredService<RuntimeResumptionDiscoveryStateStore>(), sp.GetRequiredService<IExecutionLivenessStateStore>(),
                persistenceAccessContextAccessor: sp.GetRequiredService<IPersistenceAccessContextAccessor>());
            await service.SweepAsync(new(outboxBatchSize: 10, backlogBatchSize: 10, recoveryScanBatchSize: 10), cancellationToken);
        }
    }
}

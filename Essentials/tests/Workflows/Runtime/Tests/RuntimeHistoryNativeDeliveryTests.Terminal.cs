using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        AssertTerminalFailure(await f.SweepTerminalAsync());
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
        AssertTerminalFailure(await f.SweepTerminalAsync(recording: false));
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
        AssertTerminalFailure(await f.SweepTerminalAsync());
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
        var recording = new ReceiverControl { Failure = "hard", Match = IsTerminalResumeDecision }; var log = new TerminalFailureLog();
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: services =>
        { recording.Configure(services); services.AddSingleton<ILogger<RuntimeResumptionService>>(log); });
        await QueueTerminalResumeAsync(f);
        var result = await f.SweepTerminalAsync(); AssertTerminalFailure(result, "runtime.history.integrity");
        var retained = Assert.IsType<RuntimeHistoryIntegrityException>(Assert.Single(log.Exceptions));
        Assert.IsType<IOException>(Assert.IsType<InvalidDataException>(retained.InnerException).InnerException);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result.Dispatches));
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

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_persistently_blocked_terminal_history_owner_cannot_hold_the_bounded_backlog_walk_ahead_of_healthy_legacy_work(bool healthyTerminal)
    {
        const string healthy = "z-healthy";
        var recording = new ReceiverControl { Match = IsTerminalResumeDecision };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: recording.Configure);
        await QueueTerminalResumeAsync(f);
        var owed = Assert.Single(await f.Queue.ListAllAsync(new(Run)));
        await SeedHealthyLegacyWorkAsync(f, healthy, healthyTerminal); recording.PairUnavailable = true;
        var blockedObserved = false; var healthyProgressed = false;
        for (var pass = 0; pass < 8 && !(blockedObserved && healthyProgressed); pass++)
        {
            var result = await f.SweepTerminalAsync(request: new(outboxBatchSize: 10, backlogBatchSize: 1,
                recoveryScanBatchSize: 1, maxExecutionsPerSweep: 1));
            blockedObserved |= result.Dispatches.Any(item => item.WorkflowExecutionId == Run &&
                item.Outcome == RuntimeResumptionDispatchOutcome.Faulted && item.Failure == "runtime.history.capture-unavailable");
            healthyProgressed |= healthyTerminal ? (await f.Queue.ListAllAsync(new(healthy))).Count == 0 :
                result.Dispatches.Any(item => item.WorkflowExecutionId == healthy &&
                    item.Outcome is RuntimeResumptionDispatchOutcome.Accepted or RuntimeResumptionDispatchOutcome.Duplicate);
            Assert.DoesNotContain(result.Dispatches, item => item.WorkflowExecutionId == healthy && item.Outcome == RuntimeResumptionDispatchOutcome.Faulted);
        }
        Assert.True(blockedObserved); Assert.True(healthyProgressed);
        Assert.Equal(owed.WorkItemId, Assert.Single(await f.Queue.ListAllAsync(new(Run))).WorkItemId);
        Assert.Equal(0, f.Activator.Calls); Assert.DoesNotContain((await f.History.ReadPageAsync(new(Run))).Items, IsTerminalResumeDecision);
    }

    [Fact]
    public async Task Held_terminal_history_failure_is_reported_without_blocking_healthy_cleanup_or_claiming_the_failed_run_was_purged()
    {
        const string healthy = "z-healthy";
        var recording = new ReceiverControl { Match = IsTerminalResumeDecision };
        using var f = await Fixture.CreateAsync("complete", publication: true, configure: recording.Configure);
        await QueueTerminalResumeAsync(f); var owed = Assert.Single(await f.Queue.ListAllAsync(new(Run)));
        await f.Queue.EnqueueAsync(new("held-head", Run, "held-command", WorkflowExecutionCommandKind.StartActivity,
            "held-envelope", "held-key", At.AddSeconds(-1), At.AddSeconds(-1)));
        await f.Services.GetRequiredService<IWorkflowHoldStateStore>().SaveAsync(new("control-held", Run,
            activeHolds: [WorkflowHold.ForWorkflowExecution("hold", Run, At, "operator", "Paused")]));
        await SeedHealthyLegacyWorkAsync(f, healthy, terminal: true); recording.PairUnavailable = true;
        var result = await f.SweepTerminalAsync();
        var failed = Assert.Single(result.Dispatches, item => item.WorkflowExecutionId == Run);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Faulted, failed.Outcome); Assert.Equal("runtime.history.capture-unavailable", failed.Failure);
        Assert.Equal(1, result.TerminalExecutionsPurged); Assert.Equal(2, result.PurgedWorkItemCount);
        Assert.Empty(await f.Queue.ListAllAsync(new(healthy)));
        Assert.Equal(owed.WorkItemId, Assert.Single(await f.Queue.ListAllAsync(new(Run))).WorkItemId); Assert.Equal(0, f.Activator.Calls);
    }

    private static async Task SeedHealthyLegacyWorkAsync(Fixture f, string id, bool terminal)
    {
        var workflows = f.Services.GetRequiredService<IWorkflowExecutionStateStore>();
        var template = (await workflows.FindAsync(Run))!;
        await workflows.SaveAsync(template with { WorkflowExecutionId = id,
            Status = terminal ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running,
            HistoryCapturePolicy = null, HistoryCaptureEnabledAtAdmission = false, RunKind = WorkflowRunKind.Unknown,
            Partition = f.Services.GetService<IWorkflowExecutionPartitionAccessor>()?.Current ?? new(WorkflowExecutionPartition.DefaultValue) });
        await f.Queue.EnqueueAsync(new("healthy-head", id, "healthy-command", WorkflowExecutionCommandKind.RunSchedulerWork,
            "healthy-envelope", "healthy-key", At, At));
    }

    private static void AssertTerminalFailure(RuntimeResumptionSweepResult result, string code = "runtime.history.capture-unavailable")
    {
        var failure = Assert.Single(result.Dispatches, item => item.WorkflowExecutionId == Run);
        Assert.Equal(RuntimeResumptionDispatchOutcome.Faulted, failure.Outcome); Assert.Equal(code, failure.Failure); Assert.Null(failure.EnvelopeId);
        Assert.Equal(0, result.TerminalExecutionsPurged);
    }
    private sealed class TerminalFailureLog : ILogger<RuntimeResumptionService>
    {
        internal List<Exception> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (exception is not null) Exceptions.Add(exception); }
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
        internal async Task<RuntimeResumptionSweepResult> SweepTerminalAsync(bool recording = true, CancellationToken cancellationToken = default,
            RuntimeResumptionSweepRequest? request = null)
        {
            var sp = scope.ServiceProvider;
            var service = recording ? ActivatorUtilities.CreateInstance<RuntimeResumptionService>(sp) : new RuntimeResumptionService(
                sp.GetRequiredService<IRuntimePostCommitOutboxProcessor>(), Queue, sp.GetRequiredService<IRuntimeRecoveryScanner>(),
                sp.GetRequiredService<IWorkflowExecutionActorProvider>(), sp.GetRequiredService<IRuntimeExecutionIdGenerator>(), clock,
                sp.GetRequiredService<IWorkflowExecutionStateStore>(), sp.GetRequiredService<IWorkflowSchedulerPauseGate>(),
                sp.GetRequiredService<RuntimeResumptionDiscoveryStateStore>(), sp.GetRequiredService<IExecutionLivenessStateStore>(),
                persistenceAccessContextAccessor: sp.GetRequiredService<IPersistenceAccessContextAccessor>());
            return await service.SweepAsync(request ?? new(outboxBatchSize: 10, backlogBatchSize: 10, recoveryScanBatchSize: 10), cancellationToken);
        }
    }
}

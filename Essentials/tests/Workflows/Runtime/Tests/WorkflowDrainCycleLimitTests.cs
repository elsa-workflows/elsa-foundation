using System.Text.Json;
using Elsa.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The drain cycle limits of <see cref="WorkflowDrainOrchestrator"/> (#2661).
/// <see cref="WorkflowDrainOrchestratorOptions.MaxCyclesWithoutProgress"/> counts the consecutive cycles that dispatch no
/// work item the drain had not dispatched before, so a drain that keeps advancing is not cut off however long it runs, and
/// one that keeps cycling without new work still fails. <see cref="WorkflowDrainOrchestratorOptions.MaxTotalDrainCycles"/>
/// counts every cycle when set, and past <see cref="WorkflowDrainOrchestratorOptions.DrainCycleWarningThreshold"/> each
/// drain logs one warning. The drainer double refuses to run past <see cref="RunawayCalls"/> calls, so a limit that lets a
/// runaway through fails a test instead of hanging it.
/// </summary>
public sealed class WorkflowDrainCycleLimitTests
{
    private const int Limit = 4;
    private const int RunawayCalls = 10_000;
    private const int DefaultThreshold = WorkflowDrainOrchestratorOptions.DefaultDrainCycleWarningThreshold;
    private const string ExecutionId = "wfexec-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowExecutionCommandEnvelope Envelope = NewEnvelope();

    private readonly RecordingLogger<WorkflowDrainOrchestrator> _logger = new();

    [Fact]
    public async Task A_drain_that_dispatches_the_same_work_item_every_cycle_fails_after_MaxCyclesWithoutProgress_cycles()
    {
        // The redelivery runaway: the outbox reports a delivery every cycle, and every cycle dispatches the work item the
        // first one did. A dispatch is progress only when its work item is new to the drain.
        var drainer = new ScriptedDrainer(_ => ["work-same"]);

        var exception = await Assert.ThrowsAsync<DrainCycleLimitExceededException>(() => DrainAsync(drainer, _ => true));

        Assert.Equal(DrainCycleLimit.CyclesWithoutProgress, exception.Limit);
        Assert.Equal(Limit, exception.MaxCycles);
        Assert.Equal(Limit + 1, exception.Cycles);
        Assert.Equal(Limit + 1, drainer.Calls);
    }

    [Fact]
    public async Task A_drain_that_dispatches_new_work_every_cycle_runs_far_past_MaxCyclesWithoutProgress_and_quiesces()
    {
        const int cycles = Limit * 10;
        var drainer = new ScriptedDrainer(call => call < cycles ? [$"work-{call}"] : []);

        var result = await DrainAsync(drainer, call => call < cycles - 1);

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(cycles, result.Items.Count);
    }

    [Fact]
    public async Task A_cycle_with_progress_resets_the_count_of_cycles_without_progress()
    {
        // Progress in every Limit-th cycle leaves Limit - 1 cycles without progress in a row, never Limit.
        const int cycles = Limit * 5;
        var drainer = new ScriptedDrainer(call => call % Limit == 0 ? [$"work-{call}"] : []);

        var result = await DrainAsync(drainer, call => call < cycles - 1);

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(cycles, drainer.Calls);
    }

    [Fact]
    public async Task MaxTotalDrainCycles_ends_a_drain_that_keeps_making_progress()
    {
        var drainer = new ScriptedDrainer(call => [$"work-{call}"]);

        var exception = await Assert.ThrowsAsync<DrainCycleLimitExceededException>(
            () => DrainAsync(drainer, _ => true, Options(maxTotalDrainCycles: Limit * 3)));

        Assert.Equal(DrainCycleLimit.TotalCycles, exception.Limit);
        Assert.Equal(Limit * 3, exception.MaxCycles);
        Assert.Equal(Limit * 3, exception.Cycles);
        Assert.Equal(Limit * 3, drainer.Calls);
        Assert.Contains("total cycle limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_drain_that_settles_within_MaxTotalDrainCycles_completes()
    {
        const int cycles = Limit * 3;
        var drainer = new ScriptedDrainer(call => [$"work-{call}"]);

        var result = await DrainAsync(drainer, call => call < cycles - 1, Options(maxTotalDrainCycles: cycles));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, result.StopReason);
        Assert.Equal(cycles, drainer.Calls);
    }

    [Theory]
    [InlineData(DefaultThreshold, 0)]
    [InlineData(DefaultThreshold + 1, 1)]
    [InlineData(DefaultThreshold * 3, 1)]
    public async Task A_drain_logs_one_warning_once_it_passes_the_default_warning_threshold(int cycles, int warnings)
    {
        await DrainAsync(new ScriptedDrainer(call => [$"work-{call}"]), call => call < cycles - 1);

        Assert.Equal(warnings, _logger.Warnings.Count());
    }

    [Fact]
    public async Task Each_drain_past_the_warning_threshold_logs_its_own_warning_naming_its_execution_and_cycle()
    {
        // Two drains of one orchestrator, each running past the threshold. The outbox stops delivering at the end of each.
        const int cyclesPerDrain = Limit * 3;
        var orchestrator = NewOrchestrator(
            new ScriptedDrainer(call => [$"work-{call}"]),
            call => (call + 1) % cyclesPerDrain != 0,
            Options(drainCycleWarningThreshold: Limit));

        await DrainAsync(orchestrator);
        await DrainAsync(orchestrator);

        var warnings = _logger.Warnings.ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.All(warnings, warning =>
        {
            Assert.Equal(68117, warning.EventId.Id);
            Assert.Equal<object?>(ExecutionId, warning.Fields["WorkflowExecutionId"]);
            Assert.Equal<object?>(Limit + 1, warning.Fields["Cycle"]);
            Assert.Equal<object?>(Limit, warning.Fields["DrainCycleWarningThreshold"]);
        });
    }

    [Fact]
    public async Task A_null_warning_threshold_logs_no_warning()
    {
        const int cycles = DefaultThreshold * 2;

        await DrainAsync(new ScriptedDrainer(call => [$"work-{call}"]), call => call < cycles - 1, Options(drainCycleWarningThreshold: null));

        Assert.Empty(_logger.Warnings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Each_cycle_limit_and_the_warning_threshold_must_be_positive(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowDrainOrchestratorOptions(maxCyclesWithoutProgress: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowDrainOrchestratorOptions(maxTotalDrainCycles: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowDrainOrchestratorOptions(drainCycleWarningThreshold: value));
    }

    [Fact]
    public void The_options_the_container_builds_limit_only_cycles_without_progress_and_warn_past_1000_cycles()
    {
        // The runtime registers the options for the container to construct, as here.
        using var services = new ServiceCollection().AddSingleton<WorkflowDrainOrchestratorOptions>().BuildServiceProvider();

        var options = services.GetRequiredService<WorkflowDrainOrchestratorOptions>();

        Assert.Equal(64, options.MaxCyclesWithoutProgress);
        Assert.Null(options.MaxTotalDrainCycles);
        Assert.Equal(1000, options.DrainCycleWarningThreshold);
    }

    private static WorkflowDrainOrchestratorOptions Options(int? maxTotalDrainCycles = null, int? drainCycleWarningThreshold = DefaultThreshold) =>
        new(
            maxCyclesWithoutProgress: Limit,
            outboxDeliveryBatchSize: 1,
            maxTotalDrainCycles: maxTotalDrainCycles,
            drainCycleWarningThreshold: drainCycleWarningThreshold);

    private WorkflowDrainOrchestrator NewOrchestrator(ScriptedDrainer drainer, Func<int, bool> delivers, WorkflowDrainOrchestratorOptions? options = null)
    {
        var claims = new InMemoryRuntimeCheckpointCommitStore();
        return new WorkflowDrainOrchestrator(
            drainer,
            new ScriptedOutbox(delivers),
            [],
            TestCheckpointRuleViolationFaulter.Create(new FakeTimeProvider(Now)),
            new RuntimeExecutionOwnershipService(new InMemoryExecutionLivenessStateStore()),
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            claims,
            claims,
            new InMemoryWorkflowSchedulerWorkQueue(),
            options ?? Options(),
            NonFoldingDrainScopeFactory.Instance,
            persistenceModeResolver: NonFoldingDrainScopeFactory.Resolver,
            logger: _logger);
    }

    private Task<RuntimeSchedulerDrainResult> DrainAsync(ScriptedDrainer drainer, Func<int, bool> delivers, WorkflowDrainOrchestratorOptions? options = null) =>
        DrainAsync(NewOrchestrator(drainer, delivers, options));

    private static async Task<RuntimeSchedulerDrainResult> DrainAsync(WorkflowDrainOrchestrator orchestrator) =>
        await orchestrator.DrainAsync(Envelope, new RuntimeSchedulerDrainRequest(ExecutionId));

    private static WorkflowExecutionCommandEnvelope NewEnvelope()
    {
        var command = new WorkflowExecutionCommand(
            CommandId: "command-1",
            WorkflowExecutionId: ExecutionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: Now,
            Payload: JsonSerializer.SerializeToElement(new { workItemId = "work-1" }),
            Metadata: new Dictionary<string, string>());
        return new(
            envelopeId: "envelope-1",
            workflowExecutionId: ExecutionId,
            command: command,
            idempotencyKey: $"{ExecutionId}:command-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            sequence: 1,
            metadata: new Dictionary<string, string>());
    }

    /// <summary>Dispatches the work items its script names for each call, counting calls from zero across drains.</summary>
    private sealed class ScriptedDrainer(Func<int, string[]> script) : IWorkflowSchedulerDrainer
    {
        public int Calls { get; private set; }

        public ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
        {
            if (Calls >= RunawayCalls)
                throw new InvalidOperationException($"The drain ran {RunawayCalls} cycles: a drain cycle limit let a runaway through.");

            var items = script(Calls++)
                .Select(workItemId => new RuntimeSchedulerWorkItemResult(
                    workItemId: workItemId,
                    workflowExecutionId: request.WorkflowExecutionId,
                    commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
                    status: RuntimeSchedulerWorkItemResultStatus.Completed,
                    handlerName: nameof(ScriptedDrainer),
                    startedAt: Now,
                    completedAt: Now))
                .ToArray();
            return ValueTask.FromResult(new RuntimeSchedulerDrainResult(request.WorkflowExecutionId, Now, Now, items));
        }
    }

    /// <summary>Reports one delivered continuation on each call its predicate allows, counting calls from zero.</summary>
    private sealed class ScriptedOutbox(Func<int, bool> delivers) : IRuntimePostCommitOutboxProcessor
    {
        private int _calls;

        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimePostCommitOutboxProcessResult(delivers(_calls++)
                ? [new RuntimePostCommitOutboxProcessedItem($"outbox-{_calls}", $"intent-{_calls}", RuntimePostCommitOutboxStatus.Delivered, null)]
                : []));
    }
}

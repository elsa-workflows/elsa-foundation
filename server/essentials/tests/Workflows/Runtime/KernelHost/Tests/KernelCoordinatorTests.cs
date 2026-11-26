using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Kernel.WorkflowActor;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;
using Native = Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>
/// The kernel coordinator on the old runtime's in-memory slot authority, switch, projection stores and executable store, with
/// a recording schedule host in place of the schedule actors (the Sim's schedule checks run the actor itself).
/// </summary>
public sealed class KernelCoordinatorTests
{
    private sealed class RecordingSchedules : IScheduleActors
    {
        public List<string> Calls { get; } = [];
        public Dictionary<string, Models.WorkflowStart?> Runs { get; } = new(StringComparer.Ordinal);
        public string? FailSet { get; set; }

        public ValueTask RegisterRunAsync(string startKey, Models.WorkflowStart? run, CancellationToken cancellationToken = default)
        {
            Calls.Add(run is null ? $"unregister {startKey}" : $"register {startKey} {run.Workflow}");
            Runs[startKey] = run;
            return ValueTask.CompletedTask;
        }

        public ValueTask SetAsync(string key, long revision, RecurringSchedule schedule, string startKey, CancellationToken cancellationToken = default)
        {
            if (FailSet == key)
            {
                // It fails once: the compensation that follows reaches it.
                FailSet = null;
                throw new InvalidOperationException($"The schedule actor '{key}' is unavailable.");
            }
            Calls.Add($"set {key} {revision} {schedule.Kind}:{schedule.Value}");
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(string key, long revision, CancellationToken cancellationToken = default)
        {
            Calls.Add($"stop {key} {revision}");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A trigger observer, as Studio's workflow library is one: it records each refresh, and can fail.</summary>
    private sealed class RecordingObserver : IWorkflowTriggerIndexObserver
    {
        public List<string> Refreshes { get; } = [];
        public bool Fail { get; set; }

        public ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new InvalidOperationException("The workflow library is unavailable.");
            Refreshes.Add($"{snapshot.DefinitionId} {snapshot.ArtifactId} {snapshot.RequiresProjectionRefresh} {snapshot.Bindings.Count}");
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A switch that holds an activation's slot move until it is released, so another can start meanwhile.</summary>
    private sealed class GatedSwitch(IWorkflowActivationSwitch inner) : IWorkflowActivationSwitch
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task;
            return await inner.TryActivateAsync(request, cancellationToken);
        }

        public ValueTask<bool> TryRevertAsync(WorkflowActivationRevert revert, CancellationToken cancellationToken = default) => inner.TryRevertAsync(revert, cancellationToken);
        public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(WorkflowDeactivationSlotRequest request, IReadOnlyCollection<string> alsoServing,
            CancellationToken cancellationToken = default) => inner.TryDeactivateAsync(request, alsoServing, cancellationToken);
        public ValueTask<bool> TryDiscardAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) => inner.TryDiscardAsync(reference, cancellationToken);
        public ValueTask<bool> TryRepairAsync(WorkflowActivationSlot slot, IReadOnlyCollection<string> alsoServing, CancellationToken cancellationToken = default) =>
            inner.TryRepairAsync(slot, alsoServing, cancellationToken);
    }

    /// <summary>A switch whose deactivation commits and then loses its answer, as a provider that drops its connection while committing does.</summary>
    private sealed class LosesDeactivationAnswer(IWorkflowActivationSwitch inner) : IWorkflowActivationSwitch
    {
        public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) =>
            inner.TryActivateAsync(request, cancellationToken);
        public ValueTask<bool> TryRevertAsync(WorkflowActivationRevert revert, CancellationToken cancellationToken = default) => inner.TryRevertAsync(revert, cancellationToken);

        public async ValueTask<WorkflowActivationTransition> TryDeactivateAsync(WorkflowDeactivationSlotRequest request, IReadOnlyCollection<string> alsoServing,
            CancellationToken cancellationToken = default)
        {
            await inner.TryDeactivateAsync(request, alsoServing, cancellationToken);
            throw new TimeoutException("The connection dropped after the commit.");
        }

        public ValueTask<bool> TryDiscardAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) => inner.TryDiscardAsync(reference, cancellationToken);
        public ValueTask<bool> TryRepairAsync(WorkflowActivationSlot slot, IReadOnlyCollection<string> alsoServing, CancellationToken cancellationToken = default) =>
            inner.TryRepairAsync(slot, alsoServing, cancellationToken);
    }

    /// <summary>One host's stores, and a coordinator over them; <see cref="Restart"/> keeps the stores and starts a new host.</summary>
    private sealed class Host
    {
        public readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 11, 25, 9, 0, 0, TimeSpan.Zero));
        public readonly InMemoryWorkflowExecutableStore Executables = new();
        public readonly InMemoryWorkflowActivationAuthority Authority = new();
        public readonly InMemoryWorkflowTriggerBindingStore Bindings = new();
        public readonly InMemoryRecurringTriggerScheduleStore RecurringSchedules = new();
        public readonly InMemoryWorkflowExecutableSourceReferenceStore References;
        public readonly InMemoryWorkflowActivationSwitch Switch;
        public KernelServing Serving = null!;
        public RecordingSchedules Schedules = null!;
        public KernelActivationCoordinator Coordinator = null!;
        public RecordingObserver Observer = null!;
        private readonly ActivationChannelOptions? _channels;
        private KernelHostCapabilities _capabilities;

        /// <param name="capabilities">What the host's foundation runs; a workflow that needs more is refused where it is mapped.</param>
        public Host(ActivationChannelOptions? channels = null, KernelHostCapabilities? capabilities = null)
        {
            _channels = channels;
            _capabilities = capabilities ?? KernelHostCapabilities.All;
            References = new(Executables, Clock);
            Switch = new(Authority, References, Clock, Bindings, RecurringSchedules);
            Restart();
        }

        /// <summary>A restart onto a host whose foundation runs <paramref name="capabilities"/>.</summary>
        public void Restart(KernelHostCapabilities capabilities)
        {
            _capabilities = capabilities;
            Restart();
        }

        public void Restart()
        {
            Serving = new KernelServing(expression => expression == "0 * * * * *" ? null : "not a cron expression this host reads", _capabilities);
            Schedules = new RecordingSchedules();
            Observer = new RecordingObserver();
            Coordinator = NewCoordinator(Switch);
        }

        /// <summary>Another coordinator over these stores and this host's serving state, as a second shell composes one.</summary>
        public KernelActivationCoordinator NewCoordinator(IWorkflowActivationSwitch activationSwitch, IScheduleActors? schedules = null) =>
            new(Authority, activationSwitch, References,
                new WorkflowExecutableRootWriteLeaseManager(Executables, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), Clock),
                Executables, Bindings, Serving, schedules ?? Schedules, Clock, RecurringSchedules, _channels, [Observer]);

        public async Task<WorkflowActivationResult> ActivateAsync(WorkflowExecutable executable, string activationId, string slot = KernelServing.DefaultSlotName,
            KernelActivationCoordinator? coordinator = null)
        {
            await Executables.SaveAsync(executable);
            var current = await Authority.FindAsync(executable.Identity.DefinitionId, slot);
            var identity = executable.Identity;
            var reference = new WorkflowExecutableSourceReference("candidate", identity.ArtifactId, "definition", identity.DefinitionId, "1",
                identity.DefinitionId, identity.DefinitionVersionId, identity.ArtifactVersion, Clock.GetUtcNow(), Clock.GetUtcNow(), WorkflowExecutableReferenceScope.Published);
            return await (coordinator ?? Coordinator).ActivateAsync(new WorkflowActivationCommand(executable, reference, slot, activationId, WorkflowActivationSource.Publishing,
                current?.Revision ?? 0));
        }

        public async Task<WorkflowActivationResult> DeactivateAsync(WorkflowExecutable executable, string slot = KernelServing.DefaultSlotName)
        {
            var current = await Authority.FindAsync(executable.Identity.DefinitionId, slot);
            return await Coordinator.DeactivateAsync(new WorkflowDeactivationCommand(executable, slot, WorkflowActivationSource.Publishing, current?.Revision ?? 0));
        }

        public Task<WorkflowExecutableSourceReference?> ReferenceAsync(string activationId) =>
            References.FindAsync(WorkflowActivationReferenceIdentity.Create(activationId)).AsTask();
    }

    private static ExecutableNode HttpWorkflow(string path, string text) => Sequence("root",
        Node("in", Types.HttpEndpoint, [Literal("Path", path, "String"), Literal("SupportedMethods", new[] { "POST" }), Literal("CanStartWorkflow", true, "Boolean")]),
        Node("out", Types.WriteHttpResponse, [Literal("Body", text, "String")]));

    private static ExecutableNode TimerWorkflow(string interval) => Sequence("root",
        Node("tick", Types.Timer, [Literal("Interval", interval, "String")]),
        WriteLine("after", Literal("text", "ticked")));

    private static WorkflowExecutable Orders(string artifact, string text = "ok") => ExecutableOf(artifact, "orders", HttpWorkflow("orders", text));
    private static WorkflowExecutable Ticks(string artifact, string interval = "PT1M") => ExecutableOf(artifact, "ticks", TimerWorkflow(interval));

    [Fact]
    public async Task An_activation_serves_its_workflow_routes_and_schedules()
    {
        var host = new Host();

        var http = await host.ActivateAsync(Orders("orders@1"), "orders-1");
        var timer = await host.ActivateAsync(Ticks("ticks@1"), "ticks-1");

        Assert.Equal(WorkflowActivationOutcome.Activated, http.Outcome);
        Assert.Equal(WorkflowActivationOutcome.Activated, timer.Outcome);
        Assert.True(host.Serving.Registry.Contains("orders@1"));
        Assert.True(host.Serving.Registry.Contains("ticks@1"));
        Assert.Equal("orders", host.Serving.Registry.Get("orders@1").Definition);
        Assert.True(host.Serving.Routes.TryStart("POST", "orders", out var route) && route == ("orders@1", "in"));
        Assert.Equal("orders@1", host.Serving.ServedWorkflow("orders").Workflow);
        Assert.Equal(
            ["register schedule-start:ticks:tick ticks@1", $"set schedule:ticks:tick {timer.Slot.Revision} interval:PT1M"],
            host.Schedules.Calls);
        // The run each occurrence starts carries its definition and begins at the trigger.
        Assert.Equal(("ticks@1", "ticks"), (host.Schedules.Runs["schedule-start:ticks:tick"]!.Workflow, host.Schedules.Runs["schedule-start:ticks:tick"]!.Definition));
        // The switch committed the slot with empty projections, so the activation serves through every store.
        Assert.Equal(WorkflowActivationProjectionState.Active, await host.Bindings.FindActivationStateAsync("orders-1"));
        Assert.Equal(WorkflowActivationProjectionState.Active, await host.RecurringSchedules.FindActivationStateAsync("ticks-1"));
        Assert.Empty((await host.Bindings.ListAllByActivationAsync("orders-1")));
    }

    [Fact]
    public async Task A_republish_replaces_the_route_and_points_the_schedule_at_the_new_artifact()
    {
        var host = new Host();
        await host.ActivateAsync(Orders("orders@1", "first"), "orders-1");
        await host.ActivateAsync(Ticks("ticks@1"), "ticks-1");
        host.Schedules.Calls.Clear();

        var orders = await host.ActivateAsync(Orders("orders@2", "second"), "orders-2");
        var ticks = await host.ActivateAsync(Ticks("ticks@2", "PT5M"), "ticks-2");

        Assert.Equal(WorkflowActivationOutcome.Activated, orders.Outcome);
        Assert.Equal("orders-1", orders.ReplacedActivationId);
        Assert.True(host.Serving.Routes.TryStart("POST", "orders", out var route) && route == ("orders@2", "in"));
        // The registry keeps the version instances may still run.
        Assert.True(host.Serving.Registry.Contains("orders@1"));
        Assert.Equal(WorkflowActivationOutcome.Activated, ticks.Outcome);
        Assert.Equal(
            ["register schedule-start:ticks:tick ticks@2", $"set schedule:ticks:tick {ticks.Slot.Revision} interval:PT5M"],
            host.Schedules.Calls);
        Assert.Equal("activation-replaced", (await host.ReferenceAsync("orders-1"))!.DeletedReason);
    }

    [Fact]
    public async Task A_mapping_refusal_fails_at_the_mapping_step_before_anything_is_minted()
    {
        var host = new Host();
        var refused = ExecutableOf("bpmn@1", "bpmn", Sequence("root", Node("comp", Types.CompensationScope, structure: Structure("elsa.compensation-scope.structure", new { }))));

        var result = await host.ActivateAsync(refused, "bpmn-1");

        Assert.Equal((false, WorkflowActivationOutcome.Failed, WorkflowActivationStep.Mapping), (result.Succeeded, result.Outcome, result.FailedStep));
        Assert.StartsWith("activity-unsupported: Node 'comp'", result.Diagnostic, StringComparison.Ordinal);
        Assert.Null(await host.ReferenceAsync("bpmn-1"));
        Assert.Equal(WorkflowActivationProjectionState.Missing, await host.Bindings.FindActivationStateAsync("bpmn-1"));
        Assert.Null(result.Slot.ActiveActivationId);
        Assert.False(host.Serving.Registry.Contains("bpmn@1"));
    }

    [Fact]
    public async Task A_route_another_definition_serves_refuses_the_publish()
    {
        var host = new Host();
        await host.ActivateAsync(Orders("orders@1"), "orders-1");

        var clash = await host.ActivateAsync(ExecutableOf("copy@1", "copy", HttpWorkflow("orders", "copy")), "copy-1");

        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.ServingCheck), (clash.Outcome, clash.FailedStep));
        Assert.StartsWith("Definitions 'copy' and 'orders' both start on POST /orders", clash.Diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("@1", clash.Diagnostic, StringComparison.Ordinal);
        Assert.Null(await host.ReferenceAsync("copy-1"));
        Assert.True(host.Serving.Routes.TryStart("POST", "orders", out var route) && route == ("orders@1", "in"));
    }

    [Fact]
    public async Task A_timer_away_from_where_a_run_begins_refuses_the_publish()
    {
        var host = new Host();
        var midway = ExecutableOf("late@1", "late", Sequence("root", WriteLine("first", Literal("text", "x")), Node("tick", Types.Timer, [Literal("Interval", "PT1M", "String")])));

        var result = await host.ActivateAsync(midway, "late-1");

        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.ServingCheck), (result.Outcome, result.FailedStep));
        Assert.Contains("use a Delay", result.Diagnostic, StringComparison.Ordinal);
        Assert.Empty(host.Schedules.Calls);
    }

    [Fact]
    public async Task An_unchanged_artifact_is_already_active()
    {
        var host = new Host();
        var first = await host.ActivateAsync(Orders("orders@1"), "orders-1");

        var again = await host.ActivateAsync(Orders("orders@1"), "orders-1b");

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, again.Outcome);
        Assert.Equal(first.Slot.Revision, again.Slot.Revision);
        Assert.Null(await host.ReferenceAsync("orders-1b"));
    }

    [Fact]
    public async Task A_channel_the_shell_does_not_declare_is_refused()
    {
        var host = new Host(new ActivationChannelOptions { AllowAdHocChannels = false, Channels = [new() { Name = KernelServing.DefaultSlotName }] });

        var preview = await host.ActivateAsync(Orders("orders@1"), "orders-1", slot: "preview");

        Assert.Equal((WorkflowActivationOutcome.ChannelNotAllowed, WorkflowActivationStep.ChannelValidation), (preview.Outcome, preview.FailedStep));
        Assert.Empty(host.Serving.Slots);
        Assert.Equal(WorkflowActivationOutcome.Activated, (await host.ActivateAsync(Orders("orders@1"), "orders-1")).Outcome);
    }

    [Fact]
    public async Task A_deactivation_stops_the_schedule_then_unregisters_its_run()
    {
        var host = new Host();
        await host.ActivateAsync(Orders("orders@1"), "orders-1");
        await host.ActivateAsync(Ticks("ticks@1"), "ticks-1");
        host.Schedules.Calls.Clear();

        var ticks = await host.DeactivateAsync(Ticks("ticks@1"));
        var orders = await host.DeactivateAsync(Orders("orders@1"));

        Assert.Equal(WorkflowActivationOutcome.Deactivated, ticks.Outcome);
        Assert.Equal([$"stop schedule:ticks:tick {ticks.Slot.Revision}", "unregister schedule-start:ticks:tick"], host.Schedules.Calls);
        Assert.Equal(WorkflowActivationOutcome.Deactivated, orders.Outcome);
        Assert.False(host.Serving.Routes.TryStart("POST", "orders", out _));
        Assert.Empty(host.Serving.Slots);
        Assert.Null(host.Serving.ServedWorkflow("orders").Workflow);
        Assert.True(host.Serving.Registry.Contains("ticks@1"));
    }

    [Fact]
    public async Task A_serving_failure_reverts_the_switch()
    {
        var host = new Host();
        var first = await host.ActivateAsync(Ticks("ticks@1"), "ticks-1");
        host.Schedules.FailSet = "schedule:ticks:tick";
        host.Schedules.Calls.Clear();

        var failed = await host.ActivateAsync(Ticks("ticks@2", "PT5M"), "ticks-2");

        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.ProjectionActivation), (failed.Outcome, failed.FailedStep));
        Assert.Contains("unavailable", failed.Diagnostic, StringComparison.Ordinal);
        // The slot names the activation it served before, and so does this host.
        Assert.Equal("ticks-1", failed.Slot.ActiveActivationId);
        Assert.Equal("ticks@1", host.Serving.ServedWorkflow("ticks").Workflow);
        Assert.Equal("ticks@1", host.Schedules.Runs["schedule-start:ticks:tick"]!.Workflow);
        Assert.Equal(WorkflowActivationProjectionState.Active, await host.RecurringSchedules.FindActivationStateAsync("ticks-1"));
        Assert.NotEqual(WorkflowActivationProjectionState.Active, await host.RecurringSchedules.FindActivationStateAsync("ticks-2"));
        Assert.True(failed.Slot.Revision > first.Slot.Revision);
        // The compensation pointed the schedule back at the version the slot serves, at the reverted slot's revision.
        Assert.Null(failed.CompensationDiagnostic);
        Assert.Equal($"set schedule:ticks:tick {failed.Slot.Revision} interval:PT1M", host.Schedules.Calls[^1]);
    }

    [Fact]
    public async Task Ensure_serving_restores_a_missing_artifact_after_a_restart()
    {
        var host = new Host();
        await host.ActivateAsync(Orders("orders@1"), "orders-1");
        var ticks = await host.ActivateAsync(Ticks("ticks@1"), "ticks-1");

        host.Restart();
        Assert.False(host.Serving.Registry.Contains("orders@1"));
        var orders = await host.Coordinator.EnsureServingAsync("orders", KernelServing.DefaultSlotName);
        var all = await host.Coordinator.ServeActiveSlotsAsync();

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, orders.Outcome);
        Assert.All(all, result => Assert.Equal(WorkflowActivationOutcome.AlreadyActive, result.Outcome));
        Assert.Equal(2, all.Count);
        Assert.True(host.Serving.Registry.Contains("orders@1"));
        Assert.True(host.Serving.Routes.TryStart("POST", "orders", out var route) && route == ("orders@1", "in"));
        Assert.Equal(
            ["register schedule-start:ticks:tick ticks@1", $"set schedule:ticks:tick {ticks.Slot.Revision} interval:PT1M"],
            host.Schedules.Calls);
        // Serving again writes nothing more.
        host.Schedules.Calls.Clear();
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, (await host.Coordinator.EnsureServingAsync("ticks", KernelServing.DefaultSlotName)).Outcome);
        Assert.Empty(host.Schedules.Calls);
    }

    [Fact]
    public async Task An_activation_after_a_restart_serves_the_slot_first()
    {
        var host = new Host();
        await host.ActivateAsync(Orders("orders@1"), "orders-1");
        host.Restart();

        var again = await host.ActivateAsync(Orders("orders@1"), "orders-1b");

        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, again.Outcome);
        Assert.True(host.Serving.Routes.TryStart("POST", "orders", out _));
    }

    [Fact]
    public async Task A_dispatch_reaches_the_version_the_definition_serves()
    {
        var host = new Host();
        await host.ActivateAsync(Orders("orders@1"), "orders-1");
        await host.ActivateAsync(Orders("orders@2"), "orders-2");
        await host.ActivateAsync(ExecutableOf("orders-preview@1", "orders", HttpWorkflow("orders-preview", "preview")), "orders-preview", slot: "preview");

        Assert.Equal("orders@2", host.Serving.ServedWorkflow("orders").Workflow);
        Assert.Null(host.Serving.ServedWorkflow("unknown").Workflow);
        var options = new Native.NativeExecutionOptions { ServedWorkflow = host.Serving.ServedWorkflow };
        Assert.Equal("orders@2", options.ServedWorkflow!("orders").Workflow);
    }

    [Fact]
    public async Task A_definition_served_in_several_channels_without_a_default_serves_several()
    {
        var host = new Host();
        await host.ActivateAsync(ExecutableOf("reports-a@1", "reports", HttpWorkflow("reports-a", "a")), "reports-a", slot: "a");
        Assert.Equal("reports-a@1", host.Serving.ServedWorkflow("reports").Workflow);

        await host.ActivateAsync(ExecutableOf("reports-b@1", "reports", HttpWorkflow("reports-b", "b")), "reports-b", slot: "b");

        Assert.Equal(Native.ServedDefinition.SeveralServe, host.Serving.ServedWorkflow("reports"));
        Assert.Equal(Native.ServedDefinition.None, host.Serving.ServedWorkflow("unknown"));
    }

    [Fact]
    public async Task Two_coordinators_over_one_serving_state_each_keep_the_others_slot()
    {
        var host = new Host();
        var gated = new GatedSwitch(host.Switch);
        var first = host.NewCoordinator(gated);

        var ticks = host.ActivateAsync(Ticks("ticks@1"), "ticks-1", coordinator: first);
        await gated.Entered.Task;
        var orders = host.ActivateAsync(Orders("orders@1"), "orders-1");
        await Task.Delay(50);
        // The second waits for the first, which planned its change against the serving set it changes.
        Assert.False(orders.IsCompleted);
        gated.Release.SetResult();

        Assert.Equal(WorkflowActivationOutcome.Activated, (await ticks).Outcome);
        Assert.Equal(WorkflowActivationOutcome.Activated, (await orders).Outcome);
        Assert.Equal(["orders", "ticks"], host.Serving.Slots.Select(slot => slot.DefinitionId).Order());
        Assert.True(host.Serving.Routes.TryStart("POST", "orders", out _));
        Assert.Single(host.Serving.Schedules.All);
    }

    [Fact]
    public async Task A_deactivation_after_a_restart_stops_the_schedule_the_slot_serves()
    {
        var host = new Host();
        await host.ActivateAsync(Ticks("ticks@1"), "ticks-1");
        host.Restart();

        var result = await host.DeactivateAsync(Ticks("ticks@1"));

        Assert.Equal(WorkflowActivationOutcome.Deactivated, result.Outcome);
        Assert.Equal([$"stop schedule:ticks:tick {result.Slot.Revision}", "unregister schedule-start:ticks:tick"], host.Schedules.Calls);
    }

    [Fact]
    public async Task A_deactivation_whose_answer_is_lost_is_finished_as_one_that_committed()
    {
        var host = new Host();
        await host.ActivateAsync(Orders("orders@1"), "orders-1");
        await host.ActivateAsync(Ticks("ticks@1"), "ticks-1");
        host.Schedules.Calls.Clear();
        host.Observer.Refreshes.Clear();
        host.Coordinator = host.NewCoordinator(new LosesDeactivationAnswer(host.Switch));

        var ticks = await host.DeactivateAsync(Ticks("ticks@1"));
        var orders = await host.DeactivateAsync(Orders("orders@1"));

        Assert.Equal(WorkflowActivationOutcome.Deactivated, ticks.Outcome);
        Assert.Null(ticks.Slot.ActiveActivationId);
        Assert.Equal([$"stop schedule:ticks:tick {ticks.Slot.Revision}", "unregister schedule-start:ticks:tick"], host.Schedules.Calls);
        Assert.Equal(WorkflowActivationOutcome.Deactivated, orders.Outcome);
        Assert.False(host.Serving.Routes.TryStart("POST", "orders", out _));
        Assert.Empty(host.Serving.Slots);
        Assert.Equal(["ticks ticks@1 True 0", "orders orders@1 True 0"], host.Observer.Refreshes);
    }

    [Fact]
    public async Task Ensure_serving_an_empty_slot_drops_what_this_host_still_serves_for_it()
    {
        var host = new Host();
        var ticks = await host.ActivateAsync(Ticks("ticks@1"), "ticks-1");
        host.Schedules.Calls.Clear();
        // The slot is emptied past this host, as a deactivation whose finish was lost leaves it.
        var emptied = await host.Switch.TryDeactivateAsync(new("ticks", KernelServing.DefaultSlotName, WorkflowActivationSource.Publishing, ticks.Slot.Revision,
            host.Clock.GetUtcNow()), []);
        Assert.True(emptied.Succeeded);

        var result = await host.Coordinator.EnsureServingAsync("ticks", KernelServing.DefaultSlotName);

        Assert.Equal(WorkflowActivationOutcome.AlreadyInactive, result.Outcome);
        Assert.Empty(host.Serving.Slots);
        Assert.Equal([$"stop schedule:ticks:tick {emptied.Slot.Revision}", "unregister schedule-start:ticks:tick"], host.Schedules.Calls);
    }

    [Fact]
    public async Task The_trigger_observers_refresh_after_each_activation_and_deactivation()
    {
        var host = new Host();

        await host.ActivateAsync(Orders("orders@1"), "orders-1");
        await host.ActivateAsync(Orders("orders@2"), "orders-2");
        await host.DeactivateAsync(Orders("orders@2"));

        Assert.Equal(["orders orders@1 True 0", "orders orders@2 True 0", "orders orders@2 True 0"], host.Observer.Refreshes);
    }

    [Fact]
    public async Task An_observer_failure_after_an_activation_reverts_it()
    {
        var host = new Host();
        await host.ActivateAsync(Orders("orders@1"), "orders-1");
        host.Observer.Fail = true;

        var failed = await host.ActivateAsync(Orders("orders@2"), "orders-2");

        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.TriggerObserverNotification), (failed.Outcome, failed.FailedStep));
        Assert.Equal("orders-1", failed.Slot.ActiveActivationId);
        Assert.True(host.Serving.Routes.TryStart("POST", "orders", out var route) && route == ("orders@1", "in"));
        Assert.Equal("orders@1", host.Serving.ServedWorkflow("orders").Workflow);
    }

    [Fact]
    public async Task An_observer_failure_after_a_deactivation_switches_it_back_on()
    {
        var host = new Host();
        await host.ActivateAsync(Ticks("ticks@1"), "ticks-1");
        host.Observer.Fail = true;

        var failed = await host.DeactivateAsync(Ticks("ticks@1"));

        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.TriggerObserverNotification), (failed.Outcome, failed.FailedStep));
        Assert.Equal("ticks-1", failed.Slot.ActiveActivationId);
        Assert.Equal("ticks@1", host.Serving.ServedWorkflow("ticks").Workflow);
        Assert.Equal("ticks@1", host.Schedules.Runs["schedule-start:ticks:tick"]!.Workflow);
    }

    [Fact]
    public async Task A_definition_serves_one_scheduled_channel()
    {
        var host = new Host();
        await host.ActivateAsync(Ticks("ticks@1"), "ticks-1");

        var second = await host.ActivateAsync(Ticks("ticks@2", "PT5M"), "ticks-preview", slot: "preview");

        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.ServingCheck), (second.Outcome, second.FailedStep));
        Assert.Contains("Definition 'ticks'", second.Diagnostic, StringComparison.Ordinal);
        Assert.Contains("publish the scheduled definition to one channel", second.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_without_schedule_actors_refuses_a_timer_where_it_maps_it()
    {
        var host = new Host(capabilities: new(Schedules: false));
        host.Coordinator = host.NewCoordinator(host.Switch, new UnavailableScheduleActors(KernelHostCapabilities.SchedulesUnavailable));

        var timer = await host.ActivateAsync(Ticks("ticks@1"), "ticks-1");
        var http = await host.ActivateAsync(Orders("orders@1"), "orders-1");

        // Refused before anything is minted: no reference, and the slot never moved.
        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.Mapping), (timer.Outcome, timer.FailedStep));
        Assert.Equal($"{NativeMappingRefusedException.ActivityUnsupported}: Node 'tick': {KernelHostCapabilities.SchedulesUnavailable}", timer.Diagnostic);
        Assert.Null(timer.Slot.ActiveActivationId);
        Assert.Null(await host.ReferenceAsync("ticks-1"));
        Assert.Null(host.Serving.ServedWorkflow("ticks").Workflow);
        Assert.Equal(WorkflowActivationOutcome.Activated, http.Outcome);
        // Unpublishing what serves stops no schedule and unregisters no run, which the stand-in takes without failing.
        Assert.True((await host.DeactivateAsync(Orders("orders@1"))).Succeeded);
    }

    [Fact]
    public async Task The_stand_in_takes_a_stop_and_an_unregister_and_refuses_a_set()
    {
        var schedules = new UnavailableScheduleActors("no schedules here");

        await schedules.StopAsync("schedule:ticks:tick", 3);
        await schedules.RegisterRunAsync("schedule-start:ticks:tick", null);

        Assert.Equal("no schedules here", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            schedules.SetAsync("schedule:ticks:tick", 3, new RecurringSchedule("interval", "PT1M"), "schedule-start:ticks:tick").AsTask())).Message);
        Assert.Equal("no schedules here", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            schedules.RegisterRunAsync("schedule-start:ticks:tick", Native.NativeExecutionModel.Start("ticks@1")).AsTask())).Message);
    }

    private static WorkflowExecutable Waits(string artifact, string text) => ExecutableOf(artifact, "waits", Sequence("root",
        Node("delay", Types.Delay, [Literal("Duration", "00:00:05", "TimeSpan")]),
        SetOutput("done", "Result", Literal("value", text, "String"))));

    /// <summary>The Native model of a host over its serving state, loading what its registry lacks from the host's executable store.</summary>
    private static Native.NativeExecutionModel ModelOf(Host host) => new(host.Serving.Registry, new Native.NativeExecutionOptions
    {
        ResolveWorkflow = (workflow, token) => NativeExecutableMapper.MapStoredAsync(host.Executables, workflow, token),
    });

    [Fact]
    public async Task Instances_in_flight_continue_after_a_restart_on_the_served_version_and_on_an_older_one()
    {
        var host = new Host();
        await host.ActivateAsync(Waits("waits@1", "first"), "waits-1");
        var older = await NativeTurn.StartAsync(ModelOf(host), host.Serving.Registry.Get("waits@1"));
        await host.ActivateAsync(Waits("waits@2", "second"), "waits-2");
        var served = await NativeTurn.StartAsync(ModelOf(host), host.Serving.Registry.Get("waits@2"));
        Assert.Equal((Contract.ActorStatus.Running, Contract.ActorStatus.Running), (older.Status, served.Status));

        host.Restart();
        await host.Coordinator.ServeActiveSlotsAsync();
        Assert.True(host.Serving.Registry.Contains("waits@2"));
        Assert.False(host.Serving.Registry.Contains("waits@1"));

        // Each delay's timer comes due on the restarted host: each instance runs on to its end, on the version it started on.
        var model = ModelOf(host);
        var olderNext = await older.ResumeAsync(model);
        var servedNext = await served.ResumeAsync(model);

        Assert.Equal((Contract.ActorStatus.Finished, Contract.ActorStatus.Finished), (olderNext.Status, servedNext.Status));
        Assert.Equal("first", olderNext.Output?["Result"]?.GetValue<string>());
        Assert.Equal("second", servedNext.Output?["Result"]?.GetValue<string>());
        // The older version joined the registry, and serves nothing: its definition's route and Dispatch reach the served one.
        Assert.True(host.Serving.Registry.Contains("waits@1"));
        Assert.Equal("waits@2", host.Serving.ServedWorkflow("waits").Workflow);
    }

    [Fact]
    public async Task A_turn_of_a_workflow_the_host_has_no_artifact_for_faults_as_before()
    {
        var host = new Host();
        await host.ActivateAsync(Waits("waits@1", "first"), "waits-1");
        var turn = await NativeTurn.StartAsync(ModelOf(host), host.Serving.Registry.Get("waits@1"));

        // Another host, whose store never held the artifact.
        var other = new Host();
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => turn.ResumeAsync(ModelOf(other)));

        Assert.Equal("No workflow 'waits@1'.", missing.Message);
    }

    [Fact]
    public void The_kernel_trigger_extractor_reports_no_triggers()
    {
        IWorkflowTriggerBindingExtractor extractor = new KernelTriggerBindingExtractor();
        var executable = Orders("orders@1");

        Assert.Empty(extractor.Extract(executable));
        Assert.Empty(extractor.Evaluate(executable).NodeOutcomes);
    }

    private static WorkflowExecutable Pauses(string artifact) => ExecutableOf(artifact, "pauses", Sequence("root",
        Node("pause", Types.Delay, [Literal("Duration", "00:00:05", "TimeSpan")], displayName: "Wait a moment"),
        WriteLine("after", Literal("text", "after"))));

    private static WorkflowExecutable Dispatches(string artifact, bool wait) => ExecutableOf(artifact, "dispatches", Sequence("root",
        Node("call", Types.Dispatch, [Literal("WorkflowDefinitionId", "invoicing", "String"), Literal("WaitForCompletion", wait, "Boolean")])));

    [Fact]
    public async Task A_host_without_timers_refuses_a_delay_where_it_maps_it()
    {
        var host = new Host(capabilities: new(Timers: false));

        var refused = await host.ActivateAsync(Pauses("pauses@1"), "pauses-1");

        // The refusal names the node by its display name, as every mapping refusal does.
        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.Mapping), (refused.Outcome, refused.FailedStep));
        Assert.Equal($"{NativeMappingRefusedException.ActivityUnsupported}: Node 'Wait a moment' (pause): {KernelHostCapabilities.DelayUnavailable}",
            refused.Diagnostic);
        Assert.Null(refused.Slot.ActiveActivationId);
        Assert.Null(await host.ReferenceAsync("pauses-1"));
        Assert.False(host.Serving.Registry.Contains("pauses@1"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_host_without_child_workflows_refuses_a_dispatch_waited_or_not(bool wait)
    {
        var host = new Host(capabilities: new(Children: false));

        var refused = await host.ActivateAsync(Dispatches("dispatches@1", wait), "dispatches-1");

        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.Mapping), (refused.Outcome, refused.FailedStep));
        Assert.Equal($"{NativeMappingRefusedException.ActivityUnsupported}: Node 'call': {KernelHostCapabilities.DispatchUnavailable}", refused.Diagnostic);
        Assert.Null(refused.Slot.ActiveActivationId);
    }

    [Fact]
    public async Task A_host_with_timers_and_child_workflows_serves_both()
    {
        var host = new Host();

        Assert.Equal(WorkflowActivationOutcome.Activated, (await host.ActivateAsync(Pauses("pauses@1"), "pauses-1")).Outcome);
        Assert.Equal(WorkflowActivationOutcome.Activated, (await host.ActivateAsync(Dispatches("dispatches@1", true), "dispatches-1")).Outcome);
    }

    [Fact]
    public async Task A_restart_on_a_host_without_timers_does_not_serve_a_delay_published_before()
    {
        var host = new Host();
        await host.ActivateAsync(Pauses("pauses@1"), "pauses-1");

        host.Restart(new KernelHostCapabilities(Timers: false));
        var served = Assert.Single(await host.Coordinator.ServeActiveSlotsAsync());

        Assert.False(served.Succeeded);
        Assert.Equal(WorkflowActivationStep.Mapping, served.FailedStep);
        Assert.Contains(KernelHostCapabilities.DelayUnavailable, served.Diagnostic, StringComparison.Ordinal);
        Assert.False(host.Serving.Registry.Contains("pauses@1"));
    }

    // A start route only, as an edge that serves no wait routes takes it.
    private static WorkflowExecutable Approvals(string artifact) => ExecutableOf(artifact, "approvals", Sequence("root",
        Node("in", Types.HttpEndpoint, [Literal("Path", "approvals/{id}", "String"), Literal("SupportedMethods", new[] { "POST" }), Literal("CanStartWorkflow", true, "Boolean")]),
        Node("out", Types.WriteHttpResponse, [Literal("Body", "approved", "String")])));

    // A start route and a wait route after it.
    private static WorkflowExecutable Decisions(string artifact) => ExecutableOf(artifact, "decisions", Sequence("root",
        Node("in", Types.HttpEndpoint, [Literal("Path", "approvals/{id}", "String"), Literal("SupportedMethods", new[] { "POST" }), Literal("CanStartWorkflow", true, "Boolean")]),
        Node("wait", Types.HttpEndpoint, [Literal("Path", "decisions", "String"), Literal("SupportedMethods", new[] { "POST" }), Literal("CanStartWorkflow", false, "Boolean")],
            displayName: "Wait for the decision"),
        Node("out", Types.WriteHttpResponse, [Literal("Body", "decided", "String")])));

    private static readonly KernelHostCapabilities StartRoutesOnly = new(HttpWaits: false);

    [Fact]
    public async Task A_host_whose_edge_serves_no_wait_routes_refuses_a_waiting_http_endpoint_where_it_maps_it()
    {
        var host = new Host(capabilities: StartRoutesOnly);

        var refused = await host.ActivateAsync(Decisions("decisions@1"), "decisions-1");

        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.Mapping), (refused.Outcome, refused.FailedStep));
        Assert.Equal($"{NativeMappingRefusedException.ActivityUnsupported}: Node 'Wait for the decision' (wait): {KernelHostCapabilities.HttpWaitUnavailable}",
            refused.Diagnostic);
        Assert.Null(refused.Slot.ActiveActivationId);
        Assert.True((await host.ActivateAsync(Approvals("approvals@1"), "approvals-1")).Succeeded);
    }

    [Fact]
    public async Task A_host_whose_edge_serves_no_wait_routes_refuses_an_http_endpoint_at_the_start_that_may_not_start_a_run()
    {
        var host = new Host(capabilities: StartRoutesOnly);
        var waitsFirst = ExecutableOf("waits-first@1", "waits-first", Sequence("root",
            Node("in", Types.HttpEndpoint, [Literal("Path", "waits-first", "String"), Literal("SupportedMethods", new[] { "POST" }), Literal("CanStartWorkflow", false, "Boolean")]),
            Node("out", Types.WriteHttpResponse, [Literal("Body", "no", "String")])));

        var refused = await host.ActivateAsync(waitsFirst, "waits-first-1");

        Assert.Equal((WorkflowActivationOutcome.Failed, WorkflowActivationStep.Mapping), (refused.Outcome, refused.FailedStep));
        Assert.Equal($"{NativeMappingRefusedException.ActivityUnsupported}: Node 'in': {KernelHostCapabilities.HttpWaitUnavailable}", refused.Diagnostic);
    }

    [Fact]
    public async Task The_edge_starts_the_workflow_a_start_route_serves_with_the_request_as_its_trigger()
    {
        var host = new Host(capabilities: StartRoutesOnly);
        var activated = await host.ActivateAsync(Approvals("approvals@1"), "approvals-1");
        Assert.True(activated.Succeeded, activated.Diagnostic);
        var request = new Native.HttpRequest("POST", "/workflows/http/approvals/A-7", new System.Text.Json.Nodes.JsonObject { ["by"] = "Ada" });

        var start = KernelHttpEdge.Match(host.Serving, request, "/approvals/A-7");

        Assert.NotNull(start);
        Assert.Equal(("approvals@1", false), (start.Workflow, start.RespondBeforeCommit));
        Assert.Equal(Kernel.WorkflowActor.WorkflowActor.ActorType, start.Spec.Type);
        var native = System.Text.Json.Nodes.JsonNode.Parse(start.Spec.Input!)!;
        Assert.Equal("in", native["TriggerNode"]?.GetValue<string>());
        Assert.Equal("A-7", native["Trigger"]?["RouteValues"]?["id"]?.GetValue<string>());
        Assert.Equal("Ada", native["Trigger"]?["Body"]?["by"]?.GetValue<string>());
    }

    [Fact]
    public async Task The_edge_serves_no_wait_route_no_other_method_and_no_unknown_path()
    {
        // A host whose edge would serve waits still maps them; this edge matches start routes only.
        var host = new Host();
        await host.ActivateAsync(Decisions("decisions@1"), "decisions-1");

        Assert.Null(KernelHttpEdge.Route(host.Serving, "POST", "/decisions"));
        Assert.NotNull(KernelHttpEdge.Route(host.Serving, "POST", "/approvals/A-7"));
        Assert.Null(KernelHttpEdge.Match(host.Serving, new Native.HttpRequest("POST", "/x"), "/decisions"));
        Assert.Null(KernelHttpEdge.Match(host.Serving, new Native.HttpRequest("GET", "/x"), "/approvals/A-7"));
        Assert.Null(KernelHttpEdge.Match(host.Serving, new Native.HttpRequest("POST", "/x"), "/unknown"));
        Assert.Equal(404, KernelHttpEdge.NotFound.StatusCode);
    }

    [Fact]
    public async Task A_matched_route_whose_workflow_stops_serving_before_the_start_starts_nothing()
    {
        var host = new Host(capabilities: StartRoutesOnly);
        await host.ActivateAsync(Approvals("approvals@1"), "approvals-1");
        var route = KernelHttpEdge.Route(host.Serving, "POST", "/approvals/A-7");
        Assert.NotNull(route);

        Assert.True((await host.DeactivateAsync(Approvals("approvals@1"))).Succeeded);

        Assert.Null(KernelHttpEdge.Start(host.Serving, route, new Native.HttpRequest("POST", "/workflows/http/approvals/A-7")));
    }

    [Fact]
    public async Task A_host_workflow_that_waits_at_an_http_endpoint_is_refused_on_an_edge_without_wait_routes()
    {
        var host = new Host(capabilities: StartRoutesOnly);
        var waits = Native.Wf.Workflow("waits", Native.Wf.Sequence("main", Native.Wf.HttpEndpoint("start", "waits"),
            Native.Wf.HttpEndpoint("decision", "waits/decision"), Native.Wf.Respond("out", "ok")));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => host.Serving.ServeHostWorkflowAsync(waits).AsTask());

        Assert.Contains(KernelHostCapabilities.HttpWaitUnavailable, refused.Message, StringComparison.Ordinal);
        Assert.Contains("node 'decision'", refused.Message, StringComparison.Ordinal);
        Assert.False(host.Serving.Routes.TryStart("POST", "waits", out _));
    }

    [Fact]
    public void The_edge_answers_each_reply_as_the_test_edges_do()
    {
        var designed = Native.Values.From(new Native.HttpResponse(201, "text/plain", System.Text.Json.Nodes.JsonValue.Create("made"))
            { Headers = new Dictionary<string, string> { ["x-order"] = "7" } });
        var json = Native.Values.From(new Native.HttpResponse(200, "application/json", new System.Text.Json.Nodes.JsonObject { ["ok"] = true }));

        var made = KernelHttpEdge.Answer(new("response", designed));
        Assert.Equal((201, "made", "7"), (made.StatusCode, made.Body, made.Headers["x-order"]));
        Assert.Equal((200, "{\"ok\":true}"), (KernelHttpEdge.Answer(new("response", json)).StatusCode, KernelHttpEdge.Answer(new("response", json)).Body));
        Assert.Equal(204, KernelHttpEdge.Answer(new(null, null, Kernel.Foundation.Contracts.ActorStatus.Finished)).StatusCode);
        Assert.Equal(504, KernelHttpEdge.Answer(new(null, null, Kernel.Foundation.Contracts.ActorStatus.Running, "timeout")).StatusCode);
        Assert.Equal(503, KernelHttpEdge.Answer(new(null, null, null, "lost")).StatusCode);
        Assert.Equal(404, KernelHttpEdge.Answer(new(null, null, null, "missing")).StatusCode);
        Assert.Equal(500, KernelHttpEdge.Answer(new(null, null, Kernel.Foundation.Contracts.ActorStatus.Faulted)).StatusCode);
        // Terminated before it answered is no success.
        var terminated = KernelHttpEdge.Answer(new(null, null, Kernel.Foundation.Contracts.ActorStatus.Cancelled));
        Assert.Equal((503, "The workflow was terminated before it answered."), (terminated.StatusCode, terminated.Body));
    }

    [Fact]
    public async Task A_host_workflow_serves_beside_the_publications_and_its_route_conflicts_are_refused()
    {
        var host = new Host();
        await host.ActivateAsync(Orders("orders@1"), "orders-1");
        var probe = Native.Wf.Workflow("probe", Native.Wf.Sequence("main", Native.Wf.HttpEndpoint("start", "probe"), Native.Wf.Respond("out", "ok")));
        var clash = Native.Wf.Workflow("clash", Native.Wf.Sequence("main", Native.Wf.HttpEndpoint("start", "orders"), Native.Wf.Respond("out", "no")));

        await host.Serving.ServeHostWorkflowAsync(probe);

        Assert.True(host.Serving.Routes.TryStart("POST", "probe", out var route) && route == ("probe", "start"));
        Assert.True(host.Serving.Routes.TryStart("POST", "orders", out var orders) && orders == ("orders@1", "in"));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => host.Serving.ServeHostWorkflowAsync(clash).AsTask());
    }
}

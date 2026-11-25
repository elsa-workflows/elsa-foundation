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
using Models = Kernel.Model.Contracts;
using Native = Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

/// <summary>
/// The kernel coordinator on the old runtime's in-memory slot authority, switch, projection stores and executable store, with
/// a recording schedule host in place of the schedule actors (the Sim's schedule checks run the actor itself).
/// </summary>
public sealed class KernelCoordinatorTests
{
    private sealed class RecordingSchedules : IKernelScheduleHost
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
        private readonly ActivationChannelOptions? _channels;

        public Host(ActivationChannelOptions? channels = null)
        {
            _channels = channels;
            References = new(Executables, Clock);
            Switch = new(Authority, References, Clock, Bindings, RecurringSchedules);
            Restart();
        }

        public void Restart()
        {
            Serving = new KernelServing(expression => expression == "0 * * * * *" ? null : "not a cron expression this host reads");
            Schedules = new RecordingSchedules();
            Coordinator = new KernelActivationCoordinator(Authority, Switch, References,
                new WorkflowExecutableRootWriteLeaseManager(Executables, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), Clock),
                Executables, Bindings, Serving, Schedules, Clock, RecurringSchedules, _channels);
        }

        public async Task<WorkflowActivationResult> ActivateAsync(WorkflowExecutable executable, string activationId, string slot = "default")
        {
            await Executables.SaveAsync(executable);
            var current = await Authority.FindAsync(executable.Identity.DefinitionId, slot);
            var identity = executable.Identity;
            var reference = new WorkflowExecutableSourceReference("candidate", identity.ArtifactId, "definition", identity.DefinitionId, "1",
                identity.DefinitionId, identity.DefinitionVersionId, identity.ArtifactVersion, Clock.GetUtcNow(), Clock.GetUtcNow(), WorkflowExecutableReferenceScope.Published);
            return await Coordinator.ActivateAsync(new WorkflowActivationCommand(executable, reference, slot, activationId, WorkflowActivationSource.Publishing,
                current?.Revision ?? 0));
        }

        public async Task<WorkflowActivationResult> DeactivateAsync(WorkflowExecutable executable, string slot = "default")
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
        Assert.Equal("orders@1", host.Serving.ServedWorkflow("orders"));
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
        Assert.Contains("POST /orders", clash.Diagnostic, StringComparison.Ordinal);
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
        var host = new Host(new ActivationChannelOptions { AllowAdHocChannels = false, Channels = [new() { Name = "default" }] });

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
        Assert.Null(host.Serving.ServedWorkflow("orders"));
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
        Assert.Equal("ticks@1", host.Serving.ServedWorkflow("ticks"));
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
        var orders = await host.Coordinator.EnsureServingAsync("orders", "default");
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
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, (await host.Coordinator.EnsureServingAsync("ticks", "default")).Outcome);
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

        Assert.Equal("orders@2", host.Serving.ServedWorkflow("orders"));
        Assert.Null(host.Serving.ServedWorkflow("unknown"));
        var options = new Native.NativeExecutionOptions { ServedWorkflow = host.Serving.ServedWorkflow };
        Assert.Equal("orders@2", options.ServedWorkflow!("orders"));
    }

    [Fact]
    public void The_kernel_trigger_extractor_reports_no_triggers()
    {
        IWorkflowTriggerBindingExtractor extractor = new KernelTriggerBindingExtractor();
        var executable = Orders("orders@1");

        Assert.Empty(extractor.Extract(executable));
        Assert.Empty(extractor.Evaluate(executable).NodeOutcomes);
    }
}

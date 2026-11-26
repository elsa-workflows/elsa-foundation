using System.Text.Json;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>Adapter interpretation controls with constructed validated callback data; T045 owns actual transport/recovery proof.</summary>
public sealed class BpmnCalledControlAdapterTests
{
    [Theory]
    [InlineData("ORDER", "Faulted", true)]
    [InlineData("ORDER", "Cancelled", true)]
    [InlineData("OTHER", "Faulted", false)]
    [InlineData("OTHER", "Cancelled", false)]
    public async Task Authored_terminal_code_routes_by_exact_code_even_when_cancellation_wins_actual_status(
        string reportedCode, string localOutcome, bool caught)
    {
        await using var host = await F.CreateAsync(["control-parent", "control-call", "control-extra"]);
        var executable = Executable(host, includeEscalation: false);
        var run = await host.RunAsync(executable);
        var parent = run.State(F.ProcessNodeId);
        var child = run.State("node-call");
        var runtime = host.Provider.GetRequiredService<IBpmnProcessRuntime>();
        var context = Context(runtime, executable, parent, child);
        var control = Transport(context, child, 0, true, [], reportedCode);
        var result = await runtime.ControlAsync(context, new RuntimeChildControlContext(context, child.InvocationId,
            child.Execution.ExecutableNodeId, child.IterationId, child.Provenance.Metadata, [control], [localOutcome]));
        Assert.Equal(!caught, (result.Kind == RuntimeStructuralContinuationKind.Fault));
        Assert.Equal(caught ? new[] { "node-error" } : Array.Empty<string>(), context.GetChildActivityScheduleRequests().Select(request => request.ExecutableNodeId));
        Assert.Empty(context.GetChildFaultAbsorptionRequests());
        Assert.NotNull(result.StateUpdate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Earlier_signal_cannot_start_work_before_later_unhandled_authored_failure_is_folded(bool indivisible)
    {
        await using var host = await F.CreateAsync(["fold-parent", "fold-call", "fold-extra"]);
        var executable = Executable(host, includeEscalation: true);
        var run = await host.RunAsync(executable);
        var parent = run.State(F.ProcessNodeId);
        var child = run.State("node-call");
        var runtime = host.Provider.GetRequiredService<IBpmnProcessRuntime>();
        var context = Context(runtime, executable, parent, child);
        var signal = new RuntimeChildControlNotification(global::Bpmn.Semantics.BpmnInterpreter.EscalationSignalCode,
            JsonSerializer.SerializeToElement(new { code = "WARN" }));
        var before = Transport(context, child, 0, false, [signal]);
        var terminal = Transport(context, child, indivisible ? 0 : 1, true, indivisible ? [signal] : [], "UNMATCHED");
        var result = await runtime.ControlAsync(context, new RuntimeChildControlContext(context, child.InvocationId,
            child.Execution.ExecutableNodeId, child.IterationId, child.Provenance.Metadata, indivisible ? [terminal] : [before, terminal], ["Faulted"]));
        Assert.True((result.Kind == RuntimeStructuralContinuationKind.Fault));
        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.Empty(context.GetChildFaultAbsorptionRequests());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Running_call_escalation_uses_its_exact_handle_and_cancels_only_when_interrupting(bool interrupting)
    {
        await using var host = await F.CreateAsync(["notice-parent", "notice-call", "notice-extra"]);
        var executable = Executable(host, includeEscalation: true, interrupting: interrupting);
        var run = await host.RunAsync(executable);
        var parent = run.State(F.ProcessNodeId);
        var child = run.State("node-call");
        var runtime = host.Provider.GetRequiredService<IBpmnProcessRuntime>();
        var context = Context(runtime, executable, parent, child);
        var signal = new RuntimeChildControlNotification(global::Bpmn.Semantics.BpmnInterpreter.EscalationSignalCode,
            JsonSerializer.SerializeToElement(new { code = "WARN" }));
        var incoming = Transport(context, child, 0, false, [signal]);
        var result = await runtime.ControlAsync(context, new RuntimeChildControlContext(context, child.InvocationId,
            child.Execution.ExecutableNodeId, child.IterationId, child.Provenance.Metadata, [incoming]));
        Assert.True(result.IsDeferred);
        Assert.Equal("node-warning", Assert.Single(context.GetChildActivityScheduleRequests()).ExecutableNodeId);
        Assert.Equal(interrupting ? 1 : 0, context.GetChildSubtreeCancellationRequests().Count);
        if (interrupting) Assert.Equal(child.InvocationId, Assert.Single(context.GetChildSubtreeCancellationRequests()).ChildActivityExecutionId);
        Assert.Empty(context.GetChildFaultAbsorptionRequests());
    }

    [Fact]
    public async Task Retirement_from_an_earlier_callback_suppresses_later_notice_but_still_settles_terminal()
    {
        await using var host = await F.CreateAsync(["retirement-parent", "retirement-call", "retirement-extra"]);
        var executable = Executable(host, includeEscalation: true, interrupting: true);
        var run = await host.RunAsync(executable);
        var parent = run.State(F.ProcessNodeId);
        var child = run.State("node-call");
        var runtime = host.Provider.GetRequiredService<IBpmnProcessRuntime>();
        var signal = new RuntimeChildControlNotification(global::Bpmn.Semantics.BpmnInterpreter.EscalationSignalCode,
            JsonSerializer.SerializeToElement(new { code = "WARN" }));
        var firstContext = Context(runtime, executable, parent, child);
        var first = await runtime.ControlAsync(firstContext, new RuntimeChildControlContext(firstContext, child.InvocationId,
            child.Execution.ExecutableNodeId, child.IterationId, child.Provenance.Metadata,
            [Transport(firstContext, child, 0, false, [signal])]));
        Assert.Single(firstContext.GetChildActivityScheduleRequests());
        Assert.Single(firstContext.GetChildSubtreeCancellationRequests());
        parent = WithState(parent, first);
        var secondContext = Context(runtime, executable, parent, child);
        var second = await runtime.ControlAsync(secondContext, new RuntimeChildControlContext(secondContext, child.InvocationId,
            child.Execution.ExecutableNodeId, child.IterationId, child.Provenance.Metadata,
            [Transport(secondContext, child, 1, false, [signal])]));
        Assert.True(second.IsDeferred);
        Assert.Empty(secondContext.GetChildActivityScheduleRequests());
        Assert.Empty(secondContext.GetChildSubtreeCancellationRequests());
        var terminalContext = Context(runtime, executable, WithState(parent, second), child);
        var terminal = await runtime.ControlAsync(terminalContext, new RuntimeChildControlContext(terminalContext, child.InvocationId,
            child.Execution.ExecutableNodeId, child.IterationId, child.Provenance.Metadata,
            [Transport(terminalContext, child, 2, true, [], "ORDER")], ["Faulted"]));
        Assert.True(terminal.IsDeferred);
        Assert.Empty(terminalContext.GetChildActivityScheduleRequests());
        Assert.Empty(terminalContext.GetChildFaultAbsorptionRequests());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupting_terminal_prefix_suppresses_second_notice_and_settles_stopped_handle(bool fault)
    {
        await using var host = await F.CreateAsync(["terminal-interrupt-parent", "terminal-interrupt-call", "terminal-interrupt-extra"]);
        var executable = Executable(host, includeEscalation: true, interrupting: true);
        var run = await host.RunAsync(executable);
        var child = run.State("node-call");
        var runtime = host.Provider.GetRequiredService<IBpmnProcessRuntime>();
        var context = Context(runtime, executable, run.State(F.ProcessNodeId), child);
        var signal = new RuntimeChildControlNotification(global::Bpmn.Semantics.BpmnInterpreter.EscalationSignalCode,
            JsonSerializer.SerializeToElement(new { code = "WARN" }));
        var result = await runtime.ControlAsync(context, new RuntimeChildControlContext(context, child.InvocationId,
            child.Execution.ExecutableNodeId, child.IterationId, child.Provenance.Metadata,
            [Transport(context, child, 0, true, [signal, signal], fault ? "ORDER" : null)], [fault ? "Faulted" : "Done"]));
        Assert.True(result.IsDeferred);
        Assert.Equal("node-warning", Assert.Single(context.GetChildActivityScheduleRequests()).ExecutableNodeId);
        Assert.Equal(child.InvocationId, Assert.Single(context.GetChildSubtreeCancellationRequests()).ChildActivityExecutionId);
        Assert.Empty(context.GetChildFaultAbsorptionRequests());
    }

    private static ActivityExecutionState WithState(ActivityExecutionState parent, RuntimeStructuralContinuation result) =>
        parent with { PrivateState = new ActivityPrivateState(parent.InvocationId, result.StateUpdate!.StateVersion,
            result.StateUpdate.Value, "control-attempt", DateTimeOffset.UnixEpoch) };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Boundary_read_outage_before_semantic_effects_preserves_actual_host_work_for_retry(bool duringCallback)
    {
        var target = new RuntimeChildControlTarget("outage-caller", "outage-call", "outage-consumer",
            WorkflowExecutionHarness.Identity, "outage-root", "elsa.bpmn", 1);
        var boundary = new AdmittedRoot(target) { UnavailableCount = duringCallback ? 0 : 1 };
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await using var host = await F.CreateAsync(["outage-root", "outage-wait"], services =>
        {
            services.AddScoped<IRuntimeWorkflowControlBoundary>(_ => boundary);
            services.AddSingleton<TimeProvider>(clock);
        }, target.ChildWorkflowExecutionId);
        var executable = host.NewExecutable([F.EventWaitNode("node-wait", "hold")],
            [F.StartEvent(), F.Task("wait", "node-wait"), F.EndEvent()],
            [F.Flow("start-wait", "start", "wait"), F.Flow("wait-end", "wait", "end")],
            [F.NodeResumeTarget("node-wait", Elsa.Activities.Primitives.Activities.Event.ResumeTargetId)]);
        string? before = null;
        if (duringCallback)
        {
            var started = await host.RunAsync(executable);
            before = started.State(F.ProcessNodeId).PrivateState!.Value.InlineValue!.Value.GetRawText();
            boundary.UnavailableCount = 1;
            var bookmark = Assert.Single(await host.BookmarksAsync());
            await host.ResumeAsync(bookmark, JsonSerializer.SerializeToElement(new { }));
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(executable));
        var activities = host.Provider.GetRequiredService<IActivityExecutionStateStore>();
        var root = (await activities.FindAsync(target.ChildWorkflowExecutionId, "outage-root"))!;
        Assert.Equal(ActivityExecutionStatus.Running, root.Status);
        Assert.Null(root.Fault);
        Assert.Equal(0, root.FaultCount);
        Assert.Equal(before, root.PrivateState?.Value.InlineValue?.GetRawText());
        Assert.Empty(await host.Provider.GetRequiredService<IIncidentStateStore>().ListAsync(target.ChildWorkflowExecutionId));
        Assert.Contains(await host.Provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(target.ChildWorkflowExecutionId),
            work => work.CommandKind == (duringCallback ? WorkflowExecutionCommandKind.CompleteActivity : WorkflowExecutionCommandKind.InvokeActivity));
        clock.Advance(TimeSpan.FromSeconds(6));
        var metadata = new Dictionary<string, string>();
        var now = clock.GetUtcNow();
        var actor = await host.Provider.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(
            new WorkflowExecutionActorActivationRequest(target.ChildWorkflowExecutionId, WorkflowExecutionActorActivationReason.ControlPlaneCommand,
                now, "test.boundary-retry", WorkflowExecutionActorCapabilities.None, metadata));
        await actor.EnqueueAsync(new WorkflowExecutionCommandEnvelope("boundary-retry", target.ChildWorkflowExecutionId,
            new WorkflowExecutionCommand("boundary-retry", target.ChildWorkflowExecutionId, WorkflowExecutionCommandKind.RunSchedulerWork, now, null, metadata),
            "boundary-retry", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
        if (!duringCallback)
            await host.ResumeAsync(Assert.Single(await host.BookmarksAsync()), JsonSerializer.SerializeToElement(new { }));
        var workflow = (await host.Provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(target.ChildWorkflowExecutionId))!;
        Assert.Equal(WorkflowExecutionStatus.Completed, workflow.Status);
        Assert.Empty(await host.Provider.GetRequiredService<IIncidentStateStore>().ListAsync(target.ChildWorkflowExecutionId));
        Assert.Empty(await host.Provider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(target.ChildWorkflowExecutionId));
        root = (await activities.FindAsync(target.ChildWorkflowExecutionId, "outage-root"))!;
        Assert.True(root.ProducedControl!.IsTerminal);
        Assert.Equal(0, root.FaultCount);
        Assert.Null(root.ProducedControl.AuthoredFailureCode);
    }

    [Fact]
    public async Task Boundary_read_outage_in_a_parent_notification_keeps_its_structural_source_owed()
    {
        var target = new RuntimeChildControlTarget("notice-caller", "notice-call", "notice-consumer",
            WorkflowExecutionHarness.Identity, "notice-root", "elsa.bpmn", 1);
        var boundary = new AdmittedRoot(target) { FailOnCall = 2 };
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await using var host = await F.CreateAsync(["notice-root", "notice-nested", "notice-wait"], services =>
        {
            services.AddScoped<IRuntimeWorkflowControlBoundary>(_ => boundary);
            services.AddSingleton<TimeProvider>(clock);
        }, target.ChildWorkflowExecutionId);
        var nested = F.NestedProcessNode("node-nested",
            [F.StartEvent("nested-start"), F.EscalationThrow("notice", "WARN"), F.Task("wait", "node-wait"), F.EndEvent("nested-end")],
            [F.Flow("nested-notice", "nested-start", "notice"), F.Flow("notice-wait", "notice", "wait"),
                F.Flow("wait-end", "wait", "nested-end")], [F.EventWaitNode("node-wait", "hold")]);
        var executable = host.NewExecutable([nested], [F.StartEvent(), F.SubProcess("nested", "node-nested"), F.EndEvent()],
            [F.Flow("start-nested", "start", "nested"), F.Flow("nested-end", "nested", "end")],
            [F.NodeResumeTarget("node-wait", Elsa.Activities.Primitives.Activities.Event.ResumeTargetId)]);
        try { await host.RunAsync(executable); }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("Scheduler did not drain to completion", StringComparison.Ordinal)) { }
        var activities = host.Provider.GetRequiredService<IActivityExecutionStateStore>();
        var root = (await activities.FindAsync(target.ChildWorkflowExecutionId, "notice-root"))!;
        Assert.Equal(ActivityExecutionStatus.Running, root.Status);
        Assert.Null(root.Fault);
        Assert.Equal(0, root.FaultCount);
        Assert.Null(root.ProducedControl);
        var pending = await host.Provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(target.ChildWorkflowExecutionId);
        Assert.True(pending.Any(work => work.CommandKind == WorkflowExecutionCommandKind.NotifyParentActivity),
            JsonSerializer.Serialize(new { boundary.Calls, Pending = pending.Select(work => work.CommandKind.ToString()),
                States = (await activities.ListAllAsync(target.ChildWorkflowExecutionId)).Select(state => new
                    { state.InvocationId, Node = state.Execution.ExecutableNodeId, state.Status, Fault = state.Fault?.Message, state.ParentActivityExecutionId }),
                Intents = host.Provider.GetRequiredService<Elsa.Workflows.Runtime.Services.Checkpoints.InMemoryRuntimeCheckpointCommitStore>()
                    .ListCommits().SelectMany(record => record.Commit.PostCommitIntents).Select(intent => intent.Kind) }));
        clock.Advance(TimeSpan.FromSeconds(6));
        var metadata = new Dictionary<string, string>();
        var now = clock.GetUtcNow();
        var actor = await host.Provider.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(
            new WorkflowExecutionActorActivationRequest(target.ChildWorkflowExecutionId, WorkflowExecutionActorActivationReason.ControlPlaneCommand,
                now, "test.notice-retry", WorkflowExecutionActorCapabilities.None, metadata));
        await actor.EnqueueAsync(new WorkflowExecutionCommandEnvelope("notice-retry", target.ChildWorkflowExecutionId,
            new WorkflowExecutionCommand("notice-retry", target.ChildWorkflowExecutionId, WorkflowExecutionCommandKind.RunSchedulerWork, now, null, metadata),
            "notice-retry", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
        root = (await activities.FindAsync(target.ChildWorkflowExecutionId, "notice-root"))!;
        Assert.Equal(ActivityExecutionStatus.Running, root.Status);
        Assert.Equal(0, root.FaultCount);
        Assert.False(root.ProducedControl!.IsTerminal);
        Assert.Equal(0, root.ProducedControl.Ordinal);
        Assert.Equal("WARN", Assert.Single(root.ProducedControl.Notifications).Payload!.Value.GetProperty("code").GetString());
        Assert.Empty(await host.Provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(target.ChildWorkflowExecutionId));
        Assert.Empty(await host.Provider.GetRequiredService<IIncidentStateStore>().ListAsync(target.ChildWorkflowExecutionId));
        Assert.Empty(await host.Provider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(target.ChildWorkflowExecutionId));
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now += value;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Only_a_call_under_a_new_declared_executable_opts_into_control(bool declared, bool call)
    {
        await using var host = await F.CreateAsync(["admission-parent", "admission-child"]);
        var executable = host.NewExecutable([F.EventWaitNode("node-call", "hold")],
            [F.StartEvent(), call ? F.CallActivity("call", "node-call") : F.Task("call", "node-call"), F.EndEvent()],
            [F.Flow("start-call", "start", "call"), F.Flow("call-end", "call", "end")],
            [F.NodeResumeTarget("node-call", Elsa.Activities.Primitives.Activities.Event.ResumeTargetId)], controlCapable: declared);
        var run = await host.RunAsync(executable);
        var child = run.State("node-call");
        Assert.Equal(declared && call ? "elsa.bpmn" : null,
            child.Provenance.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ChildControlConsumerKey));
        Assert.Equal(declared && call ? "1" : null,
            child.Provenance.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ChildControlConsumerVersion));
    }

    [Theory]
    [InlineData("sibling", "unique durable direct-child")]
    [InlineData("token", "work-token identity")]
    [InlineData("protocol", "consumer protocol")]
    public async Task Constructed_control_cannot_bypass_exact_local_call_identity(string defect, string expected)
    {
        await using var host = await F.CreateAsync(["identity-parent", "identity-call", "identity-extra"]);
        var executable = Executable(host, includeEscalation: false);
        var run = await host.RunAsync(executable);
        var parent = run.State(F.ProcessNodeId);
        var child = run.State("node-call");
        if (defect == "token")
            await host.Provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(child with
                { Provenance = child.Provenance with { ExecutionPathId = null } });
        if (defect == "sibling") child = child with { Execution = child.Execution with { ActivityExecutionId = "unrelated-call" } };
        var runtime = host.Provider.GetRequiredService<IBpmnProcessRuntime>();
        var context = Context(runtime, executable, parent, child);
        var control = Transport(context, child, 0, false, [new RuntimeChildControlNotification("ignored")], consumerKey: defect == "protocol" ? "foreign" : "elsa.bpmn");
        var failure = await Assert.ThrowsAsync<BpmnHostException>(async () => await runtime.ControlAsync(context,
            new RuntimeChildControlContext(context, child.InvocationId, child.Execution.ExecutableNodeId, child.IterationId,
                child.Provenance.Metadata, [control])));
        Assert.Contains(expected, failure.Message, StringComparison.Ordinal);
        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.Empty(context.GetChildSubtreeCancellationRequests());
    }

    [Theory]
    [InlineData("signal-end")]
    [InlineData("signal-fault")]
    [InlineData("signal-wait-fault")]
    public async Task Called_root_emits_whole_control_instead_of_a_local_parent_notice(string scenario)
    {
        var target = new RuntimeChildControlTarget("outer-workflow", "outer-call", "outer-consumer",
            WorkflowExecutionHarness.Identity, "called-root", "elsa.bpmn", 1);
        var boundary = new AdmittedRoot(target);
        await using var host = await F.CreateAsync(["called-root", "called-wait"], services =>
            services.AddScoped<IRuntimeWorkflowControlBoundary>(_ => boundary), target.ChildWorkflowExecutionId);
        var elements = new List<BpmnElement> { F.StartEvent(), F.EscalationThrow("notice", "WARN"), F.EndEvent() };
        var flows = new List<BpmnSequenceFlow> { F.Flow("start-notice", "start", "notice") };
        var children = new List<ExecutableNode>();
        if (scenario == "signal-wait-fault")
        {
            elements.Add(F.Task("wait", "node-wait"));
            children.Add(F.EventWaitNode("node-wait", "hold"));
            flows.Add(F.Flow("notice-wait", "notice", "wait"));
            flows.Add(F.Flow("wait-fault", "wait", "failure"));
        }
        else flows.Add(F.Flow("notice-final", "notice", scenario == "signal-end" ? "end" : "failure"));
        if (scenario != "signal-end")
            elements.Add(new BpmnElement("failure", BpmnElementTypes.EndEvent, eventDefinitions:
                [new BpmnEventDefinition(BpmnEventDefinitionTypes.Error,
                    new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = "ORDER" })]));
        var executable = host.NewExecutable(children, elements, flows,
            children.Select(child => F.NodeResumeTarget(child.ExecutableNodeId, Elsa.Activities.Primitives.Activities.Event.ResumeTargetId)).ToArray());
        var run = await host.RunAsync(executable);
        var root = run.State(F.ProcessNodeId);
        Assert.NotNull(root.ProducedControl);
        Assert.Equal(0, root.ProducedControl.Ordinal);
        Assert.Equal(target.ChildWorkflowExecutionId, root.ProducedControl.ChildWorkflowExecutionId);
        Assert.Equal("WARN", Assert.Single(root.ProducedControl.Notifications).Payload!.Value.GetProperty("code").GetString());
        Assert.Equal(scenario != "signal-wait-fault", root.ProducedControl.IsTerminal);
        Assert.Equal(scenario == "signal-fault" ? "ORDER" : null, root.ProducedControl.AuthoredFailureCode);
        if (scenario == "signal-wait-fault")
        {
            run = await host.ResumeAsync(Assert.Single(await host.BookmarksAsync()), JsonSerializer.SerializeToElement(new { }));
            root = run.State(F.ProcessNodeId);
            Assert.Equal(1, root.ProducedControl!.Ordinal);
            Assert.True(root.ProducedControl.IsTerminal);
            Assert.Empty(root.ProducedControl.Notifications);
            Assert.Equal("ORDER", root.ProducedControl.AuthoredFailureCode);
        }
        Assert.NotNull(run.WorkflowState);
        Assert.Equal(scenario == "signal-end" ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Faulted, run.WorkflowState.Status);
    }

    private sealed class AdmittedRoot(RuntimeChildControlTarget target) : IRuntimeWorkflowControlBoundary
    {
        public int UnavailableCount { get; set; }
        public int FailOnCall { get; set; }
        private int _calls;
        public int Calls => _calls;

        public ValueTask<RuntimeChildControlTarget?> ResolveAsync(string workflowExecutionId, string rootActivityExecutionId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(target.ChildWorkflowExecutionId, workflowExecutionId);
            Assert.Equal(target.RootActivityExecutionId, rootActivityExecutionId);
            if (++_calls == FailOnCall || UnavailableCount-- > 0) throw new WorkflowControlBoundaryException(workflowExecutionId, rootActivityExecutionId,
                new IOException("One-shot boundary store outage."), isUnavailable: true);
            return ValueTask.FromResult<RuntimeChildControlTarget?>(target);
        }
    }

    private static WorkflowExecutable Executable(F host, bool includeEscalation, bool interrupting = false)
    {
        var elements = new List<BpmnElement>
        {
            F.StartEvent(), F.CallActivity("call", "node-call"), F.Task("error-work", "node-error"), F.EndEvent(),
            new("error", BpmnElementTypes.BoundaryEvent, attachedToRef: "call", eventDefinitions:
                [new(BpmnEventDefinitionTypes.Error, new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = "ORDER" })])
        };
        var flows = new List<BpmnSequenceFlow> { F.Flow("start-call", "start", "call"), F.Flow("call-end", "call", "end"),
            F.Flow("error-work", "error", "error-work"), F.Flow("caught-end", "error-work", "end") };
        var children = new List<ExecutableNode> { F.EventWaitNode("node-call", "hold"), F.EventWaitNode("node-error", "caught") };
        if (includeEscalation)
        {
            elements.Add(F.EscalationBoundary("warning", "call", "WARN", cancelActivity: interrupting));
            elements.Add(F.Task("warning-work", "node-warning"));
            children.Add(F.EventWaitNode("node-warning", "warning"));
            flows.Add(F.Flow("warning-work", "warning", "warning-work"));
            flows.Add(F.Flow("warning-end", "warning-work", "end"));
        }
        return host.NewExecutable(children, elements, flows,
            children.Select(child => F.NodeResumeTarget(child.ExecutableNodeId, Elsa.Activities.Primitives.Activities.Event.ResumeTargetId)).ToArray());
    }

    private static SimpleActivityExecutionContext Context(IBpmnProcessRuntime runtime, WorkflowExecutable executable,
        ActivityExecutionState parent, ActivityExecutionState child) => new(new Activities.BpmnProcess(runtime), CancellationToken.None,
        "wfexec-1", WorkflowExecutionHarness.Identity,
        new RuntimeSchedulerWorkItem("control", "wfexec-1", "command", WorkflowExecutionCommandKind.DeliverChildControl,
            "envelope", "idempotency", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch), executable.RootActivity, parent,
        liveChildActivities: [new RuntimeLiveChildActivity(child.InvocationId, child.Execution.ExecutableNodeId, child.Status, child.IterationId)]);

    private static RuntimeChildControlTransport Transport(IRuntimeActivityExecutionContext context, ActivityExecutionState child,
        long ordinal, bool terminal, IReadOnlyList<RuntimeChildControlNotification> notifications, string? failure = null, string consumerKey = "elsa.bpmn")
    {
        var identity = new WorkflowDispatchIdentity(context.WorkflowExecutionId, child.InvocationId);
        var commit = "source-" + ordinal;
        return new RuntimeChildControlTransport(identity.DispatchId, context.WorkflowExecutionId, child.InvocationId,
            context.ActivityExecutionState.InvocationId, context.PinnedExecutable, commit, "intent-" + ordinal,
            RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit, "intent-" + ordinal),
            new RuntimeChildControlBatch(1, consumerKey, 1, identity.ChildWorkflowExecutionId, "called-root", "source-work-" + ordinal,
                ordinal, terminal, notifications, failure));
    }
}

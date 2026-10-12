using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnFaultAndTerminateTests
{
    [Fact]
    public async Task TerminateEndEvent_EndsProcess_WhileSiblingBranchStillPending()
    {
        // Parallel fork: one branch reaches a terminate end event immediately (its task is a
        // pass-through), the other branch schedules a child. Terminate must end the whole process.
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-b"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-b")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ParallelGateway("fork"),
                BpmnRuntimeFixture.Task("task-a"),
                BpmnRuntimeFixture.Task("task-b", childNodeId: "node-b"),
                BpmnRuntimeFixture.TerminateEndEvent(),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "fork"),
                BpmnRuntimeFixture.Flow("flow-2", "fork", "task-a"),
                BpmnRuntimeFixture.Flow("flow-3", "fork", "task-b"),
                BpmnRuntimeFixture.Flow("flow-4", "task-a", "terminate"),
                BpmnRuntimeFixture.Flow("flow-5", "task-b", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);

        var state = await fixture.GetBpmnStateAsync();
        Assert.True(state.Terminated);
        Assert.Contains(state.Diagnostics, diagnostic => diagnostic.Kind == BpmnDiagnosticKind.Terminated);
        Assert.Empty(state.ActiveChildren);
    }

    [Fact]
    public async Task FaultingChild_FaultsProcess_InsteadOfHanging()
    {
        // Diamond onto a parallel join: node-b faults, so the join can never fire; the process must
        // fault deterministically (Flowchart #308 parity).
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-b", "actexec-c"]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewFaultingNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-d")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ParallelGateway("fork"),
                BpmnRuntimeFixture.Task("task-b", childNodeId: "node-b"),
                BpmnRuntimeFixture.Task("task-c", childNodeId: "node-c"),
                BpmnRuntimeFixture.ParallelGateway("join"),
                BpmnRuntimeFixture.Task("task-d", childNodeId: "node-d"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "fork"),
                BpmnRuntimeFixture.Flow("flow-2", "fork", "task-b"),
                BpmnRuntimeFixture.Flow("flow-3", "fork", "task-c"),
                BpmnRuntimeFixture.Flow("flow-4", "task-b", "join"),
                BpmnRuntimeFixture.Flow("flow-5", "task-c", "join"),
                BpmnRuntimeFixture.Flow("flow-6", "join", "task-d"),
                BpmnRuntimeFixture.Flow("flow-7", "task-d", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        var faultedBranch = run.State("node-b");
        Assert.Equal(ActivityExecutionStatus.Faulted, faultedBranch.Status);
        Assert.NotEmpty(faultedBranch.IncidentIds);

        run.AssertDidNotRun("node-d");

        var process = run.State(BpmnRuntimeFixture.ProcessNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, process.Status);
        Assert.NotEmpty(process.IncidentIds);

        var persistedState = JsonSerializer.Deserialize<BpmnExecutionState>(
            process.PrivateState!.Value.InlineValue!.Value.GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains(persistedState!.Diagnostics, diagnostic => diagnostic.Kind == BpmnDiagnosticKind.Faulted);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
    }

    [Fact]
    public async Task ParallelJoin_UnreachableInboundFlow_FaultsAsDeadlock()
    {
        // An exclusive decision routes to only one branch, but both branches reconverge at a parallel
        // join: the missing arrival can never be produced, so the engine faults deterministically
        // instead of leaving the process Running forever.
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-decision", "actexec-a"]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-decision", outcomes: ["A"]),
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ExclusiveGateway("decision", childNodeId: "node-decision"),
                BpmnRuntimeFixture.Task("task-a", childNodeId: "node-a"),
                BpmnRuntimeFixture.Task("task-b", childNodeId: "node-b"),
                BpmnRuntimeFixture.ParallelGateway("join"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "decision"),
                BpmnRuntimeFixture.Flow("flow-a", "decision", "task-a", conditionOutcome: "A"),
                BpmnRuntimeFixture.Flow("flow-b", "decision", "task-b", conditionOutcome: "B"),
                BpmnRuntimeFixture.Flow("flow-2", "task-a", "join"),
                BpmnRuntimeFixture.Flow("flow-3", "task-b", "join"),
                BpmnRuntimeFixture.Flow("flow-4", "join", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        var process = run.State(BpmnRuntimeFixture.ProcessNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, process.Status);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
    }

    [Fact]
    public async Task TaskWithoutMatchingFlow_AndNoDefault_Faults()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-a"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a", outcomes: ["Unmatched"])],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("task-a", childNodeId: "node-a"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "task-a"),
                BpmnRuntimeFixture.Flow("flow-2", "task-a", "end", conditionOutcome: "Done")
            ]);

        var run = await fixture.RunAsync(executable);

        var process = run.State(BpmnRuntimeFixture.ProcessNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, process.Status);
    }
}

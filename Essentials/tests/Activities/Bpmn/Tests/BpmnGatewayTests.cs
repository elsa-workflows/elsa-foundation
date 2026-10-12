using Elsa.Activities.Bpmn.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnGatewayTests
{
    [Fact]
    public async Task ParallelForkAndJoin_RunsBothBranches_JoinsOnce_AndCompletes()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-a", "actexec-b", "actexec-c"]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ParallelGateway("fork"),
                BpmnRuntimeFixture.Task("task-a", childNodeId: "node-a"),
                BpmnRuntimeFixture.Task("task-b", childNodeId: "node-b"),
                BpmnRuntimeFixture.ParallelGateway("join"),
                BpmnRuntimeFixture.Task("task-c", childNodeId: "node-c"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "fork"),
                BpmnRuntimeFixture.Flow("flow-2", "fork", "task-a"),
                BpmnRuntimeFixture.Flow("flow-3", "fork", "task-b"),
                BpmnRuntimeFixture.Flow("flow-4", "task-a", "join"),
                BpmnRuntimeFixture.Flow("flow-5", "task-b", "join"),
                BpmnRuntimeFixture.Flow("flow-6", "join", "task-c"),
                BpmnRuntimeFixture.Flow("flow-7", "task-c", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertRan("node-a", "node-b", "node-c");
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();

        var state = await fixture.GetBpmnStateAsync();
        Assert.Contains(state.Diagnostics, diagnostic => diagnostic.Kind == BpmnDiagnosticKind.Joined && diagnostic.ElementId == "join");
        // task-c ran exactly once: the join merged both arrivals into one token.
        Assert.Single(run.States("node-c"));
    }

    [Fact]
    public async Task ExclusiveGateway_WithBoundDecision_RoutesByOutcome()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-decision", "actexec-approved"]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-decision", outcomes: ["Approved"]),
                fixture.NewProbeNode("node-approved"),
                fixture.NewProbeNode("node-rejected")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ExclusiveGateway("decision", childNodeId: "node-decision"),
                BpmnRuntimeFixture.Task("task-approved", childNodeId: "node-approved"),
                BpmnRuntimeFixture.Task("task-rejected", childNodeId: "node-rejected"),
                BpmnRuntimeFixture.EndEvent("end-approved"),
                BpmnRuntimeFixture.EndEvent("end-rejected")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "decision"),
                BpmnRuntimeFixture.Flow("flow-approved", "decision", "task-approved", conditionOutcome: "Approved"),
                BpmnRuntimeFixture.Flow("flow-rejected", "decision", "task-rejected", isDefault: true),
                BpmnRuntimeFixture.Flow("flow-2", "task-approved", "end-approved"),
                BpmnRuntimeFixture.Flow("flow-3", "task-rejected", "end-rejected")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertRan("node-decision", "node-approved");
        run.AssertDidNotRun("node-rejected");
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task ExclusiveGateway_WithoutMatch_TakesDefaultFlow()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-decision", "actexec-rejected"]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-decision", outcomes: ["SomethingElse"]),
                fixture.NewProbeNode("node-approved"),
                fixture.NewProbeNode("node-rejected")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.ExclusiveGateway("decision", childNodeId: "node-decision"),
                BpmnRuntimeFixture.Task("task-approved", childNodeId: "node-approved"),
                BpmnRuntimeFixture.Task("task-rejected", childNodeId: "node-rejected"),
                BpmnRuntimeFixture.EndEvent("end-approved"),
                BpmnRuntimeFixture.EndEvent("end-rejected")
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "decision"),
                BpmnRuntimeFixture.Flow("flow-approved", "decision", "task-approved", conditionOutcome: "Approved"),
                BpmnRuntimeFixture.Flow("flow-rejected", "decision", "task-rejected", isDefault: true),
                BpmnRuntimeFixture.Flow("flow-2", "task-approved", "end-approved"),
                BpmnRuntimeFixture.Flow("flow-3", "task-rejected", "end-rejected")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertRan("node-decision", "node-rejected");
        run.AssertDidNotRun("node-approved");
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
    }

    [Fact]
    public async Task InclusiveGateway_ActivationAwareJoin_FiresWithSingleActivatedBranch()
    {
        // The decision activates only the "Approved" branch; the inclusive join must fire once that
        // branch arrives, because nothing live can still reach the other inbound flow.
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-decision", "actexec-approved", "actexec-after"]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-decision", outcomes: ["Approved"]),
                fixture.NewProbeNode("node-approved"),
                fixture.NewProbeNode("node-extra"),
                fixture.NewProbeNode("node-after")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.InclusiveGateway("split", childNodeId: "node-decision"),
                BpmnRuntimeFixture.Task("task-approved", childNodeId: "node-approved"),
                BpmnRuntimeFixture.Task("task-extra", childNodeId: "node-extra"),
                BpmnRuntimeFixture.InclusiveGateway("join"),
                BpmnRuntimeFixture.Task("task-after", childNodeId: "node-after"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "split"),
                BpmnRuntimeFixture.Flow("flow-approved", "split", "task-approved", conditionOutcome: "Approved"),
                BpmnRuntimeFixture.Flow("flow-extra", "split", "task-extra", conditionOutcome: "Extra"),
                BpmnRuntimeFixture.Flow("flow-2", "task-approved", "join"),
                BpmnRuntimeFixture.Flow("flow-3", "task-extra", "join"),
                BpmnRuntimeFixture.Flow("flow-4", "join", "task-after"),
                BpmnRuntimeFixture.Flow("flow-5", "task-after", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertRan("node-decision", "node-approved", "node-after");
        run.AssertDidNotRun("node-extra");
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task InclusiveGateway_BothBranchesActivated_JoinWaitsForBoth()
    {
        // Without a bound decision child, an inclusive split takes every unconditional flow — both
        // branches are activated, so the activation-aware join must wait for both.
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-a", "actexec-b", "actexec-after"]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-after")
            ],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.InclusiveGateway("split"),
                BpmnRuntimeFixture.Task("task-a", childNodeId: "node-a"),
                BpmnRuntimeFixture.Task("task-b", childNodeId: "node-b"),
                BpmnRuntimeFixture.InclusiveGateway("join"),
                BpmnRuntimeFixture.Task("task-after", childNodeId: "node-after"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "split"),
                BpmnRuntimeFixture.Flow("flow-a", "split", "task-a"),
                BpmnRuntimeFixture.Flow("flow-b", "split", "task-b"),
                BpmnRuntimeFixture.Flow("flow-2", "task-a", "join"),
                BpmnRuntimeFixture.Flow("flow-3", "task-b", "join"),
                BpmnRuntimeFixture.Flow("flow-4", "join", "task-after"),
                BpmnRuntimeFixture.Flow("flow-5", "task-after", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertRan("node-a", "node-b", "node-after");
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        // task-after ran exactly once: the join merged both arrivals.
        Assert.Single(run.States("node-after"));
    }
}

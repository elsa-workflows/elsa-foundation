using Elsa.Activities.Bpmn.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnProcessTests
{
    [Fact]
    public async Task LinearProcess_RunsBoundTask_AndCompletes()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-a"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("task-a", childNodeId: "node-a"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "task-a"),
                BpmnRuntimeFixture.Flow("flow-2", "task-a", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertRan("node-a");
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();

        // The last persisted (Defer) state reflects the scheduled child; terminal continuations do not
        // rewrite the visible private state.
        var state = await fixture.GetBpmnStateAsync();
        Assert.Contains(state.Diagnostics, diagnostic => diagnostic.Kind == BpmnDiagnosticKind.Scheduled && diagnostic.ElementId == "task-a");
    }

    [Fact]
    public async Task UnboundAbstractTask_IsPassThrough()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-a"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a")],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                BpmnRuntimeFixture.Task("task-noop"),
                BpmnRuntimeFixture.Task("task-a", childNodeId: "node-a"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "task-noop"),
                BpmnRuntimeFixture.Flow("flow-2", "task-noop", "task-a"),
                BpmnRuntimeFixture.Flow("flow-3", "task-a", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertRan("node-a");
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
    }

    [Fact]
    public async Task EmptyProcess_CompletesImmediately()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn"]);
        var executable = fixture.NewExecutable(children: [], elements: [], sequenceFlows: []);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task NestedBpmnProcess_AsSubprocessChild_RunsToCompletion()
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["actexec-bpmn", "actexec-inner", "actexec-a"]);

        var innerChildren = new[] { fixture.NewProbeNode("node-a") };

        // The inner BPMN process is itself an ordinary child activity bound by a subProcess element.
        var innerNode = new Elsa.Workflows.Runtime.Core.Models.ExecutableNode(
            executableNodeId: "node-inner",
            authoredActivityId: "authored-inner",
            activityType: typeof(Elsa.Activities.Bpmn.Activities.BpmnProcess).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "InnerDescriptor",
            descriptorPayload: System.Text.Json.JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, Elsa.Workflows.Runtime.Core.Models.RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new Elsa.Workflows.Runtime.Core.Models.ExecutableChildSlot(
                    Elsa.Activities.Bpmn.Activities.BpmnProcess.ActivitiesSlotName,
                    innerChildren)
            ],
            structure: new Elsa.Workflows.Runtime.Core.Models.ExecutableActivityStructure(
                Elsa.Activities.Bpmn.Activities.BpmnProcess.StructureKind,
                Elsa.Activities.Bpmn.Activities.BpmnProcess.StructureSchemaVersion,
                System.Text.Json.JsonSerializer.SerializeToElement(new BpmnStructure(
                    [
                        BpmnRuntimeFixture.StartEvent("inner-start"),
                        BpmnRuntimeFixture.Task("inner-task", childNodeId: "node-a"),
                        BpmnRuntimeFixture.EndEvent("inner-end")
                    ],
                    [
                        BpmnRuntimeFixture.Flow("inner-flow-1", "inner-start", "inner-task"),
                        BpmnRuntimeFixture.Flow("inner-flow-2", "inner-task", "inner-end")
                    ]))));

        var executable = fixture.NewExecutable(
            children: [innerNode],
            elements:
            [
                BpmnRuntimeFixture.StartEvent(),
                new BpmnElement("sub-1", BpmnElementTypes.SubProcess, childNodeId: "node-inner"),
                BpmnRuntimeFixture.EndEvent()
            ],
            sequenceFlows:
            [
                BpmnRuntimeFixture.Flow("flow-1", "start", "sub-1"),
                BpmnRuntimeFixture.Flow("flow-2", "sub-1", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertRan("node-a");
        run.AssertCompleted("node-inner");
        run.AssertCompleted(BpmnRuntimeFixture.ProcessNodeId);
        run.AssertWorkflowCompleted();
    }
}

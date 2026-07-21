using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using static Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Activities.Bpmn.Tests;

/// <summary>
/// Runtime start-semantics tests for BPMN event-defined start events (spec 117 D6): trigger-delivery vs
/// direct-invocation seeding and the two deterministic <c>bpmn.start.*</c> faults. Each event start routes through
/// a probe task so the executed branch is observable and the process persists an intermediate state.
/// </summary>
public sealed class BpmnEventStartRuntimeTests
{
    private static readonly string[] IdPool = ["actexec-bpmn", "actexec-a", "actexec-b"];

    [Fact]
    public async Task TriggerDelivery_SeedsOnlyTargetedStart_AndRoutesToCompletion()
    {
        await using var fixture = await CreateAsync(IdPool);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b")],
            elements:
            [
                EventStart("msg-a", BpmnEventDefinitionTypes.Message),
                EventStart("msg-b", BpmnEventDefinitionTypes.Message),
                Task("task-a", "node-a"), Task("task-b", "node-b"), EndEvent("end")
            ],
            sequenceFlows:
            [
                Flow("f1", "msg-a", "task-a"), Flow("f2", "task-a", "end"),
                Flow("f3", "msg-b", "task-b"), Flow("f4", "task-b", "end")
            ]);

        var run = await fixture.RunAsTriggerAsync(executable, startElementId: "msg-a");

        run.AssertCompleted(ProcessNodeId);
        run.AssertCompleted("node-a");
        run.AssertDidNotRun("node-b");
        var state = await fixture.GetBpmnStateAsync();
        Assert.Contains(state.Diagnostics, d => d.Kind == BpmnDiagnosticKind.TokenEmitted && d.ElementId == "msg-a" && d.Message!.Contains("trigger delivery"));
    }

    [Fact]
    public async Task DirectInvocation_SeedsOnlyNoneStarts_AndLeavesEventStartsDormant()
    {
        await using var fixture = await CreateAsync(IdPool);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b")],
            elements:
            [
                StartEvent("none-start"), EventStart("sig-start", BpmnEventDefinitionTypes.Signal),
                Task("task-a", "node-a"), Task("task-b", "node-b"), EndEvent("end")
            ],
            sequenceFlows:
            [
                Flow("f1", "none-start", "task-a"), Flow("f2", "task-a", "end"),
                Flow("f3", "sig-start", "task-b"), Flow("f4", "task-b", "end")
            ]);

        var run = await fixture.RunAsync(executable);

        run.AssertCompleted(ProcessNodeId);
        run.AssertCompleted("node-a");
        run.AssertDidNotRun("node-b");
    }

    [Fact]
    public async Task DirectInvocation_WithOnlyEventDefinedStarts_FaultsDeterministically()
    {
        await using var fixture = await CreateAsync(["actexec-bpmn"]);
        var executable = fixture.NewExecutable(
            children: [],
            elements: [EventStart("timer-start", BpmnEventDefinitionTypes.Timer), EndEvent("end")],
            sequenceFlows: [Flow("f1", "timer-start", "end")]);

        var run = await fixture.RunAsync(executable);

        var process = run.State(ProcessNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, process.Status);
        Assert.Equal("bpmn.start.none-available", process.Fault?.Code);
    }

    [Fact]
    public async Task TriggerDelivery_WithUnresolvableStartElement_FaultsDeterministically()
    {
        await using var fixture = await CreateAsync(["actexec-bpmn"]);
        var executable = fixture.NewExecutable(
            children: [],
            elements: [EventStart("msg-a", BpmnEventDefinitionTypes.Message), EndEvent("end")],
            sequenceFlows: [Flow("f1", "msg-a", "end")]);

        var run = await fixture.RunAsTriggerAsync(executable, startElementId: "ghost");

        var process = run.State(ProcessNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, process.Status);
        Assert.Equal("bpmn.start.unresolved-trigger", process.Fault?.Code);
    }

    [Fact]
    public async Task TriggerDelivery_TokenIds_AreDeterministicAcrossRuns()
    {
        var tokenIdsPerRun = new List<string[]>();
        for (var i = 0; i < 2; i++)
        {
            await using var fixture = await CreateAsync(IdPool);
            var executable = fixture.NewExecutable(
                children: [fixture.NewProbeNode("node-a")],
                elements: [EventStart("msg-a", BpmnEventDefinitionTypes.Message), Task("task-a", "node-a"), EndEvent("end")],
                sequenceFlows: [Flow("f1", "msg-a", "task-a"), Flow("f2", "task-a", "end")]);

            await fixture.RunAsTriggerAsync(executable, startElementId: "msg-a");
            var state = await fixture.GetBpmnStateAsync();
            tokenIdsPerRun.Add(state.Tokens.Select(token => token.TokenId).OrderBy(x => x).ToArray());
        }

        Assert.Equal(tokenIdsPerRun[0], tokenIdsPerRun[1]);
    }
}

using System.Text.Json;
using Kernel.Foundation.Contracts;
using Kernel.Graph.Models;
using Kernel.Native;
using Xunit;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class NativeEventFlowchartEntryTests
{
    [Fact]
    public async Task An_older_HTTP_checkpoint_keeps_its_loop_boundary_when_the_owner_accepts_Event_starts()
    {
        // The detached loop is valid with the stock DFS. Starting a DFS from Event would close at b instead,
        // which has two inbound edges and invalidates this entire chart, including the independent HTTP path.
        var workflow = StockWorkflow();
        var stock = Model(workflow, events: false);
        var eventOwner = Model(StockWorkflow(), events: true);

        Assert.Equal("aa-event", Assert.Single(NativeStartTriggers.StartEvents(workflow)).Id);
        Assert.Equal("zz-http", Assert.Single(NativeStartTriggers.StartEndpoints(workflow)).Id);
        Assert.True(Flowchart.GraphOf(workflow.Root).IsBackwardEdge("b", "a"));
        Assert.False(Flowchart.GraphOf(workflow.Root).IsBackwardEdge("a", "b"));

        var first = await NativeTurn.StartAsync(stock, workflow, mode: Modes.Durable, triggerNode: "zz-http",
            trigger: Values.From(new HttpRequest("POST", "/orders", null)));
        Assert.Equal("zz-http", Active(first));
        Assert.DoesNotContain(nameof(NativeState.EmptyEventStartPending), JsonSerializer.Serialize(first.Native));

        var continued = await first.ResumeAsync(eventOwner);
        Assert.Equal("http-output", Active(continued));
        var finished = await continued.ResumeAsync(eventOwner);
        Assert.Equal(ActorStatus.Finished, finished.Status);
        Assert.Equal("http", finished.Output!["result"]!.GetValue<string>());

        var nullTrigger = await NativeTurn.StartAsync(stock, workflow, mode: Modes.Durable, triggerNode: "aa-event");
        Assert.Equal("start", Active(nullTrigger));
        Assert.False(nullTrigger.Native.EmptyEventStartPending);
    }

    [Fact]
    public async Task A_selected_Event_with_no_payload_enters_a_supported_loop_after_rehydration()
    {
        var workflow = Wf.Workflow("workflow", Wf.Flowchart("root",
            [Wf.SetOutput("start", "result", "default"), Wf.Event("event", "orders"),
                Wf.Flowchart("loop", [Wf.SetOutput("a", "result", "a"), Wf.SetOutput("b", "result", "b")],
                    [new("a", "b"), new("b", "a")])],
            [new("event", "loop")]));
        var host = Model(workflow, events: true);
        var first = await NativeTurn.StartAsync(host, workflow, mode: Modes.Durable, triggerNode: "event");
        Assert.Equal("event", Active(first));
        Assert.True(first.Native.EmptyEventStartPending);

        var replacement = Model(workflow, events: true);
        var checkpoint = await first.ResumeAsync(replacement);
        Assert.Equal("loop", Active(checkpoint));
        Assert.Equal("orders", checkpoint.Native.Results["event"]!["EventName"]!.GetValue<string>());
        Assert.Null(checkpoint.Native.Results["event"]!["Payload"]);
        Assert.Null(checkpoint.Native.TriggerNode);
        Assert.False(checkpoint.Native.EmptyEventStartPending);

        var entered = await checkpoint.ResumeAsync(replacement);
        Assert.Equal("a", Active(entered, "loop"));
        var continued = await entered.ResumeAsync(replacement);
        Assert.Equal("b", Active(continued, "loop"));
        Assert.Empty(GraphState(continued, "loop").LoopIterationCounters);
        var firstLoop = await continued.ResumeAsync(replacement);
        Assert.Equal("a", Active(firstLoop, "loop"));
        Assert.Equal(1, GraphState(firstLoop, "loop").LoopIterationCounters["a"]);
    }

    private static NativeExecutionModel Model(Workflow workflow, bool events) =>
        new(new WorkflowRegistry().Add(workflow), new() { AuthoredEventStarts = events });

    private static Workflow StockWorkflow() => Wf.Workflow("workflow", Wf.Flowchart("root",
        [Wf.SetOutput("start", "result", "default"), Wf.Event("aa-event", "orders"), Wf.HttpEndpoint("zz-http", "/orders"),
            Wf.SetOutput("http-output", "result", "http"),
            Wf.SetOutput("a", "result", "a"), Wf.SetOutput("b", "result", "b")],
        [new("aa-event", "b"), new("zz-http", "http-output"), new("a", "b"), new("b", "a")]));

    private static string Active(NativeTurn turn, string nodeId = "root") => Assert.Single(GraphState(turn, nodeId).ActiveChildren).NodeId;

    private static FlowchartExecutionState GraphState(NativeTurn turn, string nodeId)
    {
        var frame = turn.Native.Frames.Values.SingleOrDefault(frame => frame.Node == nodeId);
        Assert.True(frame is not null, $"The {nodeId} frame is missing. {turn.Status} {turn.Output}");
        return Flowchart.ReadEngineState(frame.State)!;
    }
}

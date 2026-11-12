using System.Text.Json.Nodes;

namespace Kernel.Native;

// The ported activities. Semantics follow the Elsa 4 sources the activity map names; what differs is in NATIVE.md.

// ---- primitives ------------------------------------------------------------------------------------------------------

public sealed class WriteLine : Activity
{
    /// <summary>Where the lines go. Null in measurements, so that the console is not what is measured.</summary>
    public static TextWriter? Out { get; set; }

    [Input] public string? Text { get; set; }

    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        Out?.WriteLine(Text);
        return new(Complete());
    }
}

/// <summary>A variable write is an engine intrinsic: the interpreter applies it, as Elsa 4's Set intrinsic.</summary>
public sealed class SetVariable : ActivityBase, IIntrinsic
{
    [Input] public string Name { get; set; } = "";
    [Input] public JsonNode? Value { get; set; }
}

/// <summary>A side effect that is not safe to replay: a call to a partner that is not idempotent. The host performs it.</summary>
[External]
public sealed class SendNotification : Activity
{
    [Input] public string? Message { get; set; }
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) => new(Complete());
}

/// <summary>Slow external work. Background: it runs as the kernel's off-step work, so the instance still takes a cancel or a timer meanwhile.</summary>
[Background]
public sealed class CallService : Activity<string>
{
    [Input] public string Service { get; set; } = "";
    [Input] public long Milliseconds { get; set; }

    protected override async ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        if (Milliseconds > 0) await Task.Delay(TimeSpan.FromMilliseconds(Milliseconds), context.CancellationToken);
        return Complete($"answer of {Service}");
    }
}

// ---- composites ------------------------------------------------------------------------------------------------------

/// <summary>Stateless: the next child follows from the position of the one that completed.</summary>
public sealed class Sequence : CompositeActivity
{
    protected internal override void Execute(CompositeContext context)
    {
        if (context.Children.Count == 0) context.Complete();
        else context.Schedule(context.Children[0]);
    }

    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        if (child.Outcome == "Break")
        {
            context.Complete("Break");
            return;
        }
        var next = context.Children.ToList().FindIndex(c => c.Id == child.Node) + 1;
        if (next < context.Children.Count) context.Schedule(context.Children[next]);
        else context.Complete();
    }
}

/// <summary>Then and Else are the children tagged by the builder; the outcome is True or False either way, so a flowchart can route on it.</summary>
public sealed class If : CompositeActivity
{
    [Input] public bool Condition { get; set; }

    protected internal override void Execute(CompositeContext context)
    {
        var outcome = Condition ? "True" : "False";
        if (context.Children.FirstOrDefault(c => c.Slot == (Condition ? "Then" : "Else")) is { } branch) context.Schedule(branch, outcome);
        else context.Complete(outcome);
    }

    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete(child.Tag!);
}

public sealed record ParallelState
{
    public List<string> Pending { get; set; } = [];
    public int Done { get; set; }
    public int Faulted { get; set; }
    public bool Joined { get; set; }
}

/// <summary>Fork and threshold join. It counts its branches in its own state, where Elsa 4 queried the activity-state store.</summary>
public sealed class Parallel : CompositeActivity
{
    [Input] public int? Threshold { get; set; }

    protected internal override void Execute(CompositeContext context)
    {
        if (context.Children.Count == 0)
        {
            context.Complete();
            return;
        }
        var state = new ParallelState();
        foreach (var branch in context.Children) state.Pending.Add(context.Schedule(branch, branch.Id));
        context.SetState(state);
    }

    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var state = context.GetState<ParallelState>();
        state.Pending.Remove(child.Frame);
        var guarded = !context.Flawed(NativeFlaw.JoinWithoutGuard);
        if (guarded && state.Joined) return;
        state.Done++;
        var threshold = Math.Clamp(Threshold ?? context.Children.Count, 1, context.Children.Count);
        if (state.Done >= threshold && !(guarded && state.Joined))
        {
            state.Joined = true;
            context.SetState(state);
            context.Join();     // records the join, completes, and cancels the branches still running
            return;
        }
        context.SetState(state);
    }

    protected internal override void OnChildFaulted(CompositeContext context, ChildFaulted child)
    {
        var state = context.GetState<ParallelState>();
        state.Pending.Remove(child.Frame);
        state.Faulted++;
        context.SetState(state);
        var threshold = Math.Clamp(Threshold ?? context.Children.Count, 1, context.Children.Count);
        if (context.Children.Count - state.Faulted < threshold) context.Fault(child.Fault);   // the threshold can no longer be met
    }
}

public sealed record ForEachState
{
    public JsonArray Items { get; set; } = [];
    public int Index { get; set; }
}

/// <summary>Each pass runs the body in an iteration scope that declares CurrentItem and CurrentIndex.</summary>
public sealed class ForEach : CompositeActivity
{
    [Input] public JsonArray? Items { get; set; }

    protected internal override void Execute(CompositeContext context)
    {
        var state = new ForEachState { Items = Items ?? [] };
        context.SetState(state);
        Next(context, state);
    }

    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var state = context.GetState<ForEachState>();
        if (child.Outcome == "Break")
        {
            context.Complete();
            return;
        }
        state.Index++;
        context.SetState(state);
        Next(context, state);
    }

    private static void Next(CompositeContext context, ForEachState state)
    {
        if (state.Index >= state.Items.Count || context.Children.Count == 0)
        {
            context.Complete();
            return;
        }
        context.Schedule(context.Children[0], scope: new Dictionary<string, JsonNode?>
        {
            ["CurrentItem"] = state.Items[state.Index]?.DeepClone(),
            ["CurrentIndex"] = state.Index,
        });
    }
}

/// <summary>Composite inputs are evaluated at every activation, so the condition is read again after each pass without a marker.</summary>
public sealed class While : CompositeActivity
{
    [Input] public bool Condition { get; set; }

    protected internal override void Execute(CompositeContext context)
    {
        if (Condition && context.Children.Count > 0) context.Schedule(context.Children[0]);
        else context.Complete();
    }

    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        if (child.Outcome != "Break" && Condition) context.Schedule(context.Children[0]);
        else context.Complete();
    }
}

public sealed record FlowchartState
{
    public Dictionary<string, List<string>> Arrived { get; set; } = new(StringComparer.Ordinal);
    public List<string> Fired { get; set; } = [];
}

/// <summary>
/// Follows connections from a child's outcome. A node with several inbound connections is a join: All waits for every
/// inbound connection, Any fires on the first. The flowchart completes when nothing under it runs any more.
/// </summary>
public sealed class Flowchart : CompositeActivity
{
    protected internal override void Execute(CompositeContext context)
    {
        var targets = context.Node.Connections.Select(c => c.Target).ToHashSet(StringComparer.Ordinal);
        var starts = context.Children.Where(c => !targets.Contains(c.Id)).ToList();
        if (starts.Count == 0)
        {
            context.Complete();
            return;
        }
        foreach (var start in starts) context.Schedule(start);
    }

    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child)
    {
        var state = context.GetState<FlowchartState>();
        foreach (var connection in context.Node.Connections.Where(c => c.Source == child.Node && c.Outcome == child.Outcome))
        {
            var target = context.Children.First(c => c.Id == connection.Target);
            var inbound = context.Node.Connections.Where(c => c.Target == target.Id).Select(c => c.Source + ">" + c.Outcome).Distinct().ToList();
            if (inbound.Count <= 1)
            {
                context.Schedule(target);
                continue;
            }
            if (context.Node.Joins.GetValueOrDefault(target.Id, JoinMode.All) == JoinMode.Any)
            {
                if (state.Fired.Contains(target.Id)) continue;
                state.Fired.Add(target.Id);
            }
            else
            {
                var arrived = state.Arrived.TryGetValue(target.Id, out var list) ? list : state.Arrived[target.Id] = [];
                var key = connection.Source + ">" + connection.Outcome;
                if (!arrived.Contains(key)) arrived.Add(key);
                if (!inbound.All(arrived.Contains)) continue;
                state.Arrived.Remove(target.Id);
            }
            context.RecordJoin(target.Id);
            context.Schedule(target);
        }
        context.SetState(state);
        if (context.Running.Count == 0) context.Complete();
    }
}

// ---- waiting ---------------------------------------------------------------------------------------------------------

public sealed record DelayState(long Milliseconds);

/// <summary>Elsa 4 wrote a durable timer through a scheduler. Here the trigger becomes a message to self with a due time.</summary>
public sealed class Delay : StatefulActivity<Unit, DelayState, JsonNode?>
{
    [Input] public long Milliseconds { get; set; }

    protected override ValueTask<Transition> ExecuteAsync(StartContext<JsonNode?> context) =>
        new(Suspend(new DelayState(Milliseconds), new TimerTrigger(Milliseconds)));

    protected override ValueTask<Transition> ResumeAsync(ResumeContext<DelayState, JsonNode?> context) => new(Complete(default));
}

public sealed record EventResult(string EventName, JsonNode? Payload);

public sealed record EventWaitState(string EventName);

public sealed class Event : StatefulActivity<EventResult, EventWaitState, JsonNode?>
{
    [Input] public string EventName { get; set; } = "";

    protected override ValueTask<Transition> ExecuteAsync(StartContext<JsonNode?> context) =>
        new(context.TryGetTrigger(out var payload)
            ? Complete(new EventResult(EventName, payload))
            : Suspend(new EventWaitState(EventName), new EventTrigger(EventName)));

    protected override ValueTask<Transition> ResumeAsync(ResumeContext<EventWaitState, JsonNode?> context) =>
        new(Complete(new EventResult(context.State.EventName, context.Trigger)));
}

/// <summary>Elsa 4 staged the publish through a stager. Here it is an effect: the kernel's publish, ticketed in the commit.</summary>
public sealed class PublishEvent : Activity
{
    [Input] public string EventName { get; set; } = "";
    [Input] public JsonNode? Payload { get; set; }

    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) => new(Complete().With(new PublishEffect(EventName, Payload)));
}

public sealed record DispatchResult(JsonNode? Output, string Status);

public sealed record DispatchState(string Workflow);

public sealed record ChildDone(string Status, JsonNode? Output, string Child);

/// <summary>
/// Elsa 4 read the parent's state, executable and node pin from stores and staged a dispatch record. Here: without a
/// wait, a detached start effect; with a wait, a child trigger (the kernel's Start) and its child-finished message.
/// </summary>
public sealed class DispatchWorkflow : StatefulActivity<DispatchResult, DispatchState, ChildDone>
{
    [Input] public string Workflow { get; set; } = "";
    [Input] public JsonObject? Input { get; set; }
    [Input] public bool WaitForCompletion { get; set; }
    [Input] public string Mode { get; set; } = Modes.Balanced;

    protected override ValueTask<Transition> ExecuteAsync(StartContext<ChildDone> context) =>
        new(WaitForCompletion
            ? Suspend(new DispatchState(Workflow), new ChildTrigger(Workflow, Input, Mode))
            : Complete(new DispatchResult(null, "Dispatched"), "Dispatched").With(new DispatchEffect(Workflow, Input, Mode)));

    protected override ValueTask<Transition> ResumeAsync(ResumeContext<DispatchState, ChildDone> context)
    {
        var outcome = context.Trigger.Status switch { "Finished" => "Completed", var other => other };
        return new(Complete(new DispatchResult(context.Trigger.Output, context.Trigger.Status), outcome));
    }
}

// ---- HTTP ------------------------------------------------------------------------------------------------------------

public sealed record HttpRequest(string Method, string Path, JsonNode? Body = null, string? ReplyTo = null);

public sealed record HttpResponse(int StatusCode, string ContentType, JsonNode? Body);

public sealed record HttpWaitState(string Method, string Path);

/// <summary>As a start trigger it completes with the request. In the middle of a workflow it waits for a request on its route.</summary>
public sealed class HttpEndpoint : StatefulActivity<HttpRequest, HttpWaitState, HttpRequest>
{
    [Input] public string Path { get; set; } = "";
    [Input] public string Method { get; set; } = "POST";

    protected override ValueTask<Transition> ExecuteAsync(StartContext<HttpRequest> context) =>
        new(context.TryGetTrigger(out var request)
            ? Complete(request)
            : Suspend(new HttpWaitState(Method, Path), new HttpTrigger(Method, Path)));

    protected override ValueTask<Transition> ResumeAsync(ResumeContext<HttpWaitState, HttpRequest> context) => new(Complete(context.Trigger));
}

/// <summary>The response is the activity's result and goes out on the live channel. It does not end the workflow.</summary>
public sealed class WriteHttpResponse : Activity<HttpResponse>
{
    [Input] public int StatusCode { get; set; } = 200;
    [Input] public string ContentType { get; set; } = "text/plain";
    [Input] public JsonNode? Body { get; set; }
    /// <summary>Opt out of the release rule: answer before the step's commit lands.</summary>
    [Input] public bool BeforeCommit { get; set; }

    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        var response = new HttpResponse(StatusCode, ContentType, Body);
        return new(Complete(response).With(new EmitEffect(HttpRoutes.ResponseChannel, Values.From(response), BeforeCommit)));
    }
}

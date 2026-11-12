using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kernel.Native;

public static class Modes
{
    public const string Ephemeral = "Ephemeral", Balanced = "Balanced", Durable = "Durable";
}

/// <summary>Messages the native model understands besides the kernel's own kinds (start, child-finished, job-finished, event).</summary>
public static class NativeKinds
{
    public const string Continue = "native.continue";   // a Durable boundary: the burst goes on in the next turn
    public const string Timer = "native.timer";         // a Delay is due
    public const string Resume = "native.resume";       // an outsider resumes a wait by its stimulus, without the event tables
    public const string Cancel = "native.cancel";
}

/// <summary>Deliberate defects of the model, one per check of the simulation (NATIVE.md, section 5).</summary>
[Flags]
public enum NativeFlaw
{
    None = 0,
    InFlightInMemory = 1 << 0,  // Durable: the executed work item leaves the agenda only in memory
    JoinWithoutGuard = 1 << 1,  // Parallel: no joined flag, and the join does not cancel the other branches
    ResumeAnyChild = 1 << 2,    // a child-finished resumes every child wait, not the one for that child
    NoAttemptCommit = 1 << 3,   // Durable: an External activity runs without its attempt committed first
    ScopeInMemory = 1 << 4,     // an iteration scope is kept in memory only
}

/// <summary>An HTTP start gives its trigger as the turn's ambient value instead (the kernel's Ambient: never stored).</summary>
public sealed record NativeStart(string Mode = Modes.Balanced, JsonObject? Input = null, string? TriggerNode = null, JsonNode? Trigger = null);

public sealed record TimerFired(string Wait);

public sealed record ResumeStimulus(string Event, JsonNode? Payload = null);

public sealed record BackgroundArgs(string Workflow, string Node, string Frame, JsonObject Inputs);

public sealed record BackgroundResult(TransitionKind Kind, JsonNode? Result, string Outcome, ActivityFault? Fault);

public sealed class NativeOptions
{
    public NativeFlaw Flaws { get; init; }
    /// <summary>Gates a background job before it runs (the simulation decides when it completes).</summary>
    public Func<IJob, CancellationToken, ValueTask>? Gate { get; init; }
    /// <summary>
    /// Called when an External activity's side effect happens: instance, frame, node, committed attempt, and whether the
    /// turn may have run before (the kernel's <see cref="ITurn.Redelivered"/>, friction F-N6).
    /// </summary>
    public Action<InstanceRef, string, string, int, bool>? OnExternal { get; init; }
    public int MaxActivitiesPerTurn { get; init; } = 1000;
    public TimeSpan EventWindow { get; init; } = TimeSpan.FromHours(1);
}

// ---- the state: live frames, scheduled work, waits, variables, results ---------------------------------------------

public sealed class NativeState
{
    public string Mode { get; set; } = Modes.Balanced;
    public bool Started { get; set; }
    public long Next { get; set; }
    public Dictionary<string, Frame> Frames { get; set; } = new(StringComparer.Ordinal);
    public List<Work> Agenda { get; set; } = [];
    public Dictionary<string, WaitEntry> Waits { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonNode?> Variables { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, JsonNode?> Results { get; set; } = new(StringComparer.Ordinal);
    public string? TriggerNode { get; set; }
    public JsonNode? Trigger { get; set; }
}

/// <summary>A live activity: a composite with children, or an activity that waits.</summary>
public sealed class Frame
{
    public string Id { get; set; } = "";
    public string Node { get; set; } = "";
    public string? Parent { get; set; }
    public string? Tag { get; set; }
    public JsonNode? State { get; set; }
    public Dictionary<string, JsonNode?>? Scope { get; set; }
    public int Attempt { get; set; }
    [JsonIgnore] internal Dictionary<string, JsonNode?>? LiveScope;   // only under NativeFlaw.ScopeInMemory
}

/// <summary>Scheduled work: execute a node as a new frame, or resume a frame's wait.</summary>
public sealed class Work
{
    public bool Resume { get; set; }
    public string Frame { get; set; } = "";
    public string? Node { get; set; }
    public string? Parent { get; set; }
    public string? Tag { get; set; }
    public Dictionary<string, JsonNode?>? Scope { get; set; }
    public string? Wait { get; set; }
    public JsonNode? Payload { get; set; }
    public int Attempt { get; set; }       // External in Durable: the attempt committed before it runs
    [JsonIgnore] internal Dictionary<string, JsonNode?>? LiveScope;
    [JsonIgnore] internal bool Gone;       // only under NativeFlaw.InFlightInMemory
}

public sealed record WaitEntry(string Frame, string Kind, string Key);

// ---- the model -----------------------------------------------------------------------------------------------------

public sealed class NativeModel(WorkflowRegistry registry, NativeOptions? options = null) : IExecutionModel
{
    public const string ModelKey = "native";
    public string Key => ModelKey;
    public Type StateType => typeof(NativeState);

    /// <summary>Round 3's state versions: native's state has one shape so far.</summary>
    public int StateVersion => 1;
    public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException($"native state has no version {fromVersion}");

    internal WorkflowRegistry Registry => registry;
    internal NativeOptions Options { get; } = options ?? new NativeOptions();

    public static StartSpec Spec(string workflow, string mode = Modes.Balanced, JsonObject? input = null, string? triggerNode = null, JsonNode? trigger = null) =>
        new(ModelKey, workflow, Json.Write(new NativeStart(mode, input, triggerNode, trigger)), Ephemeral: mode == Modes.Ephemeral);

    public object CreateState(StartSpec start) => new NativeState { Mode = Json.Read<NativeStart>(start.Input ?? "{}").Mode };

    public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken) => new Burst(this, turn, cancellationToken).RunAsync();

    /// <summary>A Background leaf, run as a background job: its inputs were materialized in the turn that started it.</summary>
    public async ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken)
    {
        if (Options.Gate is { } gate) await gate(job, cancellationToken);
        var args = Json.Read<BackgroundArgs>(job.Arguments!);
        var node = registry.Get(args.Workflow).Nodes[args.Node];
        var activity = (ILeaf)Hydrator.Create(node, args.Inputs);
        var t = await activity.ExecuteAsync(new ActivityContext(job.Instance, args.Frame, node.Id, 0, cancellationToken));
        return new BackgroundResult(t.Kind, t.Result, t.Outcome, t.Fault);
    }
}

/// <summary>One turn: take the inbox, then run the scheduled work until everything waits, ends or meets a boundary.</summary>
internal sealed class Burst(NativeModel model, ITurn turn, CancellationToken cancellationToken)
{
    private readonly NativeState _s = (NativeState)turn.State;
    private readonly Workflow _wf = model.Registry.Get(turn.Instance.Definition);
    private InstanceStatus _status = InstanceStatus.Running;
    private int _executed;

    private bool Has(NativeFlaw flaw) => (model.Options.Flaws & flaw) != 0;
    internal bool Flawed(NativeFlaw flaw) => Has(flaw);
    private bool Durable => _s.Mode == Modes.Durable;
    private string NewId(string prefix) => prefix + ++_s.Next;

    public async ValueTask RunAsync()
    {
        foreach (var message in turn.Inbox) Accept(message);
        await DrainAsync();
    }

    // ---- messages in -------------------------------------------------------------------------------------------------

    private void Accept(Message m)
    {
        switch (m.Kind)
        {
            case Kinds.Start when !_s.Started:
            {
                _s.Started = true;
                var start = Json.Read<NativeStart>(m.Start?.Input ?? "{}");
                foreach (var (name, value) in _wf.Variables) _s.Variables[name] = value?.DeepClone();
                if (start.Input is { } input) _s.Variables["input"] = input.DeepClone();
                (_s.TriggerNode, _s.Trigger) = (start.TriggerNode, start.Trigger ?? turn.Ambient as JsonNode);
                _s.Agenda.Add(new Work { Frame = NewId("f"), Node = _wf.Root.Id });
                break;
            }
            case NativeKinds.Timer:
                Resume(m.Read<TimerFired>().Wait, null);
                break;
            case Kinds.Event:
                // Friction F-N3, fixed in the merge: the kernel says which wait a delivery is for.
                if (Events.Delivered(m) is { } delivered) Resume(delivered.Wait, Values.Parse(m.Payload));
                break;
            case NativeKinds.Resume:
            {
                var stimulus = m.Read<ResumeStimulus>();
                if (_s.Waits.FirstOrDefault(w => w.Value.Key == "event:" + stimulus.Event).Key is { } wait) Resume(wait, stimulus.Payload);
                break;
            }
            case Kinds.ChildFinished:
            {
                var finished = m.Read<ChildFinished>();
                var payload = new JsonObject { ["Status"] = finished.Status.ToString(), ["Output"] = finished.Output is null ? null : JsonNode.Parse(finished.Output), ["Child"] = finished.Child };
                foreach (var (id, wait) in _s.Waits.Where(w => w.Value.Kind == "child" && (w.Value.Key == finished.Child || Has(NativeFlaw.ResumeAnyChild))).ToList())
                    Resume(id, payload.DeepClone());
                break;
            }
            case Kinds.JobFinished:
            {
                var finished = m.Read<JobFinished>();
                Resume(finished.Key, Values.From(finished));
                break;
            }
            case NativeKinds.Cancel:
                CancelWorkflow();
                break;
        }
    }

    // A wait is removed when its resume is processed, so a second delivery for it (two events, a stale timer) does nothing.
    private void Resume(string wait, JsonNode? payload)
    {
        if (_s.Waits.TryGetValue(wait, out var entry)) _s.Agenda.Add(new Work { Resume = true, Frame = entry.Frame, Wait = wait, Payload = payload });
    }

    // ---- the burst ---------------------------------------------------------------------------------------------------

    private async ValueTask DrainAsync()
    {
        _s.Agenda.RemoveAll(w => w.Gone);      // only under NativeFlaw.InFlightInMemory: memory forgets now what the row still has
        while (_s.Agenda.Exists(w => !w.Gone) && _status == InstanceStatus.Running)
        {
            // Durable: a boundary after every activity. Balanced and Ephemeral: one turn until all waits or ends (capped).
            if ((Durable && _executed > 0) || _executed >= model.Options.MaxActivitiesPerTurn)
            {
                turn.Send(turn.Instance.Ref, NativeKinds.Continue);
                return;
            }
            var work = _s.Agenda.First(w => !w.Gone);
            if (!work.Resume && Durable && _wf.Nodes[work.Node!].External && work.Attempt == 0 && !Has(NativeFlaw.NoAttemptCommit))
            {
                // Not replay-safe: commit the attempt first, run it in the next turn. After a crash the row says it may have run.
                work.Attempt = 1;
                turn.Record($"a:{work.Frame}:{work.Attempt}");
                turn.Send(turn.Instance.Ref, NativeKinds.Continue);
                return;
            }
            if (Has(NativeFlaw.InFlightInMemory) && Durable) work.Gone = true;     // MUTATION: dequeued in memory only; the commit still carries it
            else _s.Agenda.Remove(work);
            _executed++;
            if (work.Resume) await ResumeAsync(work);
            else await ExecuteAsync(work);
        }
    }

    private async ValueTask ExecuteAsync(Work work)
    {
        if (work.Parent is not null && !_s.Frames.ContainsKey(work.Parent)) return;     // its parent was cancelled meanwhile
        var node = _wf.Nodes[work.Node!];
        var frame = new Frame { Id = work.Frame, Node = node.Id, Parent = work.Parent, Tag = work.Tag, Scope = work.Scope, Attempt = work.Attempt, LiveScope = work.LiveScope };
        var scope = new FrameScope(this, frame);
        if (node.Background)
        {
            // The slow activity is a background job: its intent rides in this commit, its result comes back as a message.
            _s.Frames[frame.Id] = frame;
            var wait = NewId("w");
            _s.Waits[wait] = new WaitEntry(frame.Id, "job", wait);
            turn.StartJob(wait, new BackgroundArgs(_wf.Id, node.Id, frame.Id, Hydrator.Snapshot(node, b => Evaluate(b, scope))));
            return;
        }
        var activity = Hydrator.Create(node, b => Evaluate(b, scope));
        switch (activity)
        {
            case IIntrinsic set:
                Assign(frame, set.Name, set.Value);
                Complete(frame, "Done", null);
                break;
            case CompositeActivity composite:
                _s.Frames[frame.Id] = frame;
                composite.Execute(new CompositeContext(this, frame, node));
                break;
            case IStateful stateful:
            {
                var delivery = _s.TriggerNode == node.Id && _s.Trigger is not null;
                var trigger = delivery ? _s.Trigger : null;
                if (delivery) (_s.TriggerNode, _s.Trigger) = (null, null);
                Apply(frame, await stateful.StartAsync(Context(frame), delivery, trigger));
                break;
            }
            case ILeaf leaf:
                if (node.External)
                {
                    // Friction F-N6, fixed in the merge: the kernel says when the turn may have run before, so the
                    // journal can say "this side effect may have happened already" instead of nobody knowing.
                    var again = turn.Inbox.Any(turn.Redelivered);
                    if (again && Durable) turn.Record($"m:{frame.Id}:{frame.Attempt}");
                    model.Options.OnExternal?.Invoke(turn.Instance.Ref, frame.Id, node.Id, frame.Attempt, again);
                }
                Apply(frame, await leaf.ExecuteAsync(Context(frame)));
                break;
        }
    }

    private async ValueTask ResumeAsync(Work work)
    {
        if (!_s.Waits.Remove(work.Wait!, out var wait) || !_s.Frames.TryGetValue(wait.Frame, out var frame)) return;
        Unregister(work.Wait!, wait, cancel: false);
        foreach (var (id, other) in _s.Waits.Where(w => w.Value.Frame == frame.Id).ToList())     // an activity resumes once
        {
            _s.Waits.Remove(id);
            Unregister(id, other, cancel: true);
        }
        if (wait.Kind == "child") turn.Record($"r:{work.Wait}:{wait.Key}:{work.Payload?["Child"]}");
        _s.Frames.Remove(frame.Id);
        var node = _wf.Nodes[frame.Node];
        if (node.Background)
        {
            var finished = Values.To<JobFinished>(work.Payload)!;
            if (finished.Error is { } error) Fail(frame, new ActivityFault("job", error));
            else if (Json.Read<BackgroundResult>(finished.Result!) is var r) Apply(frame, new Transition(r.Kind, r.Result, r.Outcome, r.Fault));
            return;
        }
        var activity = (IStateful)Hydrator.Create(node, b => Evaluate(b, new FrameScope(this, frame)));
        Apply(frame, await activity.ResumeAsync(Context(frame), frame.State, work.Payload));
    }

    private ActivityContext Context(Frame frame) => new(turn.Instance.Ref, frame.Id, frame.Node, frame.Attempt, cancellationToken);

    // ---- transitions -------------------------------------------------------------------------------------------------

    private void Apply(Frame frame, Transition t)
    {
        foreach (var effect in t.Effects ?? []) Do(frame, effect);
        switch (t.Kind)
        {
            case TransitionKind.Complete: Complete(frame, t.Outcome, t.Result); break;
            case TransitionKind.Cancel: Complete(frame, "Cancelled", null); break;
            case TransitionKind.Fault: Fail(frame, t.Fault!); break;
            case TransitionKind.Suspend:
                frame.State = t.State;
                _s.Frames[frame.Id] = frame;
                foreach (var trigger in t.Triggers ?? []) Register(frame, trigger);
                break;
        }
    }

    // Every leak of the activity map ends here: each trigger and effect is one kernel call, made by the model.
    private void Register(Frame frame, Trigger trigger)
    {
        var id = NewId("w");
        switch (trigger)
        {
            case EventTrigger e:
                _s.Waits[id] = new WaitEntry(frame.Id, "event", "event:" + e.Name);
                turn.Subscribe("event:" + e.Name, id);
                break;
            case HttpTrigger h:
                _s.Waits[id] = new WaitEntry(frame.Id, "event", HttpRoutes.Key(h.Method, h.Path));
                turn.Subscribe(HttpRoutes.Key(h.Method, h.Path), id);
                break;
            case TimerTrigger t:
                // The key is the timer message's id, so that a cancel can take it back (friction F-N4, fixed in the merge).
                _s.Waits[id] = new WaitEntry(frame.Id, "timer", turn.Send(turn.Instance.Ref, NativeKinds.Timer, new TimerFired(id), new SendOptions(Delay: TimeSpan.FromMilliseconds(t.Milliseconds))));
                break;
            case ChildTrigger c:
                var child = turn.Start(NativeModel.Spec(c.Workflow, c.Mode, c.Input as JsonObject));
                _s.Waits[id] = new WaitEntry(frame.Id, "child", child.Id);
                turn.Record($"s:{frame.Id}:{child.Id}");
                break;
        }
    }

    private void Unregister(string id, WaitEntry wait, bool cancel)
    {
        switch (wait.Kind)
        {
            case "event": turn.Unsubscribe(wait.Key, id); break;
            case "job" when cancel: turn.CancelJob(wait.Key); break;
            case "child" when cancel: turn.Send(new InstanceRef(wait.Key), NativeKinds.Cancel); break;
            case "timer" when cancel: turn.Recall(wait.Key); break;      // its row goes with this commit
        }
    }

    private void Do(Frame frame, Effect effect)
    {
        switch (effect)
        {
            case PublishEffect p:
                turn.Publish("event:" + p.Event, p.Payload?.ToJsonString(), model.Options.EventWindow);
                break;
            case DispatchEffect d:
            {
                // A detached start (friction F-N5, fixed in the merge): no parent, and minted here, so no read.
                var child = turn.Start(NativeModel.Spec(d.Workflow, d.Mode, d.Input as JsonObject), detached: true);
                turn.Record($"d:{frame.Id}:{child.Id}");
                break;
            }
            case EmitEffect e:
                // The kernel's live output: it knows who waits (the reply addresses of the messages this instance
                // consumed), releases it after this turn's commit, and a request takes only its first response.
                turn.Emit(e.Channel, e.Item);
                break;
        }
    }

    internal void Complete(Frame frame, string outcome, JsonNode? result)
    {
        _s.Frames.Remove(frame.Id);
        CancelChildren(frame.Id);
        if (result is not null) _s.Results[frame.Node] = result;
        foreach (var (variable, path) in _wf.Nodes[frame.Node].Captures) Assign(frame, variable, Values.Path(result, path)?.DeepClone());
        turn.Record($"c:{frame.Id}:{frame.Node}");
        NotifyParent(frame, outcome, result);
    }

    private void NotifyParent(Frame frame, string outcome, JsonNode? result)
    {
        if (frame.Parent is null)
        {
            Finish();
            return;
        }
        if (!_s.Frames.TryGetValue(frame.Parent, out var parent)) return;
        var node = _wf.Nodes[parent.Node];
        var composite = (CompositeActivity)Hydrator.Create(node, b => Evaluate(b, new FrameScope(this, parent)));
        composite.OnChildCompleted(new CompositeContext(this, parent, node), new ChildCompleted(frame.Id, frame.Node, outcome, result, frame.Tag));
    }

    /// <summary>A threshold join fired: record it once, complete the composite, cancel what still runs under it.</summary>
    internal void Join(Frame frame)
    {
        turn.Record($"j:{frame.Id}");
        if (!Has(NativeFlaw.JoinWithoutGuard))
        {
            Complete(frame, "Done", null);
            return;
        }
        turn.Record($"c:{frame.Id}:{frame.Node}");     // MUTATION: the parent hears of it, the frame and its branches live on
        NotifyParent(frame, "Done", null);
    }

    internal void RecordJoin(Frame frame, string node) => turn.Record($"j:{frame.Id}:{node}");

    internal void Fail(Frame frame, ActivityFault fault)
    {
        _s.Frames.Remove(frame.Id);
        CancelChildren(frame.Id);
        turn.Record($"f:{frame.Id}:{frame.Node}");
        if (frame.Parent is null || !_s.Frames.TryGetValue(frame.Parent, out var parent))
        {
            if (frame.Parent is null) End(InstanceStatus.Faulted, fault.Code, Values.From(fault));
            return;
        }
        var node = _wf.Nodes[parent.Node];
        var composite = (CompositeActivity)Hydrator.Create(node, b => Evaluate(b, new FrameScope(this, parent)));
        composite.OnChildFaulted(new CompositeContext(this, parent, node), new ChildFaulted(frame.Id, frame.Node, fault, frame.Tag));
    }

    internal string Schedule(Frame parent, Node child, string? tag, IReadOnlyDictionary<string, JsonNode?>? scope)
    {
        var work = new Work { Frame = NewId("f"), Node = child.Id, Parent = parent.Id, Tag = tag };
        if (scope is not null)
        {
            if (Has(NativeFlaw.ScopeInMemory)) work.LiveScope = new(scope);     // MUTATION: the iteration scope is not in the row
            else work.Scope = new(scope);
        }
        _s.Agenda.Add(work);
        return work.Frame;
    }

    internal IReadOnlyList<string> Running(string frame) =>
        [.. _s.Frames.Values.Where(f => f.Parent == frame).Select(f => f.Id), .. _s.Agenda.Where(w => !w.Resume && !w.Gone && w.Parent == frame).Select(w => w.Frame)];

    internal void Cancel(string frame)
    {
        if (_s.Frames.Remove(frame, out var f))
        {
            foreach (var (id, wait) in _s.Waits.Where(w => w.Value.Frame == frame).ToList())
            {
                _s.Waits.Remove(id);
                Unregister(id, wait, cancel: true);
            }
            CancelChildren(frame);
            turn.Record($"x:{f.Id}:{f.Node}");
        }
        _s.Agenda.RemoveAll(w => !w.Resume && w.Frame == frame);
    }

    private void CancelChildren(string frame)
    {
        foreach (var child in _s.Frames.Values.Where(f => f.Parent == frame).Select(f => f.Id).ToList()) Cancel(child);
        _s.Agenda.RemoveAll(w => !w.Resume && w.Parent == frame);
    }

    private void CancelWorkflow()
    {
        foreach (var frame in _s.Frames.Values.Where(f => f.Parent is null).Select(f => f.Id).ToList()) Cancel(frame);
        End(InstanceStatus.Cancelled, "cancelled", null);
    }

    private void Finish() => End(InstanceStatus.Finished, null, _s.Variables.GetValueOrDefault("output"));

    private void End(InstanceStatus status, string? subStatus, JsonNode? output)
    {
        foreach (var frame in _s.Frames.Keys.ToList()) Cancel(frame);
        _s.Agenda.Clear();
        _status = status;
        turn.SetStatus(status, subStatus, output);
    }

    // ---- variables and bindings --------------------------------------------------------------------------------------

    internal JsonNode? Evaluate(Binding binding, IScope scope) => binding switch
    {
        LiteralBinding l => l.Value?.DeepClone(),
        VariableBinding v => scope.Variable(v.Name)?.DeepClone(),
        ResultBinding r => Values.Path(_s.Results.GetValueOrDefault(r.Node), r.Path)?.DeepClone(),
        ExpressionBinding e => Expression.Evaluate(e.Text, scope),
        _ => throw new InvalidOperationException(binding.ToString()),
    };

    private Frame? ParentOf(Frame frame) => frame.Parent is { } id ? _s.Frames.GetValueOrDefault(id) : null;

    internal JsonNode? Read(Frame? frame, string name)
    {
        for (; frame is not null; frame = ParentOf(frame))
        {
            if (frame.LiveScope is { } live && live.TryGetValue(name, out var l)) return l;
            if (frame.Scope is { } scope && scope.TryGetValue(name, out var v)) return v;
        }
        return _s.Variables.GetValueOrDefault(name);
    }

    internal JsonNode? Result(string node) => _s.Results.GetValueOrDefault(node);

    /// <summary>A write goes to the nearest scope that declares the variable, else to the workflow's.</summary>
    private void Assign(Frame frame, string name, JsonNode? value)
    {
        for (var f = (Frame?)frame; f is not null; f = ParentOf(f))
        {
            if (f.LiveScope?.ContainsKey(name) == true) { f.LiveScope[name] = value; return; }
            if (f.Scope?.ContainsKey(name) == true) { f.Scope[name] = value; return; }
        }
        _s.Variables[name] = value;
    }
}

internal sealed class FrameScope(Burst burst, Frame frame) : IScope
{
    public JsonNode? Variable(string name) => burst.Read(frame, name);
    public JsonNode? Result(string node) => burst.Result(node);
}

/// <summary>What a composite sees: its children, its private state, and the structural requests it may make.</summary>
public sealed class CompositeContext
{
    private readonly Burst _burst;
    private readonly Frame _frame;

    internal CompositeContext(Burst burst, Frame frame, Node node) => (_burst, _frame, Node) = (burst, frame, node);

    public Node Node { get; }
    public string FrameId => _frame.Id;
    public IReadOnlyList<Node> Children => Node.Children;
    public IReadOnlyList<string> Running => _burst.Running(_frame.Id);
    internal bool Flawed(NativeFlaw flaw) => _burst.Flawed(flaw);

    public T GetState<T>() where T : new() => Values.To<T>(_frame.State) ?? new T();
    public void SetState<T>(T state) => _frame.State = Values.From(state);

    public string Schedule(Node child, string? tag = null, IReadOnlyDictionary<string, JsonNode?>? scope = null) => _burst.Schedule(_frame, child, tag, scope);
    public void Complete(string outcome = "Done", JsonNode? result = null) => _burst.Complete(_frame, outcome, result);
    public void Join() => _burst.Join(_frame);
    public void RecordJoin(string node) => _burst.RecordJoin(_frame, node);
    public void Fault(ActivityFault fault) => _burst.Fail(_frame, fault);
    public void Cancel(string childFrame) => _burst.Cancel(childFrame);
}

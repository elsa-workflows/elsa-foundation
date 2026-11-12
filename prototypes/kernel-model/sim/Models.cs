namespace Kernel.Sim;

// Toy execution models. Each keeps, in its state, what it would take to see a violation afterwards.

public abstract class Model<TState> : IExecutionModel where TState : class, new()
{
    public abstract string Key { get; }
    public Type StateType => typeof(TState);
    public virtual int StateVersion => 1;
    public virtual object UpgradeState(string state, int fromVersion) => Json.Read<TState>(state);
    public virtual object CreateState(StartSpec start) => new TState();
    public virtual ValueTask<object?> RunAsync(IRun run, CancellationToken cancellationToken) => new((object?)null);
    protected abstract void Step(IStep step, TState state);

    public ValueTask StepAsync(IStep step, CancellationToken cancellationToken)
    {
        Step(step, (TState)step.State);
        return default;
    }
}

// ---- a counter that records every message it applied: a duplicate, a gap or a wrong order is a violation ------------

public sealed class CounterState
{
    public List<string> Ids { get; set; } = [];
    public List<string> Tokens { get; set; } = [];
    public List<Forward> Forwarded { get; set; } = [];
    public List<string> Held { get; set; } = [];        // messages held once, which it applies when they come again
    public int Count { get; set; }                      // version 2 only: how many tokens it applied
    public string? Bound { get; set; }                  // the definition it last rebound to
    public List<string> Wrong { get; set; } = [];       // steps that saw another definition than the one it was bound to
}

public sealed record Forward(string Target, string Token);

public sealed record Add(string Token, string? ForwardTo = null, bool Hold = false);

/// <summary>Version 1 keeps tokens; version 2 also keeps their count, and upgrades a version-1 state by counting them.</summary>
public sealed class Counter(int version = 2) : Model<CounterState>
{
    public static readonly StartSpec Spec = new("counter", "c");
    public override string Key => "counter";
    public override int StateVersion => version;

    public override object UpgradeState(string state, int fromVersion)
    {
        var old = Json.Read<CounterState>(state);
        old.Count = old.Tokens.Count;
        return old;
    }

    protected override void Step(IStep step, CounterState state)
    {
        var holding = false;
        if (step.Instance.Definition != (state.Bound ?? Spec.Definition)) state.Wrong.Add($"saw {step.Instance.Definition}, bound to {state.Bound}");
        foreach (var message in step.Inbox)
        {
            if (message.Kind == "rebind")      // to another version of its definition, from the next step on
            {
                state.Bound = message.Read<string>();
                step.Rebind(state.Bound);
            }
            if (message.Kind != "add") continue;
            var add = message.Read<Add>();
            // A message that asks to be held is held once, with everything after it, so the order stays; a nudge to self
            // makes the next step come.
            if (!holding && add.Hold && !state.Held.Contains(message.Id))
            {
                holding = true;
                state.Held.Add(message.Id);
                step.Send(step.Instance.Ref, "nudge");
            }
            if (holding)
            {
                step.Hold(message);
                continue;
            }
            state.Ids.Add(message.Id);
            state.Tokens.Add(add.Token);
            if (version >= 2) state.Count++;
            step.Record(add.Token);
            if (add.ForwardTo is not { } target) continue;
            var forward = new Forward(target, add.Token + ">");
            state.Forwarded.Add(forward);
            step.Send(new InstanceRef(target), "add", new Add(forward.Token), new SendOptions(Start: Spec));
        }
    }
}

// ---- a parent that starts N children and waits for all of them; a child that finishes after a timer to itself ---------

public sealed class ParentState
{
    public bool Started { get; set; }
    public bool Cancelling { get; set; }
    public List<string> Pending { get; set; } = [];
    public List<string> Done { get; set; } = [];
    public List<string> Known { get; set; } = [];       // children that reported that they started
}

/// <summary>A delay below zero: the children wait until they are cancelled. Spread: every other child in another partition.</summary>
public sealed record ParentInput(int Children, int DelayMs, bool Spread = false);

/// <summary>
/// Starts its children and waits for them. A cancel goes to every child it started, by its own list, so a child whose
/// start is still on its way gets the cancel after the start (one sender, one target, in order).
/// </summary>
public sealed class Parent(bool knownOnly = false) : Model<ParentState>
{
    public override string Key => "parent";

    protected override void Step(IStep step, ParentState state)
    {
        foreach (var message in step.Inbox)
        {
            if (message.Kind == Kinds.Start && !state.Started && message.Start is { Input: { } json })
            {
                state.Started = true;
                var input = Json.Read<ParentInput>(json);
                for (var i = 0; i < input.Children; i++)
                    state.Pending.Add(step.Start(new StartSpec("child", "c", Json.Write(input.DelayMs)), input.Spread && i % 2 == 0 ? Placement.Spread : Placement.WithParent).Id);
            }
            else if (message.Kind == Kinds.ChildFinished)
            {
                var child = message.Read<ChildFinished>().Child;
                state.Done.Add(state.Pending.Remove(child) ? child : "!unexpected " + child);
            }
            else if (message.Kind == "started") state.Known.Add(message.Sender!);
            else if (message.Kind == "cancel" && state.Started && !state.Cancelling)
            {
                state.Cancelling = true;
                foreach (var child in state.Pending.Where(c => !knownOnly || state.Known.Contains(c))) step.Send(new InstanceRef(child), "cancel");
            }
        }
        if (state.Started && state.Pending.Count == 0) step.SetStatus(state.Cancelling ? InstanceStatus.Cancelled : InstanceStatus.Finished);
    }
}

public sealed class ChildState
{
    public int DelayMs { get; set; }
    public bool Armed { get; set; }
    public int Fired { get; set; }
}

public sealed class Child : Model<ChildState>
{
    public override string Key => "child";
    public override object CreateState(StartSpec start) => new ChildState { DelayMs = Json.Read<int>(start.Input!) };

    protected override void Step(IStep step, ChildState state)
    {
        foreach (var message in step.Inbox)
        {
            if (message.Kind == Kinds.Start && !state.Armed)
            {
                state.Armed = true;
                step.Send(new InstanceRef(step.Instance.Parent!), "started");
                if (state.DelayMs >= 0) step.Send(step.Instance.Ref, "timer", null, new SendOptions(Delay: TimeSpan.FromMilliseconds(state.DelayMs)));
            }
            else if (message.Kind == "cancel") step.SetStatus(InstanceStatus.Cancelled);
            else if (message.Kind == "timer")
            {
                state.Fired++;
                step.SetStatus(InstanceStatus.Finished, output: state.Fired);
            }
        }
    }
}

// ---- a waiter that subscribes to event keys and finishes when the first arrives ---------------------------------------

public sealed class WaiterState
{
    public bool Started { get; set; }
    public List<string> Keys { get; set; } = [];
    public List<string> Waits { get; set; } = [];
    public List<string> Got { get; set; } = [];
}

public sealed class Waiter : Model<WaiterState>
{
    public override string Key => "waiter";
    public override object CreateState(StartSpec start) => new WaiterState { Keys = Json.Read<List<string>>(start.Input!) };

    protected override void Step(IStep step, WaiterState state)
    {
        foreach (var message in step.Inbox)
        {
            if (message.Kind == Kinds.Start && !state.Started)
            {
                state.Started = true;
                foreach (var key in state.Keys) step.Subscribe(key, "w");
                state.Waits.AddRange(state.Keys);
            }
            else if (message.Kind == Kinds.Event && state.Waits.Count > 0)
            {
                state.Got.Add(message.Id);
                foreach (var key in state.Waits) step.Unsubscribe(key, "w");
                state.Waits.Clear();
                step.SetStatus(InstanceStatus.Finished);
            }
        }
    }
}

// ---- a sequence of N no-op activities: ephemeral, balanced (one commit) or durable (a step per activity) --------------

public sealed class SequenceState
{
    public int Count { get; set; }
    public string Mode { get; set; } = "";
    public bool Began { get; set; }
    public List<int> Done { get; set; } = [];
}

public sealed record SequenceInput(int Count, string Mode);

public sealed class Sequence : Model<SequenceState>
{
    public const string Ephemeral = "ephemeral", Balanced = "balanced", Durable = "durable";
    public override string Key => "sequence";

    public static StartSpec Spec(int count, string mode) => new("sequence", "s", Json.Write(new SequenceInput(count, mode)), Ephemeral: mode == Ephemeral);

    public override object CreateState(StartSpec start)
    {
        var input = Json.Read<SequenceInput>(start.Input!);
        return new SequenceState { Count = input.Count, Mode = input.Mode };
    }

    protected override void Step(IStep step, SequenceState state)
    {
        foreach (var message in step.Inbox)
        {
            if (message.Kind == Kinds.Start ? state.Began : message.Kind != "continue") continue;
            state.Began = true;
            do
            {
                state.Done.Add(state.Done.Count + 1);
                step.Record("activity " + state.Done.Count);
            } while (state.Mode != Durable && state.Done.Count < state.Count);
            // A durable boundary is the end of a step: the model sends itself a message and stops.
            if (state.Done.Count < state.Count) step.Send(step.Instance.Ref, "continue");
            else step.SetStatus(InstanceStatus.Finished);
        }
    }
}

// ---- off-step work: start it, cancel it, take its result ----------------------------------------------------------------

public sealed class RunnerState
{
    public List<string> Log { get; set; } = [];
}

public sealed class Runner(Func<IRun, CancellationToken, ValueTask<object?>> work) : Model<RunnerState>
{
    public static readonly StartSpec Spec = new("runner", "r");
    public override string Key => "runner";
    public override ValueTask<object?> RunAsync(IRun run, CancellationToken cancellationToken) => work(run, cancellationToken);

    protected override void Step(IStep step, RunnerState state)
    {
        foreach (var message in step.Inbox)
        {
            if (message.Kind == Kinds.RunFinished)
            {
                state.Log.Add("result " + message.Read<RunFinished>().Key);     // no guard: the kernel must not deliver it twice or after a cancel
                continue;
            }
            var key = message.Read<string>();
            if (message.Kind == "go" && !state.Log.Contains("start " + key))
            {
                step.Run(key, key);
                state.Log.Add("start " + key);
            }
            else if (message.Kind == "cancel" && state.Log.Contains("start " + key) && !state.Log.Contains("result " + key) && !state.Log.Contains("cancel " + key))
            {
                step.CancelRun(key);
                state.Log.Add("cancel " + key);
            }
        }
    }
}

// ---- an instance that goes wrong: its step throws, its row is refused, its step or its off-step work kills the process ----

public sealed class FragileState
{
    public int Hits { get; set; }
    public string Note { get; set; } = "";
}

public sealed class Fragile(Action die) : Model<FragileState>
{
    public const string Sour = "SOUR";
    public static readonly StartSpec Spec = new("fragile", "f");
    public override string Key => "fragile";

    public override ValueTask<object?> RunAsync(IRun run, CancellationToken cancellationToken)
    {
        die();      // off-step work that kills the process as soon as it starts
        return new ValueTask<object?>((object?)null);
    }

    protected override void Step(IStep step, FragileState state)
    {
        foreach (var message in step.Inbox)
        {
            state.Hits++;
            if (message.Kind == "dierun") step.Run(message.Id);
            if (message.Kind == "throw") throw new InvalidOperationException("the step threw");
            if (message.Kind == "spoil") state.Note = Sour;     // the store refuses a row that carries this
            if (message.Kind == "die") die();
        }
    }
}

// ---- a workflow that handles an HTTP request and writes the response itself -------------------------------------------

public sealed class HttpState
{
    public string Mode { get; set; } = "";
    public bool Started { get; set; }
    public int Steps { get; set; }
    public List<string> Answers { get; set; } = [];
    public List<string> Ambient { get; set; } = [];     // which process saw the starter's ambient, in which step
}

public sealed record HttpInput(string Mode);

/// <summary>
/// respond: answers and finishes in its first step. early: answers, then goes on for a step. timer, run: waits for a
/// short timer or off-step work, then answers. wait: waits for a resume, which may come from another node with a reply
/// address, then for a short timer, then answers. Every answer names the actor that ran the step, so an answer from a step that never committed shows.
/// </summary>
public sealed class Http(int actor, Func<IRun, CancellationToken, ValueTask<object?>> work) : Model<HttpState>
{
    public const string Respond = "respond", Early = "early", Timer = "timer", Work = "run", Wait = "wait";
    public static readonly string[] Starts = [Respond, Early, Timer, Work];
    public override string Key => "http";

    public static StartSpec Spec(string mode, bool ephemeral = false) => new("http", "h", Json.Write(new HttpInput(mode)), Ephemeral: ephemeral);
    public override object CreateState(StartSpec start) => new HttpState { Mode = Json.Read<HttpInput>(start.Input!).Mode };
    public override ValueTask<object?> RunAsync(IRun run, CancellationToken cancellationToken) => work(run, cancellationToken);

    protected override void Step(IStep step, HttpState state)
    {
        state.Steps++;
        if (step.Ambient is { } ambient) state.Ambient.Add(Equals(ambient, "amb:" + step.Instance.Ref.Id) ? $"seen@{actor}" : $"wrong {ambient}@{actor}");
        foreach (var message in step.Inbox)
            switch (message.Kind)
            {
                case Kinds.Start when !state.Started:
                    state.Started = true;
                    if (state.Mode is Respond or Early) Answer(step, state);
                    if (state.Mode == Respond) step.SetStatus(InstanceStatus.Finished);
                    if (state.Mode == Early) step.Send(step.Instance.Ref, "continue");     // the response is out; the workflow goes on
                    if (state.Mode == Timer) step.Send(step.Instance.Ref, "timer", null, new SendOptions(Delay: TimeSpan.FromMilliseconds(300)));
                    if (state.Mode == Work) step.Run("work");
                    break;
                case "resume" when state.Mode == Wait:      // resumed, perhaps by a request on another node: a short wait, then the answer
                    step.Send(step.Instance.Ref, "timer", null, new SendOptions(Delay: TimeSpan.FromMilliseconds(300)));
                    break;
                case "continue" or "timer" or Kinds.RunFinished:
                    if (state.Mode != Early) Answer(step, state);
                    step.SetStatus(InstanceStatus.Finished);
                    break;
                case "cancel":
                    step.SetStatus(InstanceStatus.Cancelled);
                    break;
            }
    }

    private void Answer(IStep step, HttpState state)
    {
        var answer = $"{step.Instance.Ref}:{state.Steps}@{actor}";
        state.Answers.Add(answer);
        step.Record(answer);
        step.Emit(Request.Response, answer);
    }
}

// ---- a tree of instances that never ends by itself: only a terminate ends it ---------------------------------------------

public sealed record TreeInput(int Depth, int Fanout);

public sealed class TreeState
{
    public int Depth { get; set; }
    public int Fanout { get; set; }
    public bool Started { get; set; }
    public List<string> Children { get; set; } = [];
    public List<string> Log { get; set; } = [];
}

/// <summary>Inner nodes start children, every other one in another partition; a leaf holds off-step work and a wait.</summary>
public sealed class Tree(Func<IRun, CancellationToken, ValueTask<object?>> work) : Model<TreeState>
{
    public override string Key => "tree";
    public static StartSpec Spec(int depth, int fanout) => new("tree", "t", Json.Write(new TreeInput(depth, fanout)));

    public override object CreateState(StartSpec start) => Json.Read<TreeInput>(start.Input!) is var input ? new TreeState { Depth = input.Depth, Fanout = input.Fanout } : null!;
    public override ValueTask<object?> RunAsync(IRun run, CancellationToken cancellationToken) => work(run, cancellationToken);

    protected override void Step(IStep step, TreeState state)
    {
        foreach (var message in step.Inbox)
        {
            if (message.Kind == Kinds.Start && !state.Started)
            {
                state.Started = true;
                for (var i = 0; i < state.Fanout && state.Depth > 0; i++)
                    state.Children.Add(step.Start(Spec(state.Depth - 1, state.Fanout), i % 2 == 0 ? Placement.Spread : Placement.WithParent).Id);
                if (state.Depth > 0) continue;
                step.Run("hold");
                step.Subscribe("tk", "w");
            }
            else state.Log.Add(message.Kind);
        }
    }
}

// ---- an instance that an event starts: it takes the event and finishes ----------------------------------------------------

public sealed class OnEventState
{
    public List<string> Got { get; set; } = [];
}

public sealed class OnEvent : Model<OnEventState>
{
    public override string Key => "onevent";
    public static StartSpec Spec(string definition) => new("onevent", definition);

    protected override void Step(IStep step, OnEventState state)
    {
        foreach (var message in step.Inbox.Where(m => m.Kind == Kinds.Event)) state.Got.Add(message.Id);
        step.SetStatus(InstanceStatus.Finished);
    }
}

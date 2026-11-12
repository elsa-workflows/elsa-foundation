namespace Kernel.Sim;

// Toy execution models. Each keeps, in its state, what it would take to see a violation afterwards.

public abstract class Model<TState> : IExecutionModel where TState : class, new()
{
    public abstract string Key { get; }
    public Type StateType => typeof(TState);
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
}

public sealed record Forward(string Target, string Token);

public sealed record Add(string Token, string? ForwardTo = null);

public sealed class Counter : Model<CounterState>
{
    public static readonly StartSpec Spec = new("counter", "c");
    public override string Key => "counter";

    protected override void Step(IStep step, CounterState state)
    {
        foreach (var message in step.Inbox)
        {
            var add = message.Read<Add>();
            state.Ids.Add(message.Id);
            state.Tokens.Add(add.Token);
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
    public List<string> Pending { get; set; } = [];
    public List<string> Done { get; set; } = [];
}

public sealed record ParentInput(int Children, int DelayMs);

public sealed class Parent : Model<ParentState>
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
                    state.Pending.Add(step.Start(new StartSpec("child", "c", Json.Write(input.DelayMs))).Id);
            }
            else if (message.Kind == Kinds.ChildFinished)
            {
                var child = message.Read<ChildFinished>().Child;
                state.Done.Add(state.Pending.Remove(child) ? child : "!unexpected " + child);
            }
        }
        if (state.Started && state.Pending.Count == 0) step.SetStatus(InstanceStatus.Finished);
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
                step.Send(step.Instance.Ref, "timer", null, new SendOptions(Delay: TimeSpan.FromMilliseconds(state.DelayMs)));
            }
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

// ---- an instance that goes wrong: its step throws, its row is refused, or its step kills the process --------------------

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

    protected override void Step(IStep step, FragileState state)
    {
        foreach (var message in step.Inbox)
        {
            state.Hits++;
            if (message.Kind == "throw") throw new InvalidOperationException("the step threw");
            if (message.Kind == "spoil") state.Note = Sour;     // the store refuses a row that carries this
            if (message.Kind == "die") die();
        }
    }
}

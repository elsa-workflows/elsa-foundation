using System.Text.Json.Nodes;
using Bpmn.Model.State;
using Bpmn.Semantics;

namespace Kernel.Bpmn;

public static class BpmnKinds
{
    public const string Timer = "bpmn.timer";     // a timer's wait is over
    public const string Signal = "bpmn.signal";   // a called child signals its call activity (an escalation)
}

public sealed record TimerDue(string Handle);

public sealed record ScopeSignal(string Child, string Code, string? Payload);

/// <summary>The start spec's input: variables over the process's own.</summary>
public sealed record BpmnStart(JsonObject? Variables = null);

public sealed record JobArgs(string Process, string Service, string Element, JsonObject Variables);

/// <summary>
/// One unit of host work in flight, keyed by its handle: the interpreter's binding, iteration and token, and the
/// kernel's side of it (a job key, a timer's message id, a subscription key, a child's id).
/// </summary>
public sealed record LiveWork(string Binding, string? Iteration, string Token, string Element, string Kind, string Key);

public static class WorkKinds
{
    public const string Job = "job", Timer = "timer", Message = "message", Call = "call";
}

/// <summary>
/// The instance's state: the interpreter's token state (stored without its diagnostics, which go to the journal), the
/// variables, and the host's ledger of work in flight.
/// </summary>
public sealed class BpmnState
{
    public bool Started { get; set; }
    public long Next { get; set; }
    public BpmnExecutionState Tokens { get; set; } = new();
    public Dictionary<string, JsonNode?> Variables { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, LiveWork> Live { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The events message catches took. A kept event reaches every catch that subscribes within its window, so a catch
    /// that comes round again must skip it. It grows by one id per message taken (BPMN.md: friction F-B1).
    /// </summary>
    public HashSet<string> Taken { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The variables set since the start: what a called child gives back, so it does not overwrite its parent's with stale copies.</summary>
    public HashSet<string> Set { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Deliberate defects of the host; the simulation's --bpmn-mutations names the check that must catch each (BPMN.md, section 5).</summary>
[Flags]
public enum BpmnFlaw
{
    None = 0,
    KeepStoppedWork = 1 << 0,   // a stopped unit's job, timer, subscription or child is left running
    NoLateReport = 1 << 1,      // a stopped unit is never reported: its cancelled token keeps its stopped handle
    KeepSubscription = 1 << 2,  // a message catch that fired keeps its subscription
    StaleTokens = 1 << 3,       // every message of a turn is evaluated against the token state the turn started with
    ReuseHandles = 1 << 4,      // a handle is numbered by the ledger's size, so a new unit can take a live unit's handle
    ResurrectArrivals = 1 << 5, // the stored state keeps the arrivals a join consumed, as a careless merge of old and new tokens would
    NeverEnd = 1 << 6,          // the host misses the interpreter's Complete and leaves the instance running
    LoseChildOutput = 1 << 7,   // a call activity completes, the variables its child set do not reach the state
    KeepChild = 1 << 8,         // a stopped call activity's child is not terminated
}

public sealed class BpmnOptions
{
    public BpmnFlaw Flaws { get; init; }

    /// <summary>Gates a job before it runs (the simulation decides when it completes).</summary>
    public Func<IJob, CancellationToken, ValueTask>? Gate { get; init; }
}

/// <summary>
/// BPMN as an execution model: the <c>Bpmn.Semantics</c> interpreter, unchanged, behind the kernel's contract. A turn
/// feeds each message of its inbox to the interpreter as one event; the commands it returns become kernel operations;
/// its token state is the instance's state.
/// </summary>
public sealed class BpmnModel(ProcessRegistry registry, BpmnOptions? options = null) : IExecutionModel
{
    public const string ModelKey = "bpmn";
    private static readonly BpmnInterpreter Interpreter = BpmnInterpreter.CreateDefault();

    public string Key => ModelKey;
    public Type StateType => typeof(BpmnState);
    public int StateVersion => 1;
    public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException($"BPMN state has no version {fromVersion}");

    internal ProcessRegistry Registry => registry;
    internal BpmnOptions Options { get; } = options ?? new BpmnOptions();
    internal static BpmnInterpreter Semantics => Interpreter;

    public static StartSpec Spec(string process, JsonObject? variables = null, History history = History.Full) =>
        new(ModelKey, process, Json.Write(new BpmnStart(variables)), History: history);

    /// <summary>The event key a message catch waits for, and a publisher publishes to.</summary>
    public static string MessageKey(string name, string correlation) => $"msg:{name}:{correlation}";

    public object CreateState(StartSpec start) => new BpmnState();

    public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken)
    {
        new BpmnTurn(this, turn).Run();
        return ValueTask.CompletedTask;
    }

    /// <summary>A service task's work: the named service over the variables the turn that started it saw.</summary>
    public async ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken)
    {
        if (Options.Gate is { } gate) await gate(job, cancellationToken);
        var args = Json.Read<JobArgs>(job.Arguments!);
        return registry.GetService(args.Service)(args.Variables);
    }
}

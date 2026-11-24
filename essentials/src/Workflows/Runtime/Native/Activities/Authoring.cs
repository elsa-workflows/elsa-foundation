using System.Text.Json;
using System.Text.Json.Nodes;
using Kernel.Foundation.Contracts;

namespace Kernel.Native;

/// <summary>A bound input. Leaf inputs are materialized once per activation; composites are activated per callback.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InputAttribute : Attribute;

/// <summary>Elsa 4's background placement: the leaf runs as a background job (the kernel's <c>StartJob</c>), not inside the turn.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class BackgroundAttribute : Attribute
{
    /// <summary>The input whose time span is the job's timeout (the <c>StartJob</c> deadline), when the node sets no timeout itself.</summary>
    public string? TimeoutInput { get; init; }

    /// <summary>The job's timeout when <see cref="TimeoutInput"/> is unbound or not positive, in milliseconds; 0 for none.</summary>
    public long DefaultTimeoutMilliseconds { get; init; }

    /// <summary>The outcome the activity completes on when its job's deadline passes; without one, the deadline faults it.</summary>
    public string? TimeoutOutcome { get; init; }
}

/// <summary>How a background activity's timeout input reads: one rule for the activity and for its job's deadline.</summary>
public static class ActivityTimeouts
{
    /// <summary>The longest timeout an input may give: one day.</summary>
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromDays(1);

    /// <summary>
    /// A timeout as a number of milliseconds or a time span (<c>c</c> format, as <c>00:00:30</c>, or ISO 8601, as
    /// <c>PT30S</c>), positive and at most <paramref name="max"/>; null for an unbound one. False for anything else.
    /// </summary>
    public static bool TryParse(JsonNode? value, TimeSpan max, out TimeSpan? timeout)
    {
        timeout = null;
        if (value is null) return true;
        if (value is not JsonValue scalar) return false;
        TimeSpan span;
        if (scalar.TryGetValue<double>(out var milliseconds) && double.IsFinite(milliseconds) && milliseconds <= max.TotalMilliseconds) span = TimeSpan.FromMilliseconds(milliseconds);
        else if (scalar.TryGetValue<string>(out var text) &&
                 (TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out span) || TryIso(text, out span))) { }
        else return false;
        if (span <= TimeSpan.Zero || span > max) return false;
        timeout = span;
        return true;

        static bool TryIso(string text, out TimeSpan span)
        {
            try { span = System.Xml.XmlConvert.ToTimeSpan(text); return true; }
            catch (FormatException) { span = default; return false; }
        }
    }
}

/// <summary>Elsa 4's side-effect profile External: an attempt is not safe to replay. In Durable mode it is committed before it runs.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ExternalAttribute : Attribute;

// ---- transitions, triggers, effects --------------------------------------------------------------------------------

public enum TransitionKind { Complete, Suspend, Fault, Cancel }

public sealed record ActivityFault(string Code, string Message);

/// <summary>What a suspended activity waits for. The model turns each into a kernel registration.</summary>
public abstract record Trigger;
public sealed record EventTrigger(string Name) : Trigger;                                 // a subscription
public sealed record TimerTrigger(long Milliseconds) : Trigger;                           // a message to self with a due time
public sealed record ChildTrigger(string Workflow, JsonNode? Input, string Mode) : Trigger;  // a child start and its child-finished
public sealed record HttpTrigger(string Method, string Path, bool Resolved = false) : Trigger;                   // a subscription on the route

/// <summary>What an activity does to the world besides its result. The model turns each into a kernel call.</summary>
public abstract record Effect;
public sealed record PublishEffect(string Event, JsonNode? Payload) : Effect;             // the kernel's publish
public sealed record DispatchEffect(string Workflow, JsonNode? Input, string Mode) : Effect; // a detached start
public sealed record EmitEffect(string Channel, JsonNode? Item) : Effect;                 // live, not durable: the kernel's Emit

/// <summary>
/// An effect on the outside world that is not replay-safe, performed by the host (round 5 C). <see cref="Key"/> is the
/// effect key the activity put where the receiver looks for a repeat: an HTTP request's <c>Idempotency-Key</c> header,
/// an email's <c>Message-ID</c>. The activity derives it from <see cref="ActivityContext.EffectKey"/>; the host sends it
/// as it is. It runs at least once: whether a repeat is dropped is the receiver's business.
/// </summary>
public abstract record OutboundEffect : Effect
{
    public abstract string Key { get; }
}

public sealed record OutboundHttpRequest(string Method, string Url, JsonNode? Body, IReadOnlyDictionary<string, string> Headers);
public sealed record HttpSendEffect(OutboundHttpRequest Request) : OutboundEffect
{
    public override string Key => Request.Headers[SendHttpRequest.IdempotencyKeyHeader];
}

public sealed record OutboundEmail(string To, string Subject, string Body, string MessageId);
public sealed record EmailSendEffect(OutboundEmail Mail) : OutboundEffect
{
    public override string Key => Mail.MessageId;
}

/// <summary>The closed set of what an activation can end in. Not generic: the typed result is serialized by the helper.</summary>
public sealed record Transition(
    TransitionKind Kind, JsonNode? Result = null, string Outcome = "Done", ActivityFault? Fault = null, JsonNode? State = null,
    IReadOnlyList<Trigger>? Triggers = null, IReadOnlyList<Effect>? Effects = null)
{
    public Transition With(params Effect[] effects) => this with { Effects = [.. Effects ?? [], .. effects] };
}

public readonly record struct Unit;

// ---- contexts ------------------------------------------------------------------------------------------------------

public class ActivityContext
{
    internal ActivityContext(ActorRef instance, string frame, string node, int attempt, CancellationToken cancellationToken, string iterationPath, string effectKey)
        : this(instance, frame, node, attempt, cancellationToken, null, null, iterationPath, effectKey) { }

    internal ActivityContext(ActorRef instance, string frame, string node, int attempt, CancellationToken cancellationToken, string? operationKey, string? recoverySource, string iterationPath, string effectKey)
        => (Instance, FrameId, NodeId, Attempt, CancellationToken, OperationKey, RecoverySource, IterationPath, EffectKey) =
            (instance, frame, node, attempt, cancellationToken, operationKey ?? frame, recoverySource, iterationPath, effectKey);

    internal string OperationKey { get; }
    internal string? RecoverySource { get; }
    public ActorRef Instance { get; }
    public string FrameId { get; }
    public string NodeId { get; }
    /// <summary>The committed attempt of an External activity in Durable mode; 0 otherwise. It takes no other value.</summary>
    public int Attempt { get; }
    /// <summary>
    /// Which activation of this node this is within the instance (round 5 C): the iteration numbers of the repeating
    /// scopes that enclose it, root first, each from the enclosing activity's committed state
    /// by its execution model, plus a retry generation for a retried activity;
    /// the root path when nothing repeats it.
    /// </summary>
    public string IterationPath { get; }
    /// <summary>
    /// The stable key of this attempt's external effect (instance, node,
    /// iteration path, attempt): the same string whenever this logical activation runs, on whichever owner and however a
    /// replay orders the agenda, and a different one for any other activation of this or another node. What an activity
    /// hands a partner so that the partner can drop a repeat; the kernel itself drops nothing.
    /// </summary>
    public string EffectKey { get; }
    public CancellationToken CancellationToken { get; }

    /// <summary>The host's outbound HTTP, for an activity that runs as a background job; null where the host composes none.</summary>
    public NativeHttpOptions? Http { get; internal init; }
}

/// <summary>
/// The host's outbound HTTP for <see cref="SendHttpRequest"/>'s job: the client it sends with, the most response bytes it
/// keeps, and how it turns a Connection name into credential headers, which never sit in the job's stored arguments.
/// The client must not follow redirects or keep cookies (for a <see cref="SocketsHttpHandler"/>: <c>AllowAutoRedirect</c>
/// and <c>UseCookies</c> false): a redirect would carry a Connection's headers to a host the partner names, and a cookie
/// one workflow's partner set would go out with another workflow's request.
/// </summary>
public sealed class NativeHttpOptions
{
    /// <summary>The default for <see cref="MaxResponseBytes"/>: 256 KiB.</summary>
    public const int DefaultMaxResponseBytes = 256 * 1_024;

    public HttpMessageInvoker? Client { get; init; }

    /// <summary>A longer response body completes the activity on its ResponseTooLarge outcome, with no body.</summary>
    public int MaxResponseBytes { get; init; } = DefaultMaxResponseBytes;

    /// <summary>The credential headers for a Connection name, resolved when the job runs.</summary>
    public Func<string, CancellationToken, ValueTask<IReadOnlyDictionary<string, string>>>? Connection { get; init; }
}

public sealed class StartContext<TTrigger> : ActivityContext
{
    private readonly JsonNode? _trigger;
    internal StartContext(ActivityContext c, bool delivery, JsonNode? trigger) : base(c.Instance, c.FrameId, c.NodeId, c.Attempt, c.CancellationToken, c.OperationKey, c.RecoverySource, c.IterationPath, c.EffectKey)
        => (IsTriggerDelivery, _trigger) = (delivery, trigger);

    /// <summary>This activation is the workflow's start trigger being delivered (an HTTP request, an event).</summary>
    public bool IsTriggerDelivery { get; }

    public bool TryGetTrigger(out TTrigger trigger)
    {
        trigger = IsTriggerDelivery ? Values.To<TTrigger>(_trigger)! : default!;
        return IsTriggerDelivery;
    }
}

public sealed class ResumeContext<TState, TTrigger> : ActivityContext
{
    internal ResumeContext(ActivityContext c, TState state, TTrigger trigger) : base(c.Instance, c.FrameId, c.NodeId, c.Attempt, c.CancellationToken, c.OperationKey, c.RecoverySource, c.IterationPath, c.EffectKey)
        => (State, Trigger) = (state, trigger);

    public TState State { get; }
    public TTrigger Trigger { get; }
}

// ---- base types ----------------------------------------------------------------------------------------------------

public abstract class ActivityBase;

internal interface ILeaf
{
    ValueTask<Transition> ExecuteAsync(ActivityContext context);
}

internal interface IStateful
{
    ValueTask<Transition> StartAsync(ActivityContext context, bool delivery, JsonNode? trigger);
    ValueTask<Transition> ResumeAsync(ActivityContext context, JsonNode? state, JsonNode? trigger);
}

/// <summary>A variable write: an engine intrinsic, as in Elsa 4. The interpreter applies it; nothing executes.</summary>
internal interface IIntrinsic
{
    string Name { get; }
    JsonNode? Value { get; }
}

/// <summary>A leaf activity: one attempt, a closed transition.</summary>
public abstract class Activity<TResult> : ActivityBase, ILeaf
{
    protected abstract ValueTask<Transition> ExecuteAsync(ActivityContext context);
    ValueTask<Transition> ILeaf.ExecuteAsync(ActivityContext context) => ExecuteAsync(context);

    protected static Transition Complete(TResult result, string outcome = "Done") => new(TransitionKind.Complete, Values.From(result), outcome);
    protected static Transition Fault(string code, string message) => new(TransitionKind.Fault, Fault: new ActivityFault(code, message));
    protected static Transition Cancel(string reason) => new(TransitionKind.Cancel, Outcome: "Cancelled", Fault: new ActivityFault("cancelled", reason));
}

public abstract class Activity : Activity<Unit>
{
    protected static Transition Complete(string outcome = "Done") => new(TransitionKind.Complete, null, outcome);
}

/// <summary>An activity that suspends with state and triggers and resumes on a fresh activation.</summary>
public abstract class StatefulActivity<TResult, TState, TTrigger> : ActivityBase, IStateful
{
    protected abstract ValueTask<Transition> ExecuteAsync(StartContext<TTrigger> context);
    protected abstract ValueTask<Transition> ResumeAsync(ResumeContext<TState, TTrigger> context);

    protected static Transition Complete(TResult result, string outcome = "Done") => new(TransitionKind.Complete, Values.From(result), outcome);
    protected static Transition Suspend(TState state, params Trigger[] triggers) => new(TransitionKind.Suspend, State: Values.From(state), Triggers: triggers);
    protected static Transition Fault(string code, string message) => new(TransitionKind.Fault, Fault: new ActivityFault(code, message));

    ValueTask<Transition> IStateful.StartAsync(ActivityContext context, bool delivery, JsonNode? trigger) =>
        ExecuteAsync(new StartContext<TTrigger>(context, delivery, trigger));

    ValueTask<Transition> IStateful.ResumeAsync(ActivityContext context, JsonNode? state, JsonNode? trigger) =>
        ResumeAsync(new ResumeContext<TState, TTrigger>(context, Values.To<TState>(state)!, Values.To<TTrigger>(trigger)!));
}

// ---- values --------------------------------------------------------------------------------------------------------

public static class Values
{
    public static JsonNode? From<T>(T value) => value switch
    {
        null => null,
        Unit => null,
        JsonNode node => node.DeepClone(),
        _ => JsonSerializer.SerializeToNode(value),
    };

    public static T? To<T>(JsonNode? node) => node switch
    {
        null => default,
        T same => same,
        _ => node.Deserialize<T>(),
    };

    public static object? To(JsonNode? node, Type type) => node is null ? null : type.IsInstanceOfType(node) ? node : node.Deserialize(type);

    public static JsonNode? Path(JsonNode? node, string? path)
    {
        if (string.IsNullOrEmpty(path)) return node;
        foreach (var part in path.Split('.'))
            node = node is JsonObject o && o.TryGetPropertyValue(part, out var next) ? next : null;
        return node;
    }

    public static bool Truthy(JsonNode? node) => node switch
    {
        null => false,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when TryNumber(v, out var d) => d != 0,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length > 0,
        _ => true,
    };

    /// <summary>A message payload as a value: JSON when it parses, else the text itself (a kernel payload is any string).</summary>
    public static JsonNode? Parse(string? payload)
    {
        if (payload is null) return null;
        try { return JsonNode.Parse(payload); }
        catch (JsonException) { return JsonValue.Create(payload); }
    }

    /// <summary>A JSON number whatever backs it: a parsed element, or a CLR long, int or double.</summary>
    public static bool TryNumber(JsonValue v, out double d)
    {
        if (v.TryGetValue(out d)) return true;
        if (v.TryGetValue<long>(out var l)) { d = l; return true; }
        if (v.TryGetValue<int>(out var i)) { d = i; return true; }
        if (v.TryGetValue<decimal>(out var m)) { d = (double)m; return true; }
        return false;
    }
}

/// <summary>Sets the bound inputs on a fresh activity object. Property lookups are cached per type.</summary>

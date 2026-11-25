using System.Text.Json.Nodes;

namespace Kernel.Native;

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

/// <summary>
/// The old core's WriteLines: each line on its own, in order, where <see cref="WriteLine"/> writes. Null or empty writes
/// nothing. A line is a string, or a number or Boolean written as its JSON text, and null is an empty line, as the old
/// core's list writes it; anything else faults.
/// </summary>
public sealed class WriteLines : Activity
{
    [Input] public JsonNode? Lines { get; set; }

    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        if (Lines is null) return new(Complete());
        if (Lines is not JsonArray lines || lines.Any(line => line is not (null or JsonValue)))
            return new(Fault("writelines.lines.invalid", "WriteLines takes a list of strings, numbers or Booleans."));
        var text = lines.Select(line => line is null ? "" : line.GetValueKind() == System.Text.Json.JsonValueKind.String ? line.GetValue<string>() : line.ToJsonString()).ToList();
        foreach (var line in text) WriteLine.Out?.WriteLine(line);
        return new(Complete());
    }
}

/// <summary>
/// Ends the nearest enclosing loop (For, ForEach, While, Do): it completes with the Break outcome, and Sequence, If,
/// Switch and Flowchart pass that outcome up to the loop, as on the old core. Outside a loop it ends what passes it up.
/// </summary>
public sealed class Break : Activity
{
    public const string Outcome = "Break";

    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) => new(Complete(Outcome));
}

/// <summary>
/// The old core's Fault: a deliberate fault with the authored code and message, blank ones taking the defaults, and the
/// optional category and fault type recorded with it as authored.
/// </summary>
public sealed class Fault : Activity
{
    public const string DefaultCode = "workflow.fault";
    public const string DefaultMessage = "The workflow faulted.";

    [Input] public string? Code { get; set; }
    [Input] public string? Message { get; set; }
    [Input] public string? Category { get; set; }
    [Input] public string? FaultType { get; set; }

    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) =>
        new(new Transition(TransitionKind.Fault, Fault: new ActivityFault(Coalesce(Code, DefaultCode), Coalesce(Message, DefaultMessage))
        {
            Category = Category,
            FaultType = FaultType,
        }));

    private static string Coalesce(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
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

/// <summary>
/// An outbound email (round 5 C). Not replay-safe, so External; the host sends it. The effect key is the local part of the
/// message's <c>Message-ID</c>, so a mail server or client that deduplicates by it keeps one copy of a repeat. The
/// domain is the prototype's constant; in a product it is the sending host's.
/// </summary>
[External]
public sealed class SendEmail : Activity
{
    public const string MessageIdDomain = "workflows.elsa.invalid";

    [Input] public string To { get; set; } = "";
    [Input] public string Subject { get; set; } = "";
    [Input] public string Body { get; set; } = "";

    public static string MessageId(string effectKey) => $"<{effectKey}@{MessageIdDomain}>";

    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) =>
        new(Complete().With(new EmailSendEffect(new OutboundEmail(To, Subject, Body, MessageId(context.EffectKey)))));
}

/// <summary>Slow external work. Background: it runs as the kernel's background job, so the instance still takes a cancel or a timer meanwhile.</summary>
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

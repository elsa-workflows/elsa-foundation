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

/// <summary>
/// The old core's graph Complete: ends the nearest enclosing GraphActivity with the boundary outcome its
/// <see cref="Outcome"/> names (a reference key the boundary declares), from any depth in its body. It is unrelated to Break.
/// </summary>
public sealed class Complete : Activity
{
    public const string OutcomeRequiredFaultCode = "activity.graph.complete.outcome-required";

    [Input] public string? Outcome { get; set; }

    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) =>
        new(string.IsNullOrWhiteSpace(Outcome)
            ? Fault(OutcomeRequiredFaultCode, $"Complete node '{context.NodeId}' has no boundary outcome.")
            : Complete().With(new ExitEffect(Outcome)));
}

/// <summary>The old core's Inline: its result is its <see cref="Expression"/>'s value, evaluated as any input is.</summary>
public sealed class Inline : Activity<JsonNode?>
{
    [Input] public JsonNode? Expression { get; set; }

    protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) => new(Complete(Expression));
}

public sealed record RunJavaScriptResult(JsonNode? Value);

/// <summary>
/// The old core's RunJavaScript: a program that sees only its <see cref="Arguments"/> as <c>args</c>, run in the turn by
/// the host's engine (<see cref="INativeScripts"/>). Its value is the result. With <see cref="PossibleOutcomes"/> it routes
/// by returning: a string or number naming one of them is the outcome, anything else <see cref="UnmatchedOutcome"/>. A
/// blank script returns null.
/// </summary>
public sealed class RunJavaScript : Activity<RunJavaScriptResult>
{
    public const string UnmatchedOutcome = "Unmatched";

    /// <summary>The program: a string; null or blank returns null.</summary>
    [Input] public JsonNode? Script { get; set; }
    /// <summary>An object, or null for none.</summary>
    [Input] public JsonNode? Arguments { get; set; }
    /// <summary>A list of outcome names, strings or numbers, or null for the single Done.</summary>
    [Input] public JsonNode? PossibleOutcomes { get; set; }

    protected override async ValueTask<Transition> ExecuteAsync(ActivityContext context)
    {
        string? script = null;
        if (Script is not null && !(Script is JsonValue text && text.GetValueKind() == System.Text.Json.JsonValueKind.String && text.TryGetValue(out script)))
            return Fault("javascript.script.invalid", "RunJavaScript's script must be a string.");
        if (PossibleOutcomes is not (null or JsonArray) || PossibleOutcomes is JsonArray names && names.Any(n => Name(n) is null))
            return Fault("javascript.outcomes.invalid", "RunJavaScript's possible outcomes must be a list of strings or numbers.");
        JsonNode? value = null;
        if (!string.IsNullOrWhiteSpace(script))
        {
            // As on the old core, the arguments matter only to a program that runs.
            if (Arguments is not (null or JsonObject))
                return Fault("javascript.arguments.invalid", "RunJavaScript's arguments must be an object.");
            if (context.Scripts is not { } scripts) return Fault("javascript.unavailable", "The host composes no script engine for RunJavaScript.");
            try { value = await scripts.EvaluateAsync(script, (JsonObject?)Arguments ?? [], context.CancellationToken); }
            // A cancelled turn is abandoned, whatever an engine throws for it (Jint's own cancellation is no OperationCanceledException).
            catch (Exception) when (context.CancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                var message = error.GetType().Name + ": " + error.Message;
                return Fault("javascript.failed", message[..Math.Min(message.Length, 2048)]);
            }
        }
        var result = new RunJavaScriptResult(value);
        if (PossibleOutcomes is not JsonArray { Count: > 0 } outcomes) return Complete(result);
        var selected = Name(value);
        return Complete(result, selected is not null && outcomes.Any(o => Name(o) == selected) ? selected : UnmatchedOutcome);
    }

    // Only a string or a number names an outcome; a number by its JSON text, as the old core reads it.
    private static string? Name(JsonNode? node) => node is JsonValue v
        ? v.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.String => v.GetValue<string>(),
            System.Text.Json.JsonValueKind.Number => v.ToJsonString(),
            _ => null,
        }
        : null;
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

/// <summary>
/// How a Timer's interval reads, as on the old core: text that starts with <c>P</c> is an ISO 8601 duration (<c>PT5M</c>),
/// anything else a time span (<c>00:05:00</c>, or <c>1</c> for a day); at least <see cref="MinInterval"/>. The schedule actor
/// reads it with the same rule (its <c>RecurringSchedule.TryParseInterval</c>; layer 3 shares no project between the two).
/// </summary>
public static class ScheduleIntervals
{
    /// <summary>The shortest interval: one second.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    public static bool TryParse(string? text, out TimeSpan interval)
    {
        interval = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.StartsWith('P'))
        {
            try { interval = System.Xml.XmlConvert.ToTimeSpan(text); }
            catch (Exception refused) when (refused is FormatException or OverflowException) { return false; }
        }
        else if (!TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out interval)) return false;
        return interval >= MinInterval;
    }
}

public sealed record TimerResult(string Interval);

/// <summary>
/// The old core's Timer: a recurring start trigger that starts a new run every <see cref="Interval"/>, counted from each
/// occurrence. The host's schedule actor fires it; the run completes it at once with its interval. A definition with one
/// away from where a run begins is refused, as on the old core (NativeSchedules).
/// </summary>
public sealed class Timer : StatefulActivity<TimerResult, Unit, JsonNode?>
{
    [Input] public string Interval { get; set; } = "";

    protected override ValueTask<Transition> ExecuteAsync(StartContext<JsonNode?> context) => new(Complete(new TimerResult(Interval)));
    protected override ValueTask<Transition> ResumeAsync(ResumeContext<Unit, JsonNode?> context) => new(Complete(new TimerResult(Interval)));
}

public sealed record CronResult(string Expression);

/// <summary>
/// The old core's Cron: a recurring start trigger that starts a new run at each occurrence of its <see cref="Expression"/>,
/// in UTC. The host's schedule actor fires it; the run completes it at once with its expression. A definition with one away
/// from where a run begins is refused, as on the old core (NativeSchedules).
/// </summary>
public sealed class Cron : StatefulActivity<CronResult, Unit, JsonNode?>
{
    [Input] public string Expression { get; set; } = "";

    protected override ValueTask<Transition> ExecuteAsync(StartContext<JsonNode?> context) => new(Complete(new CronResult(Expression)));
    protected override ValueTask<Transition> ResumeAsync(ResumeContext<Unit, JsonNode?> context) => new(Complete(new CronResult(Expression)));
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
    /// <summary>Whether a published event starts a run where one begins; false, it only waits (NativeStartTriggers).</summary>
    [Input] public bool CanStartWorkflow { get; set; } = true;

    protected override ValueTask<Transition> ExecuteAsync(StartContext<JsonNode?> context) =>
        new(CanStartWorkflow && context.TryGetTrigger(out var payload)
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

using System.Globalization;
using System.Text.Json;
using Kernel.Foundation.Contracts;
using Kernel.Model.Contracts;

namespace Kernel.WorkflowActor;

/// <summary>When a recurring start trigger fires: every interval, counted from each occurrence, or at a cron expression's occurrences.</summary>
public sealed record RecurringSchedule(string Kind, string Value)
{
    public const string Interval = "interval";
    public const string Cron = "cron";
}

/// <summary>The host's reading of cron expressions, in UTC.</summary>
public interface ICronCalendar
{
    /// <summary>The first occurrence after <paramref name="after"/>, in Unix milliseconds, or null when there is none.</summary>
    long? Next(string expression, long after);
}

/// <summary>A schedule actor's header: its schedule, and the run each occurrence starts.</summary>
public sealed record ScheduleHeader(RecurringSchedule Schedule, StartSpec Run);

/// <summary>
/// The schedule actor (owner decision of 24 November, card 3 B): one durable actor per recurring start trigger, keyed by
/// the trigger, with one self-timer. At each due occurrence, in one turn, it starts the trigger's workflow as a run of its
/// own and sets the timer to the next occurrence, counted from the turn's time, so an occurrence that commits starts one
/// run, and an owner that wakes after downtime fires the due occurrence once and moves on, as the old core's pump does.
/// It uses the actor contract as it stands: a self-send with a due time, its recall, and a detached start.
/// </summary>
public sealed class ScheduleActor(ICronCalendar? cron = null) : IActorType
{
    public const string ActorType = "elsa.schedule";
    /// <summary>Creates the schedule or replaces its schedule and run; the payload is the <see cref="ScheduleHeader"/>.</summary>
    public const string SetKind = "schedule.set";
    /// <summary>The self-timer; its payload is the due time it was set for.</summary>
    public const string FireKind = "schedule.fire";
    /// <summary>Ends the schedule: no further occurrence fires.</summary>
    public const string StopKind = "schedule.stop";

    private sealed record ScheduleState(long Due, string Timer, long Fired);

    public string Type => ActorType;

    /// <summary>
    /// Creates the trigger's schedule actor, or sets its schedule and run, which replaces its timer. A repeat of one
    /// <paramref name="operationId"/> (a publication of the definition, say) is a duplicate; another one applies, also
    /// when it sets a schedule the actor had before.
    /// </summary>
    public static async ValueTask<Admission> SetAsync(IIngress ingress, BoundScope scope, string key, string operationId, RecurringSchedule schedule,
        WorkflowStart run, CancellationToken cancellationToken = default)
    {
        var header = JsonSerializer.Serialize(new ScheduleHeader(schedule, WorkflowActor.Start(run)));
        var id = $"{key}#{operationId}";
        var created = await ingress.SendAsync(scope, new Target.Key(key, new StartSpec(ActorType, header, History: History.Minimal)), id, SetKind, header,
            cancellationToken: cancellationToken);
        // The key's actor exists already: the foundation takes one start per key (Repeat), so the new schedule goes to it as a
        // message. A repeat of such a message meets that message's receipt, which differs from a start's (Conflict).
        return created is { Outcome: AdmissionOutcome.Refused, Reason: RefusalReason.Repeat or RefusalReason.Conflict }
            ? await ingress.SendAsync(scope, new Target.Key(key), id, SetKind, header, cancellationToken: cancellationToken)
            : created;
    }

    /// <summary>Ends the trigger's schedule; <paramref name="operationId"/> makes a repeat a duplicate.</summary>
    public static ValueTask<Admission> StopAsync(IIngress ingress, BoundScope scope, string key, string operationId, CancellationToken cancellationToken = default) =>
        ingress.SendAsync(scope, new Target.Key(key), operationId, StopKind, cancellationToken: cancellationToken);

    public bool Withholds(ActorHeader header) => false;

    public ValueTask TurnAsync(IActorTurn turn, CancellationToken cancellationToken)
    {
        var state = turn.State is { } saved ? JsonSerializer.Deserialize<ScheduleState>(saved) : null;
        var header = Read(turn.Header.TypeData);
        foreach (var message in turn.Inbox)
        {
            switch (message.Kind)
            {
                case Kinds.Terminate:
                    Disarm(turn, state);
                    turn.End(ActorStatus.Cancelled);
                    return ValueTask.CompletedTask;
                case StopKind:
                    Disarm(turn, state);
                    turn.End(ActorStatus.Finished);
                    return ValueTask.CompletedTask;
                case Kinds.Start when state is null:
                    // The creating send arrives as the actor's start: its header is the schedule.
                    state = Arm(turn, header, 0);
                    break;
                case SetKind when message.Payload is { } payload:
                    header = Read(payload);
                    if (payload != turn.Header.TypeData) turn.SetHeader(payload, turn.Header.Labels);
                    Disarm(turn, state);
                    state = Arm(turn, header, state?.Fired ?? 0);
                    break;
                case FireKind:
                    // A timer the schedule has since replaced, or a repeat of one already handled, starts nothing.
                    if (state is null || message.Payload != state.Due.ToString(CultureInfo.InvariantCulture)) break;
                    turn.Start(header.Run, Placement.Spread, detached: true);
                    state = Arm(turn, header, state.Fired + 1);
                    break;
            }
            if (state is { Timer: "" }) break;     // no next occurrence: the schedule has ended
        }
        if (state is { Timer: "" })
        {
            turn.End(ActorStatus.Finished);
            return ValueTask.CompletedTask;
        }
        turn.State = state is null ? turn.State : JsonSerializer.Serialize(state);
        return ValueTask.CompletedTask;
    }

    // The next occurrence after now: an interval counts from now, a cron expression gives its next occurrence. None ends it.
    private ScheduleState Arm(IActorTurn turn, ScheduleHeader header, long fired)
    {
        long? due = header.Schedule.Kind switch
        {
            RecurringSchedule.Interval => checked(turn.Now + (long)Interval(header.Schedule.Value).TotalMilliseconds),
            RecurringSchedule.Cron => (cron ?? throw new InvalidOperationException("The host reads no cron expressions.")).Next(header.Schedule.Value, turn.Now),
            var other => throw new InvalidOperationException($"No schedule kind '{other}'."),
        };
        if (due is not { } next) return new(0, "", fired);
        var text = next.ToString(CultureInfo.InvariantCulture);
        return new(next, turn.Send(turn.Header.Ref, FireKind, text, next), fired);
    }

    private static void Disarm(IActorTurn turn, ScheduleState? state)
    {
        if (state is { Timer.Length: > 0 }) turn.Recall(state.Timer);
    }

    private static TimeSpan Interval(string text)
    {
        TimeSpan interval;
        if (text.Contains(':')) interval = TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        else interval = System.Xml.XmlConvert.ToTimeSpan(text);
        return interval > TimeSpan.Zero ? interval : throw new InvalidOperationException($"The interval '{text}' is not positive.");
    }

    private static ScheduleHeader Read(string typeData) =>
        JsonSerializer.Deserialize<ScheduleHeader>(typeData) ?? throw new InvalidOperationException("The schedule header is absent.");

    public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A schedule runs no jobs.");

    public RestorePlan RestoreWaits(ActorHeader header, string state)
    {
        var schedule = JsonSerializer.Deserialize<ScheduleState>(state);
        return new(schedule is { Timer.Length: > 0 }
            ? [new TimerSpec(schedule.Timer, FireKind, schedule.Due.ToString(CultureInfo.InvariantCulture), schedule.Due)]
            : [], [], []);
    }

    public long TerminalGrowth(ActorHeader header, string state) => 0;

    public bool ProtectsDeletion(ActorHeader header, string state, long now) => false;
}

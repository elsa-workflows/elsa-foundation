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

    /// <summary>The shortest interval: one second.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// An interval as the old core reads one: text that starts with <c>P</c> is an ISO 8601 duration (<c>PT5M</c>), anything
    /// else a time span (<c>00:05:00</c>, or <c>1</c> for a day); at least <see cref="MinInterval"/>.
    /// </summary>
    public static bool TryParseInterval(string? text, out TimeSpan interval)
    {
        interval = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.StartsWith('P'))
        {
            try { interval = System.Xml.XmlConvert.ToTimeSpan(text); }
            catch (Exception refused) when (refused is FormatException or OverflowException) { return false; }
        }
        else if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out interval)) return false;
        return interval >= MinInterval;
    }
}

/// <summary>The host's reading of cron expressions, in UTC.</summary>
public interface ICronCalendar
{
    /// <summary>The first occurrence after <paramref name="after"/>, in Unix milliseconds, or null when there is none; throws for an expression it does not read.</summary>
    long? Next(string expression, long after);
}

/// <summary>A schedule actor's header: its schedule, the start key each occurrence publishes to, and the revision that set them.</summary>
public sealed record ScheduleHeader(RecurringSchedule Schedule, string StartKey, long Revision);

/// <summary>
/// The schedule actor (owner decision of 24 November, card 3 B): one durable actor per recurring start trigger, keyed by
/// the trigger, with one self-timer that publishes to the trigger's start key at each occurrence. The host registers the
/// run on that key (<see cref="RegisterRunAsync"/>), so a republished definition changes the registration, not the actor.
/// The publication, the consumed timer and the next timer commit in one turn, so an occurrence that commits publishes
/// once; the next timer counts from the turn's time, so an owner that wakes after downtime fires the due occurrence once
/// and moves on, as the old core's pump does. It uses the actor contract as it stands: a self-send with a due time, its
/// recall, and a publication.
/// <list type="bullet">
/// <item>Set (<see cref="SetAsync"/>) creates the actor or applies a newer revision; an older one is ignored, and an
/// unchanged schedule keeps its timer.</item>
/// <item>Stop (<see cref="StopAsync"/>) disarms it and leaves it idle, so a later Set re-arms it; only the foundation's
/// Terminate ends it, and an ended actor takes nothing more. Suspend and the other operator commands change nothing.</item>
/// <item>A schedule it cannot read (an interval below <see cref="RecurringSchedule.MinInterval"/>, a cron expression on a
/// host without a calendar or that the calendar refuses) leaves it idle with the reason in its state.</item>
/// </list>
/// </summary>
public sealed class ScheduleActor(ICronCalendar? cron = null) : IActorType
{
    public const string ActorType = "elsa.schedule";
    /// <summary>Creates the schedule or applies a newer revision; the payload is the <see cref="ScheduleHeader"/>.</summary>
    public const string SetKind = "schedule.set";
    /// <summary>The self-timer; its payload is the due time it was set for.</summary>
    public const string FireKind = "schedule.fire";
    /// <summary>Disarms the schedule at a revision; the payload is that revision.</summary>
    public const string StopKind = "schedule.stop";
    /// <summary>The wait name of a schedule's start registration.</summary>
    public const string StartWait = "schedule";

    /// <summary>How long an occurrence's publication waits for its start: a minute.</summary>
    public static readonly TimeSpan PublicationWindow = TimeSpan.FromMinutes(1);

    private sealed record ScheduleState(long Revision, long Due = 0, string Timer = "", bool Stopped = false, string? Error = null);

    public string Type => ActorType;

    /// <summary>Registers the run each occurrence starts on the trigger's start key; null removes it.</summary>
    public static ValueTask RegisterRunAsync(IIngress ingress, BoundScope scope, string startKey, WorkflowStart? run, CancellationToken cancellationToken = default) =>
        ingress.SetStartAsync(scope, startKey, StartWait, run is null ? null : WorkflowActor.Start(run), cancellationToken);

    /// <summary>
    /// Creates the trigger's schedule actor, or applies <paramref name="revision"/> to it (a publication of the definition,
    /// say): a repeat of one revision is a duplicate, an older one changes nothing, and a newer one replaces the schedule.
    /// One revision means one schedule: a revision sent again with another schedule is refused or ignored, and the first is kept. A
    /// schedule that does not read is refused; a cron expression is read with <paramref name="calendar"/> when one is given.
    /// </summary>
    /// <remarks>To unpublish, stop the schedule before removing its run's registration, so no occurrence is published without one.</remarks>
    public static async ValueTask<Admission> SetAsync(IIngress ingress, BoundScope scope, string key, long revision, RecurringSchedule schedule, string startKey,
        ICronCalendar? calendar = null, CancellationToken cancellationToken = default)
    {
        if (schedule.Kind == RecurringSchedule.Interval ? !RecurringSchedule.TryParseInterval(schedule.Value, out _) : schedule.Kind != RecurringSchedule.Cron || !ReadsCron(calendar, schedule.Value))
            throw new ArgumentException($"The schedule {schedule.Kind} '{schedule.Value}' does not read.", nameof(schedule));
        var header = JsonSerializer.Serialize(new ScheduleHeader(schedule with { Value = schedule.Value.Trim() }, startKey, revision));
        var id = $"{key}#{revision.ToString(CultureInfo.InvariantCulture)}";
        var created = await ingress.SendAsync(scope, new Target.Key(key, new StartSpec(ActorType, header, History: History.Minimal)), id, SetKind, header,
            cancellationToken: cancellationToken);
        // The key's actor exists already: the foundation takes one start per key (Repeat), so the new schedule goes to it as a
        // message. A repeat of such a message meets that message's receipt, which differs from a start's (Conflict).
        return created is { Outcome: AdmissionOutcome.Refused, Reason: RefusalReason.Repeat or RefusalReason.Conflict }
            ? await ingress.SendAsync(scope, new Target.Key(key), id, SetKind, header, cancellationToken: cancellationToken)
            : created;
    }

    private static bool ReadsCron(ICronCalendar? calendar, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;
        if (calendar is null) return true;
        try { calendar.Next(expression.Trim(), 0); return true; }
        catch (Exception refused) when (refused is FormatException or ArgumentException) { return false; }
    }

    /// <summary>Disarms the trigger's schedule at <paramref name="revision"/>; a later Set re-arms it. A repeat is a duplicate.</summary>
    public static ValueTask<Admission> StopAsync(IIngress ingress, BoundScope scope, string key, long revision, CancellationToken cancellationToken = default) =>
        ingress.SendAsync(scope, new Target.Key(key), $"{key}#stop#{revision.ToString(CultureInfo.InvariantCulture)}", StopKind,
            revision.ToString(CultureInfo.InvariantCulture), cancellationToken: cancellationToken);

    public bool Withholds(ActorHeader header) => false;

    public ValueTask TurnAsync(IActorTurn turn, CancellationToken cancellationToken)
    {
        if (turn.Header.Status != ActorStatus.Running) return ValueTask.CompletedTask;     // an ended schedule takes nothing
        var header = Read(turn.Header.TypeData);
        var state = turn.State is { } saved ? JsonSerializer.Deserialize<ScheduleState>(saved)! : null;
        foreach (var message in turn.Inbox)
        {
            switch (message.Kind)
            {
                case Kinds.Terminate:
                    Disarm(turn, state);
                    turn.End(ActorStatus.Cancelled);
                    return ValueTask.CompletedTask;
                case Kinds.Start when state is null:
                    // The creating send arrives as the actor's start: its header is the schedule.
                    state = Arm(turn, header, new ScheduleState(header.Revision), turn.Now);
                    break;
                case SetKind when message.Payload is { } payload:
                {
                    var next = Read(payload);
                    if (state is not null && next.Revision <= state.Revision) break;     // an older or repeated revision
                    var unchanged = state is { Stopped: false, Error: null, Timer.Length: > 0 } && next.Schedule == header.Schedule && next.StartKey == header.StartKey;
                    turn.SetHeader(payload, turn.Header.Labels);
                    header = next;
                    if (unchanged) { state = state! with { Revision = next.Revision }; break; }     // the pending occurrence carries over
                    Disarm(turn, state);
                    state = Arm(turn, header, new ScheduleState(next.Revision), turn.Now);
                    break;
                }
                case StopKind when long.TryParse(message.Payload, NumberStyles.None, CultureInfo.InvariantCulture, out var revision):
                    if (state is null || revision < state.Revision) break;
                    Disarm(turn, state);
                    state = new ScheduleState(revision, Stopped: true);
                    break;
                case FireKind:
                    // A timer the schedule has since replaced, or a repeat of one already handled, publishes nothing.
                    if (state is not { Stopped: false, Timer.Length: > 0 } || message.Payload != state.Due.ToString(CultureInfo.InvariantCulture)) break;
                    turn.Publish(header.StartKey, state.Due.ToString(CultureInfo.InvariantCulture), PublicationWindow);
                    state = Arm(turn, header, state with { Timer = "" }, Math.Max(turn.Now, state.Due));
                    break;
            }
        }
        if (state is not null) turn.State = JsonSerializer.Serialize(state);
        return ValueTask.CompletedTask;
    }

    // The next occurrence after a time: an interval counts from it, a cron expression gives its next occurrence. None, or a
    // schedule it cannot read, leaves the schedule idle.
    private ScheduleState Arm(IActorTurn turn, ScheduleHeader header, ScheduleState state, long after)
    {
        long? due;
        switch (header.Schedule.Kind)
        {
            case RecurringSchedule.Interval when RecurringSchedule.TryParseInterval(header.Schedule.Value, out var interval):
                due = checked(after + (long)interval.TotalMilliseconds);
                break;
            case RecurringSchedule.Cron when cron is not null:
                try { due = cron.Next(header.Schedule.Value, after); }
                catch (Exception refused) when (refused is FormatException or ArgumentException) { return state with { Due = 0, Timer = "", Error = refused.Message }; }
                break;
            case RecurringSchedule.Cron:
                return state with { Due = 0, Timer = "", Error = "The host reads no cron expressions." };
            default:
                return state with { Due = 0, Timer = "", Error = $"The schedule {header.Schedule.Kind} '{header.Schedule.Value}' does not read." };
        }
        if (due is not { } next) return state with { Due = 0, Timer = "" };     // the expression has no further occurrence
        next = Math.Max(next, checked(turn.Now + 1));
        return state with { Due = next, Timer = turn.Send(turn.Header.Ref, FireKind, next.ToString(CultureInfo.InvariantCulture), next), Error = null };
    }

    private static void Disarm(IActorTurn turn, ScheduleState? state)
    {
        if (state is { Timer.Length: > 0 }) turn.Recall(state.Timer);
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

    /// <summary>A schedule that has not ended is kept, stopped or not, so that a later Set finds it.</summary>
    public bool ProtectsDeletion(ActorHeader header, string state, long now) => header.Status == ActorStatus.Running;
}

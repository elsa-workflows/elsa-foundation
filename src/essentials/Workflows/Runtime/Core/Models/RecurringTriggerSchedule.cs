namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A durable, persisted recurring start schedule for a Timer/Cron start-trigger activity in a published
/// workflow (on the trigger-stimulus seam). Unlike a <see cref="DurableTimer"/> — which resumes one suspended
/// execution at a one-shot deadline — a recurring schedule has <b>no execution id</b>: it starts a <i>new</i>
/// workflow instance every time it fires. The hosted recurring-trigger pump reads due schedules and dispatches
/// the start stimulus identified by (<see cref="StimulusType"/>, <see cref="StimulusHash"/>) through the
/// stimulus router in start-only mode.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity &amp; replacement.</b> Legacy artifact-scoped indexing derives <see cref="ScheduleId"/> from
/// (artifactId, executableNodeId). Activation preparation additionally includes activation ID so explicit
/// named slots may share an artifact without collapsing their independent schedule lifecycles.
/// </para>
/// <para>
/// <b>Missed-occurrence policy.</b> <see cref="NextOccurrence"/> is the single mutable cursor. Once the occurrence in
/// it has been fired, the pump advances it to the first occurrence strictly after the wake instant — <i>not</i> to
/// <c>previous + interval</c> — so a pump that wakes after downtime fires the due occurrence once and never replays
/// the backlog of occurrences that elapsed while it was down.
/// </para>
/// <para>
/// <b>At least once per occurrence (#2198).</b> The occurrence in the cursor is claimed as in flight under a fenced
/// lease before it is fired (see <c>IRecurringTriggerScheduleStore.ClaimDueAsync</c>), and the cursor moves past it
/// only when that claim is settled. A worker that dies before settling leaves the occurrence to a peer once the lease
/// lapses; every fire of one occurrence carries the key <see cref="BuildOccurrenceKey"/> names, so the repeat converges
/// on the workflow execution the first fire started.
/// </para>
/// <para>
/// <b>Republish.</b> Activating a new publication of a slot replaces the replaced publication's schedules, and each new
/// schedule takes over the cursor of the replaced schedule of the same trigger (<see cref="IsSameTriggerAs"/>) when that
/// cursor names an occurrence which fell due before the new schedule was materialized (<see cref="TakeOverFrom"/>). The
/// occurrence key identifies the trigger, not the publication, so the new publication's fire of that occurrence and any
/// fire the replaced publication already made converge on one start.
/// </para>
/// </remarks>
/// <param name="ScheduleId">Deterministic id built from (artifactId, executableNodeId).</param>
/// <param name="ArtifactId">The published artifact that owns the trigger node; scopes replace-on-republish.</param>
/// <param name="ExecutableNodeId">The trigger node that owns this schedule. Together with <see cref="ArtifactId"/>,
/// <see cref="ActivationId"/>/<see cref="SlotId"/> and the stimulus identity it names the exact trigger binding a
/// fire starts through, so the pump never hash-broadcasts a recurring start to other artifacts (or other nodes)
/// that authored the same interval/cron literal.</param>
/// <param name="StimulusType">The start stimulus type the pump dispatches (e.g. Timer, Cron).</param>
/// <param name="StimulusHash">The start stimulus hash — matches the trigger binding the router indexed for the same node.</param>
/// <param name="Kind">Whether <see cref="Expression"/> is an interval or a cron expression.</param>
/// <param name="Expression">The recurrence spec: an ISO-8601 duration (Interval) or a cron string (Cron).</param>
/// <param name="NextOccurrence">The next wall-clock instant the schedule is due to fire: the occurrence in flight while it is claimed.</param>
/// <param name="CreatedAt">When the schedule was first written.</param>
public sealed record RecurringTriggerSchedule(
    string ScheduleId,
    string ArtifactId,
    string ExecutableNodeId,
    string StimulusType,
    string StimulusHash,
    RecurringScheduleKind Kind,
    string Expression,
    DateTimeOffset NextOccurrence,
    DateTimeOffset CreatedAt,
    string? ActivationId = null,
    string? SlotId = null,
    bool IsActive = true)
{
    /// <summary>
    /// Builds the deterministic, collision-free schedule id for a trigger node in an artifact, using the same
    /// escaping as <c>WorkflowTriggerBinding.BuildId</c> so a separator inside an id cannot forge a different
    /// (artifactId, executableNodeId) pair.
    /// </summary>
    public static string BuildId(string artifactId, string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        return $"{Escape(artifactId)}:{Escape(executableNodeId)}";
    }

    /// <summary>Builds an activation-scoped schedule id so named slots may share one artifact safely.</summary>
    public static string BuildId(string activationId, string artifactId, string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        return $"{Escape(activationId)}:{BuildId(artifactId, executableNodeId)}";
    }

    /// <summary>
    /// Builds a schedule id disambiguated by stimulus hash (spec 117), for a trigger node that fans out several
    /// recurring schedules (a BPMN process with more than one timer start event). Single-schedule triggers keep
    /// the plain <see cref="BuildId(string,string)"/> id so their schedule identity is unchanged.
    /// </summary>
    public static string BuildFanOutId(string artifactId, string executableNodeId, string stimulusHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        return $"{BuildId(artifactId, executableNodeId)}:{Escape(stimulusHash)}";
    }

    /// <summary>Activation-scoped, stimulus-hash-disambiguated schedule id (spec 117 fan-out).</summary>
    public static string BuildFanOutId(string activationId, string artifactId, string executableNodeId, string stimulusHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);
        return $"{Escape(activationId)}:{BuildFanOutId(artifactId, executableNodeId, stimulusHash)}";
    }

    /// <summary>
    /// The idempotency key every fire of the occurrence in <see cref="NextOccurrence"/> carries (#2198). A slot-scoped
    /// schedule keys the occurrence by its trigger, <c>recurring:{slotId}:{executableNodeId}:{stimulusHash}:{occurrenceTicks}</c>
    /// with each id escaped as <see cref="BuildId(string,string)"/> escapes it, so every publication of the slot names one
    /// occurrence alike; its fire starts under the artifact-free <see cref="KeyedWorkflowStartIdentity.ForOccurrence"/>. A
    /// schedule without a slot (legacy artifact-scoped indexing) keeps <c>recurring:{ScheduleId}:{occurrenceTicks}</c> and the
    /// per-artifact <see cref="KeyedWorkflowStartIdentity.For"/>.
    /// </summary>
    /// <remarks>
    /// <b>Frozen.</b> The key is the input of a persisted keyed-start identity, and a repeated fire is recognized only by
    /// deriving the same key again, so changing it silently starts every repeated occurrence a second time.
    /// <c>KeyedWorkflowStartIdentityTests</c> pins it.
    /// </remarks>
    public string BuildOccurrenceKey() =>
        SlotId is null
            ? $"recurring:{ScheduleId}:{NextOccurrence.UtcTicks}"
            : $"recurring:{Escape(SlotId)}:{Escape(ExecutableNodeId)}:{Escape(StimulusHash)}:{NextOccurrence.UtcTicks}";

    /// <summary>
    /// Whether <paramref name="other"/> is the same trigger in another publication of the same slot: the same slot (which
    /// names the workflow definition and the slot), authored trigger node and stimulus. A schedule without a slot is never
    /// the same trigger as another.
    /// </summary>
    public bool IsSameTriggerAs(RecurringTriggerSchedule other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return SlotId is not null &&
               StringComparer.Ordinal.Equals(SlotId, other.SlotId) &&
               StringComparer.Ordinal.Equals(ExecutableNodeId, other.ExecutableNodeId) &&
               StringComparer.Ordinal.Equals(StimulusType, other.StimulusType) &&
               StringComparer.Ordinal.Equals(StimulusHash, other.StimulusHash);
    }

    /// <summary>
    /// This schedule as it replaces <paramref name="replaced"/>, the same trigger's schedule in the publication it replaces
    /// (#2198). It takes over the replaced cursor when that names an occurrence earlier than its own cursor and no later than
    /// its own creation: one that fell due before this schedule was materialized, so its own cursor, the first occurrence
    /// after its creation, would skip it. Otherwise it is unchanged.
    /// </summary>
    public RecurringTriggerSchedule TakeOverFrom(RecurringTriggerSchedule replaced)
    {
        ArgumentNullException.ThrowIfNull(replaced);
        if (!IsSameTriggerAs(replaced))
            throw new ArgumentException($"Schedule '{replaced.ScheduleId}' is not the same trigger as schedule '{ScheduleId}'.", nameof(replaced));
        return replaced.NextOccurrence < NextOccurrence && replaced.NextOccurrence <= CreatedAt
            ? this with { NextOccurrence = replaced.NextOccurrence }
            : this;
    }

    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");
}

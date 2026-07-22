namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A durable, persisted recurring start schedule for a Timer/Cron start-trigger activity in a published
/// workflow (W16, on W7's trigger seam). Unlike a <see cref="DurableTimer"/> — which resumes one suspended
/// execution at a one-shot deadline — a recurring schedule has <b>no execution id</b>: it starts a <i>new</i>
/// workflow instance every time it fires. The hosted recurring-trigger pump reads due schedules and dispatches
/// the start stimulus identified by (<see cref="StimulusType"/>, <see cref="StimulusHash"/>) through the
/// stimulus router in start-only mode.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity &amp; replacement.</b> Legacy artifact-scoped indexing derives <see cref="ScheduleId"/> from
/// (artifactId, executableNodeId). Publication preparation additionally includes publication ID so explicit
/// named slots may share an artifact without collapsing their independent schedule lifecycles.
/// </para>
/// <para>
/// <b>Missed-occurrence policy.</b> <see cref="NextOccurrence"/> is the single mutable cursor. On each fire the
/// pump advances it to the first occurrence strictly after the wake instant — <i>not</i> to
/// <c>previous + interval</c> — so a pump that wakes after downtime fires <b>at most once</b> per schedule and
/// never replays the backlog of occurrences that elapsed while it was down.
/// </para>
/// <para>
/// <b>Cluster-safety hook (W20).</b> The cursor is advanced through a compare-and-swap on
/// <see cref="NextOccurrence"/> (see <c>IRecurringTriggerScheduleStore.TryAdvanceAsync</c>), so exactly one
/// worker can claim a given occurrence. Single-node hosts get this for free; a future clustered store keeps the
/// same CAS contract to make the pump cluster-safe without changing the pump.
/// </para>
/// </remarks>
/// <param name="ScheduleId">Deterministic id built from (artifactId, executableNodeId).</param>
/// <param name="ArtifactId">The published artifact that owns the trigger node; scopes replace-on-republish.</param>
/// <param name="ExecutableNodeId">The trigger node that owns this schedule. Together with <see cref="ArtifactId"/>,
/// <see cref="PublicationId"/>/<see cref="SlotId"/> and the stimulus identity it names the exact trigger binding a
/// fire starts through, so the pump never hash-broadcasts a recurring start to other artifacts (or other nodes)
/// that authored the same interval/cron literal.</param>
/// <param name="StimulusType">The start stimulus type the pump dispatches (e.g. Timer, Cron).</param>
/// <param name="StimulusHash">The start stimulus hash — matches the trigger binding the router indexed for the same node.</param>
/// <param name="Kind">Whether <see cref="Expression"/> is an interval or a cron expression.</param>
/// <param name="Expression">The recurrence spec: an ISO-8601 duration (Interval) or a cron string (Cron).</param>
/// <param name="NextOccurrence">The next wall-clock instant the schedule is due to fire (the CAS cursor).</param>
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
    string? PublicationId = null,
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

    /// <summary>Builds a publication-scoped schedule id so named slots may share one artifact safely.</summary>
    public static string BuildId(string publicationId, string artifactId, string executableNodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        return $"{Escape(publicationId)}:{BuildId(artifactId, executableNodeId)}";
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

    /// <summary>Publication-scoped, stimulus-hash-disambiguated schedule id (spec 117 fan-out).</summary>
    public static string BuildFanOutId(string publicationId, string artifactId, string executableNodeId, string stimulusHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId);
        return $"{Escape(publicationId)}:{BuildFanOutId(artifactId, executableNodeId, stimulusHash)}";
    }

    private static string Escape(string value) => value.Replace("%", "%25").Replace(":", "%3A");
}

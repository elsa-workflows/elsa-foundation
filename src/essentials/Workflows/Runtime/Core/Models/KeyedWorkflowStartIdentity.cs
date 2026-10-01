namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The deterministic identity of a workflow start named by a stable start key (#2195). The workflow execution id and the
/// start command and envelope ids all derive from the key, so delivering the same start more than once converges on one
/// execution instead of starting another. The key names one occurrence of a start: the stimulus router builds it with
/// <see cref="For"/> from a routed request's idempotency key and the matched artifact, so a redelivered PublishStimulus
/// intent, a re-fired recurring occurrence, or a repeated keyed API call starts each matching workflow once.
/// </summary>
/// <remarks>
/// <para>
/// Convergence has two halves, both in <see cref="Contracts.IWorkflowStartDispatcher"/>, and they are the same pair that
/// make a DispatchWorkflow child start idempotent (<see cref="WorkflowDispatchIdentity"/>). A keyed start whose execution
/// already exists is answered as a <see cref="WorkflowExecutionCommandDispatchStatus.Duplicate"/> and enqueues nothing.
/// A keyed start that is still in flight enqueues the same start envelope again, which the scheduler work queue absorbs by
/// its work-item identity. Behind both, the start work handler refuses a Start for an execution that already has state.
/// </para>
/// <para>
/// A request is a keyed start exactly when its workflow execution id is the one derived from its idempotency key
/// (<see cref="TryGet"/>), so the identity travels on the request's existing fields.
/// </para>
/// <para>
/// <b>The formats are frozen (versions <see cref="Version"/> and <see cref="OccurrenceVersion"/>).</b> Every id is
/// <c>{prefix}:start:{version}:{digest}</c>, with the prefixes <c>wfexec</c>, <c>command</c> and <c>envelope</c>. The
/// digest is the lowercase hex SHA-256 of the values <c>"elsa.workflow-start"</c>, the version and the start key, each
/// value's UTF-8 bytes prefixed by its 4-byte big-endian length. Under <c>v1</c> (<see cref="For"/>) the router's start key
/// is <c>{idempotencyKey}:start:{artifactId}</c>. Under <c>v2</c> (<see cref="ForOccurrence"/>, #2198), used only for a
/// recurring-trigger occurrence whose key already identifies the trigger across publications, it is
/// <c>{occurrenceKey}:start</c>, with no artifact. Execution ids derived this way are persisted and are what a redelivery
/// is matched against, so changing any part of a derivation silently starts every redelivered occurrence a second time. To
/// change one, add a new version beside these; never edit <c>v1</c> or <c>v2</c>.
/// </para>
/// </remarks>
public sealed class KeyedWorkflowStartIdentity
{
    /// <summary>The per-artifact derivation (#2195), <see cref="For"/>.</summary>
    public const string Version = "v1";

    /// <summary>The artifact-free derivation of one recurring-trigger occurrence (#2198), <see cref="ForOccurrence"/>.</summary>
    public const string OccurrenceVersion = "v2";

    private KeyedWorkflowStartIdentity(string startKey, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startKey);

        var digest = RuntimeIdentityDigest.Compute("elsa.workflow-start", version, startKey);
        StartKey = startKey;
        DerivationVersion = version;
        WorkflowExecutionId = $"wfexec:start:{version}:{digest}";
        CommandId = $"command:start:{version}:{digest}";
        EnvelopeId = $"envelope:start:{version}:{digest}";
    }

    /// <summary>The key the identity derives from. A keyed start request carries it as its idempotency key.</summary>
    public string StartKey { get; }

    /// <summary>The derivation the ids carry: <see cref="Version"/> or <see cref="OccurrenceVersion"/>.</summary>
    public string DerivationVersion { get; }

    /// <summary>
    /// Whether the start is scoped to one artifact (<see cref="Version"/>). An occurrence start (<see cref="OccurrenceVersion"/>)
    /// names its occurrence whichever publication of the trigger fires it, so its execution may be pinned to another artifact.
    /// </summary>
    public bool IsArtifactScoped => DerivationVersion == Version;

    public string WorkflowExecutionId { get; }
    public string CommandId { get; }
    public string EnvelopeId { get; }

    /// <summary>
    /// The keyed start of <paramref name="artifactId"/> named by a routed request's <paramref name="idempotencyKey"/>. The
    /// start key is <c>{idempotencyKey}:start:{artifactId}</c>, so one keyed stimulus that matches two workflows starts
    /// each of them, and starts each once.
    /// </summary>
    public static KeyedWorkflowStartIdentity For(string idempotencyKey, string artifactId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        return new($"{idempotencyKey}:start:{artifactId}", Version);
    }

    /// <summary>
    /// The keyed start of one recurring-trigger occurrence (#2198), named by an <paramref name="occurrenceKey"/> that already
    /// identifies the trigger across publications (<see cref="RecurringTriggerSchedule.BuildOccurrenceKey"/>). The start key
    /// is <c>{occurrenceKey}:start</c>, with no artifact, so a republish that changes the artifact while the occurrence is in
    /// flight still starts it once.
    /// </summary>
    public static KeyedWorkflowStartIdentity ForOccurrence(string occurrenceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrenceKey);
        return new($"{occurrenceKey}:start", OccurrenceVersion);
    }

    /// <summary>The keyed identity <paramref name="request"/> carries, or <see langword="null"/> when it is not a keyed start.</summary>
    public static KeyedWorkflowStartIdentity? TryGet(WorkflowExecutionStartDispatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IdempotencyKey is not { } startKey || request.WorkflowExecutionId is not { } workflowExecutionId)
            return null;

        foreach (var version in (ReadOnlySpan<string>)[Version, OccurrenceVersion])
        {
            var identity = new KeyedWorkflowStartIdentity(startKey, version);
            if (StringComparer.Ordinal.Equals(identity.WorkflowExecutionId, workflowExecutionId))
                return identity;
        }

        return null;
    }
}

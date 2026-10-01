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
/// <b>The format is frozen (version <see cref="Version"/>).</b> Every id is <c>{prefix}:start:v1:{digest}</c>, with the
/// prefixes <c>wfexec</c>, <c>command</c> and <c>envelope</c>. The digest is the lowercase hex SHA-256 of the
/// length-prefixed UTF-8 values <c>"elsa.workflow-start"</c>, <c>"v1"</c> and the start key, and the router's start key
/// is <c>{idempotencyKey}:start:{artifactId}</c>. Execution ids derived this way are persisted and are what a redelivery
/// is matched against, so changing any part of the derivation silently starts every redelivered occurrence a second
/// time. To change it, add a new version beside this one; never edit <c>v1</c>.
/// </para>
/// </remarks>
public sealed class KeyedWorkflowStartIdentity
{
    public const string Version = "v1";

    public KeyedWorkflowStartIdentity(string startKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startKey);

        var digest = RuntimeIdentityDigest.Compute("elsa.workflow-start", Version, startKey);
        StartKey = startKey;
        WorkflowExecutionId = $"wfexec:start:{Version}:{digest}";
        CommandId = $"command:start:{Version}:{digest}";
        EnvelopeId = $"envelope:start:{Version}:{digest}";
    }

    /// <summary>The key the identity derives from. A keyed start request carries it as its idempotency key.</summary>
    public string StartKey { get; }

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
        return new($"{idempotencyKey}:start:{artifactId}");
    }

    /// <summary>The keyed identity <paramref name="request"/> carries, or <see langword="null"/> when it is not a keyed start.</summary>
    public static KeyedWorkflowStartIdentity? TryGet(WorkflowExecutionStartDispatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IdempotencyKey is not { } startKey || request.WorkflowExecutionId is not { } workflowExecutionId)
            return null;

        var identity = new KeyedWorkflowStartIdentity(startKey);
        return StringComparer.Ordinal.Equals(identity.WorkflowExecutionId, workflowExecutionId) ? identity : null;
    }
}

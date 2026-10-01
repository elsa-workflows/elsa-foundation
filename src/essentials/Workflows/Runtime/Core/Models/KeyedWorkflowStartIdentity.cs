namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The deterministic identity of a workflow start named by a stable start key (#2195). The workflow execution id and the
/// start command and envelope ids all derive from the key, so delivering the same start more than once converges on one
/// execution instead of starting another. The key names one occurrence of a start: the stimulus router uses a routed
/// request's idempotency key together with the matched artifact, so a redelivered PublishStimulus intent, or any other
/// keyed stimulus, starts each matching workflow once.
/// </summary>
/// <remarks>
/// <para>
/// Convergence has two halves, both in <see cref="Contracts.IWorkflowStartDispatcher"/>, and they are the same pair that
/// make a DispatchWorkflow child start idempotent (<see cref="WorkflowDispatchIdentity"/>). A keyed start whose execution
/// already exists is answered as a <see cref="WorkflowExecutionCommandDispatchStatus.Duplicate"/> and enqueues nothing.
/// A keyed start that is still in flight enqueues the same start envelope again, which the scheduler work queue absorbs by
/// its work-item identity.
/// </para>
/// <para>
/// A request is a keyed start exactly when its workflow execution id is the one derived from its idempotency key
/// (<see cref="TryGet"/>), so the identity travels on the request's existing fields.
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

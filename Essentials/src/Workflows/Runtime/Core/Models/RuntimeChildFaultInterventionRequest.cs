namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Activity-owned request to keep the child fault a structural child-fault evaluation is processing blocking until an
/// operator intervenes: the named incident stays blocking with a wait-for-intervention outcome and the faulted child's
/// subtree is left as it is, atomically with the parent's deferred continuation.
/// </summary>
public sealed class RuntimeChildFaultInterventionRequest
{
    public RuntimeChildFaultInterventionRequest(
        string incidentId,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        IncidentId = incidentId;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata ?? new Dictionary<string, string>());
    }

    public string IncidentId { get; }
    public string Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

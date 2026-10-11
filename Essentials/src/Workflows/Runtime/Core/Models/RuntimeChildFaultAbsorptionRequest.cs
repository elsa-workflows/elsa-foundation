namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Activity-owned request to absorb the child fault a structural child-fault evaluation is
/// processing (spec 115): the named incident resolves and the faulted child's leftover subtree is
/// reclaimed, atomically with the parent's continuation.
/// </summary>
public sealed class RuntimeChildFaultAbsorptionRequest
{
    public RuntimeChildFaultAbsorptionRequest(
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

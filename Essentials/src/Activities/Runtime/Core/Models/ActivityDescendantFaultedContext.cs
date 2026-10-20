using System.Collections.Frozen;
using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>
/// Runtime callback payload for a descendant's fault delivered past composites that do not handle child faults (spec
/// 258 R-3). Carries the faulted descendant's identity, its incident and committed fault, and the receiver's direct
/// child the fault came through, which is what an absorption cancels.
/// </summary>
public sealed class ActivityDescendantFaultedContext
{
    public ActivityDescendantFaultedContext(
        IActivityExecutionContext parentContext,
        string faultedDescendantActivityExecutionId,
        string faultedDescendantExecutableNodeId,
        string viaChildActivityExecutionId,
        string viaChildExecutableNodeId,
        string? incidentId = null,
        string? faultedDescendantIterationId = null,
        string? faultCode = null,
        string? faultCategory = null)
        : this(parentContext, faultedDescendantActivityExecutionId, faultedDescendantExecutableNodeId,
            viaChildActivityExecutionId, viaChildExecutableNodeId, incidentId, faultedDescendantIterationId,
            faultCode, faultCategory, FrozenDictionary<string, string>.Empty)
    {
    }

    public ActivityDescendantFaultedContext(
        IActivityExecutionContext parentContext,
        string faultedDescendantActivityExecutionId,
        string faultedDescendantExecutableNodeId,
        string viaChildActivityExecutionId,
        string viaChildExecutableNodeId,
        string? incidentId,
        string? faultedDescendantIterationId,
        string? faultCode,
        string? faultCategory,
        IReadOnlyDictionary<string, string> viaChildSchedulingMetadata)
    {
        ArgumentNullException.ThrowIfNull(parentContext);
        ArgumentNullException.ThrowIfNull(viaChildSchedulingMetadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultedDescendantActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(faultedDescendantExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(viaChildActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(viaChildExecutableNodeId);

        if (incidentId is not null && string.IsNullOrWhiteSpace(incidentId))
            throw new ArgumentException("Incident id cannot be blank when provided.", nameof(incidentId));

        ParentContext = parentContext;
        FaultedDescendantActivityExecutionId = faultedDescendantActivityExecutionId;
        FaultedDescendantExecutableNodeId = faultedDescendantExecutableNodeId;
        ViaChildActivityExecutionId = viaChildActivityExecutionId;
        ViaChildExecutableNodeId = viaChildExecutableNodeId;
        IncidentId = incidentId;
        FaultedDescendantIterationId = string.IsNullOrWhiteSpace(faultedDescendantIterationId) ? null : faultedDescendantIterationId;
        FaultCode = string.IsNullOrWhiteSpace(faultCode) ? null : faultCode;
        FaultCategory = string.IsNullOrWhiteSpace(faultCategory) ? null : faultCategory;
        ViaChildSchedulingMetadata = viaChildSchedulingMetadata.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public IActivityExecutionContext ParentContext { get; }
    public string FaultedDescendantActivityExecutionId { get; }
    public string FaultedDescendantExecutableNodeId { get; }

    /// <summary>The receiver's direct child the fault came through; absorbing the fault cancels its subtree.</summary>
    public string ViaChildActivityExecutionId { get; }

    /// <summary>The executable node id of <see cref="ViaChildActivityExecutionId"/>.</summary>
    public string ViaChildExecutableNodeId { get; }

    /// <summary>The id of the incident recorded for the faulted descendant, when known.</summary>
    public string? IncidentId { get; }

    /// <summary>The engine iteration identity the faulted descendant carried, or <c>null</c>.</summary>
    public string? FaultedDescendantIterationId { get; }

    /// <summary>The code of the faulted descendant's committed normalized fault, when known.</summary>
    public string? FaultCode { get; }

    /// <summary>The author-chosen category of the faulted descendant's committed fault, or <c>null</c>.</summary>
    public string? FaultCategory { get; }

    /// <summary>Immutable metadata from the via child's committed scheduling provenance, independent of the delivery command.</summary>
    public IReadOnlyDictionary<string, string> ViaChildSchedulingMetadata { get; }
}

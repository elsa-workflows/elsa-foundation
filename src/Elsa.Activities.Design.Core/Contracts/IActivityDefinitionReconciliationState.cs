namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// Read contract for the operational reconciliation-state sibling of an
/// <see cref="IActivityDefinition"/>. 1:0..1 with the parent: admin-created rows have no
/// sibling; rows produced by a reconciliation source have exactly one. Filled and updated
/// by the reconciler; consumed by audit endpoints, the stale-removal sweep, and the picker
/// (which excludes rows where <see cref="RemovedAt"/> is set).
/// </summary>
public interface IActivityDefinitionReconciliationState
{
    string Id { get; }
    string? TenantId { get; }

    string ActivityDefinitionId { get; }

    /// <summary>Most-recently-seen source-side version identifier (free-form per source).</summary>
    string? SourceVersion { get; }

    /// <summary>Hash of the latest seen projection; computed by <c>IActivityDefinitionHasher</c>.</summary>
    string? ProvisioningHash { get; }

    /// <summary>Updated on each reconciliation pass that observes this row's source-side asset.</summary>
    DateTimeOffset LastSeenAt { get; }

    /// <summary>Updated when the reconciler writes / updates the row's content.</summary>
    DateTimeOffset LastProvisionedAt { get; }

    /// <summary>Identity that ran the most recent provisioning pass for this row.</summary>
    string? LastProvisionedBy { get; }

    /// <summary>True when the source-side asset has not been seen for a configurable duration.</summary>
    bool IsStale { get; }

    /// <summary>Set by the reconciler when the source-side asset is gone. Picker filters on this.</summary>
    DateTimeOffset? RemovedAt { get; }
}

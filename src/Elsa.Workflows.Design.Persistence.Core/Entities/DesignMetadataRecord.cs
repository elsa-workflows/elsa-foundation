using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

/// <summary>
/// Value-object layout record carried inside a <c>WorkflowDefinitionVersionLayout</c> or
/// <c>WorkflowDefinitionDraftLayout</c>. One record per placed activity node, keyed by
/// <c>NodeId</c>. Implements <see cref="IDesignMetadataRecord"/> for Tier-1 read consumers.
/// Unit C FR-006 sub-shape.
/// </summary>
public sealed record DesignMetadataRecord(
    string NodeId,
    double X,
    double Y,
    double? Width = null,
    double? Height = null,
    Dictionary<string, object?>? AdditionalProperties = null
) : IDesignMetadataRecord
{
    IReadOnlyDictionary<string, object?>? IDesignMetadataRecord.AdditionalProperties => AdditionalProperties;
}

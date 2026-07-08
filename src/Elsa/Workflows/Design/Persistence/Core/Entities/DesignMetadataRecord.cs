using System.Text.Json;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

/// <summary>
/// Value-object layout record carried inside a <c>WorkflowDefinitionVersionLayout</c> or
/// <c>WorkflowDefinitionDraftLayout</c>. One record per placed activity node, keyed by
/// <c>NodeId</c>. Implements <see cref="IDesignMetadataRecord"/> for Tier-1 read consumers.
/// Unit C FR-006 sub-shape.
/// </summary>
/// <remarks>
/// <see cref="AdditionalProperties"/> is opaque, Studio-authored per-node layout metadata held as a
/// verbatim <see cref="JsonElement"/> — never a CLR-typed <c>object</c> graph. Keeping it opaque removes
/// the last open-object-polymorphism dependency from serialized design content, so the author's bytes are
/// stored as-received (never key-sorted/canonicalized) and become safe to hash/diff (ADR 0035 D3, extended
/// beyond StateSource to layout).
/// </remarks>
public sealed record DesignMetadataRecord(
    string NodeId,
    double X,
    double Y,
    double? Width = null,
    double? Height = null,
    JsonElement? AdditionalProperties = null
) : IDesignMetadataRecord;

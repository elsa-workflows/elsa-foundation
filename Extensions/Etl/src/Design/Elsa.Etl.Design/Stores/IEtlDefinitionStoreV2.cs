using Elsa.Etl.Core;

namespace Elsa.Etl.Design.Stores;

/// <summary>An additive capability for connector-capable v2 authoring drafts.</summary>
/// <remarks>
/// Implementations share definition identities, revisions and immutable publication history with
/// <see cref="IEtlDefinitionStore"/>. Publication and published reads use that existing artifact-envelope port.
/// A v2 update may explicitly upgrade a v1 draft at its expected revision; it never changes old publications.
/// A read through the wrong typed capability throws <see cref="NotSupportedException"/>.
/// Conflicting writes may return no typed draft when the existing draft uses a different format.
/// </remarks>
public interface IEtlDefinitionStoreV2
{
    /// <summary>Finds a v2 draft in exactly the supplied tenant and persistence partition.</summary>
    ValueTask<EtlDefinitionDraftV2?> FindDraftAsync(EtlScope scope, string definitionId, CancellationToken cancellationToken = default);

    /// <summary>Creates a v2 draft, or returns a conflict when that scoped identity already exists.</summary>
    ValueTask<EtlDefinitionWriteResultV2> CreateDraftAsync(EtlScope scope, string definitionId, EtlPipelineV2 pipeline, CancellationToken cancellationToken = default);

    /// <summary>Replaces or upgrades the draft only at its current revision, retaining its publication sequence.</summary>
    ValueTask<EtlDefinitionWriteResultV2> UpdateDraftAsync(EtlScope scope, string definitionId, long expectedRevision, EtlPipelineV2 pipeline, CancellationToken cancellationToken = default);
}

/// <summary>A connector-capable draft with a revision independent of immutable publication versions.</summary>
public sealed record EtlDefinitionDraftV2(string DefinitionId, long Revision, int LastPublishedVersion, EtlPipelineV2 Pipeline, DateTimeOffset UpdatedAt);

/// <summary>The result of a v2 draft create or optimistic update.</summary>
public sealed record EtlDefinitionWriteResultV2(EtlDefinitionWriteStatus Status, EtlDefinitionDraftV2? Draft);

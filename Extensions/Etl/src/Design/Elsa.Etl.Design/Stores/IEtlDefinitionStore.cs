using Elsa.Etl.Core;

namespace Elsa.Etl.Design.Stores;

/// <summary>Reads and writes ETL authoring drafts and immutable published artifacts.</summary>
/// <remarks>
/// Every operation is scoped by the tenant and persistence partition that the caller derived from its trusted
/// execution context. Runtime execution consumes only published artifacts; it never reads drafts.
/// </remarks>
public interface IEtlDefinitionStore
{
    /// <summary>Finds a draft in exactly the supplied tenant and persistence partition.</summary>
    ValueTask<EtlDefinitionDraft?> FindDraftAsync(EtlScope scope, string definitionId, CancellationToken cancellationToken = default);

    /// <summary>Creates a new draft at revision 1, or returns a conflict if its identity is already present.</summary>
    ValueTask<EtlDefinitionWriteResult> CreateDraftAsync(EtlScope scope, string definitionId, EtlPipeline pipeline, CancellationToken cancellationToken = default);

    /// <summary>Replaces the draft only if <paramref name="expectedRevision"/> is current.</summary>
    ValueTask<EtlDefinitionWriteResult> UpdateDraftAsync(EtlScope scope, string definitionId, long expectedRevision, EtlPipeline pipeline, CancellationToken cancellationToken = default);

    /// <summary>Finds one immutable published artifact in exactly the supplied tenant and persistence partition.</summary>
    ValueTask<EtlArtifact?> FindPublishedAsync(EtlScope scope, string definitionId, int definitionVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the next version compiled from the current draft revision. Replaying the identical artifact at an
    /// already published version returns <see cref="EtlPublicationStatus.Replay"/>, even if the draft has since changed.
    /// </summary>
    ValueTask<EtlPublicationResult> PublishAsync(EtlScope scope, string definitionId, long expectedRevision, EtlArtifact artifact, CancellationToken cancellationToken = default);
}

/// <summary>A mutable source draft, separate from every published runnable artifact.</summary>
public sealed record EtlDefinitionDraft(
    string DefinitionId,
    long Revision,
    int LastPublishedVersion,
    EtlPipeline Pipeline,
    DateTimeOffset UpdatedAt);

/// <summary>The outcome of a draft create or optimistic update.</summary>
public sealed record EtlDefinitionWriteResult(EtlDefinitionWriteStatus Status, EtlDefinitionDraft? Draft);

/// <summary>Draft write outcomes.</summary>
public enum EtlDefinitionWriteStatus
{
    Created,
    Updated,
    Conflict,
    NotFound
}

/// <summary>The outcome of publishing an immutable runnable artifact.</summary>
public sealed record EtlPublicationResult(EtlPublicationStatus Status, EtlArtifact? Artifact);

/// <summary>Publication outcomes.</summary>
public enum EtlPublicationStatus
{
    Published,
    Replay,
    Conflict,
    NotFound,
    InvalidArtifact
}

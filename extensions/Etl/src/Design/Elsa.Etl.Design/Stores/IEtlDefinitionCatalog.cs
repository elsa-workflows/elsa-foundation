using Elsa.Etl.Core;

namespace Elsa.Etl.Design.Stores;

/// <summary>Reads bounded metadata pages for ETL definitions and their immutable published versions.</summary>
/// <remarks>
/// Catalog reads expose metadata only. Draft pipeline JSON and published artifact envelopes remain behind
/// <see cref="IEtlDefinitionStore"/> so listing definitions never loads runnable content.
/// </remarks>
public interface IEtlDefinitionCatalog
{
    /// <summary>Lists definitions after the supplied opaque identifier, ordered by their stable definition key.</summary>
    /// <param name="scope">The exact tenant and persistence partition to read.</param>
    /// <param name="afterDefinitionId">An exclusive cursor. The definition need not still exist.</param>
    /// <param name="take">The maximum number of rows to return; values from 1 through 1000 are accepted.</param>
    /// <param name="cancellationToken">Cancels the database read.</param>
    ValueTask<IReadOnlyList<EtlDefinitionSummary>> ListDefinitionsAsync(
        EtlScope scope,
        string? afterDefinitionId,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Lists published version metadata in ascending version order within an exclusive cursor and fixed ceiling.</summary>
    /// <param name="scope">The exact tenant and persistence partition to read.</param>
    /// <param name="definitionId">The opaque definition identifier.</param>
    /// <param name="afterVersion">The exclusive lower version bound.</param>
    /// <param name="throughVersion">The inclusive upper version ceiling, captured by the caller for pagination.</param>
    /// <param name="take">The maximum number of rows to return; values from 1 through 1000 are accepted.</param>
    /// <param name="cancellationToken">Cancels the database read.</param>
    ValueTask<IReadOnlyList<EtlPublishedVersionSummary>> ListVersionsAsync(
        EtlScope scope,
        string definitionId,
        int afterVersion,
        int throughVersion,
        int take,
        CancellationToken cancellationToken = default);
}

/// <summary>Metadata for one mutable ETL definition draft.</summary>
public sealed record EtlDefinitionSummary(string DefinitionId, long Revision, int LastPublishedVersion, DateTimeOffset UpdatedAt)
{
    /// <summary>The pipeline document format, used to select the matching draft reader.</summary>
    public int FormatVersion { get; init; } = EtlArtifact.CurrentFormatVersion;
}

/// <summary>Metadata for one immutable, published ETL artifact version.</summary>
public sealed record EtlPublishedVersionSummary(string DefinitionId, int DefinitionVersion, string ContentHash, DateTimeOffset PublishedAt)
{
    /// <summary>The published artifact format, independent of its definition version.</summary>
    public int FormatVersion { get; init; } = EtlArtifact.CurrentFormatVersion;
}

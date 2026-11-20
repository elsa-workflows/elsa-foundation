using Elsa.Etl.Core;
using Elsa.Etl.Design.Stores;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

/// <summary>Metadata-only listing for scoped ETL definitions and publications.</summary>
internal sealed class EfEtlDefinitionCatalog(EtlDesignDbContext dbContext) : IEtlDefinitionCatalog
{
    private const int MaximumPageSize = 1000;

    public async ValueTask<IReadOnlyList<EtlDefinitionSummary>> ListDefinitionsAsync(
        EtlScope scope,
        string? afterDefinitionId,
        int take,
        CancellationToken cancellationToken = default)
    {
        ValidateTake(take);
        var scopeIdentity = EtlDefinitionRecordCodec.Scope(scope);
        var afterKey = afterDefinitionId is null ? null : EtlDefinitionRecordCodec.Definition(afterDefinitionId).Key;
        var query = dbContext.Drafts.AsNoTracking().Where(row => row.ScopeKey == scopeIdentity.Key);
        if (afterKey is not null)
            // The key column's binary collation makes this seek, and the order below, ordinal on every engine.
            query = query.Where(row => row.DefinitionKey.CompareTo(afterKey) > 0);

        var rows = await EtlDesignReadRetry.RunAsync(dbContext, () => query
            .OrderBy(row => row.DefinitionKey)
            .Take(take)
            .Select(row => new DraftMetadata(
                row.ScopeKey,
                row.TenantId,
                row.PersistencePartition,
                row.DefinitionKey,
                row.DefinitionId,
                row.Revision,
                row.LastPublishedVersion,
                row.SchemaVersion,
                row.UpdatedAtUtcTicks,
                row.FormatVersion))
            .ToListAsync(cancellationToken), cancellationToken);

        return rows.Select(row => EtlDefinitionRecordCodec.ReadDraftSummary(
                row.ScopeKey,
                row.TenantId,
                row.PersistencePartition,
                row.DefinitionKey,
                row.DefinitionId,
                row.Revision,
                row.LastPublishedVersion,
                row.SchemaVersion,
                row.UpdatedAtUtcTicks,
                scope, row.FormatVersion))
            .ToArray();
    }

    public async ValueTask<IReadOnlyList<EtlPublishedVersionSummary>> ListVersionsAsync(
        EtlScope scope,
        string definitionId,
        int afterVersion,
        int throughVersion,
        int take,
        CancellationToken cancellationToken = default)
    {
        ValidateTake(take);
        ArgumentOutOfRangeException.ThrowIfNegative(afterVersion);
        ArgumentOutOfRangeException.ThrowIfNegative(throughVersion);
        if (afterVersion > throughVersion)
            throw new ArgumentOutOfRangeException(nameof(afterVersion), "The exclusive version cursor cannot exceed the inclusive ceiling.");

        var scopeIdentity = EtlDefinitionRecordCodec.Scope(scope);
        var definitionIdentity = EtlDefinitionRecordCodec.Definition(definitionId);
        if (throughVersion == 0)
            return Array.Empty<EtlPublishedVersionSummary>();

        var rows = await EtlDesignReadRetry.RunAsync(dbContext, () => dbContext.PublishedArtifacts.AsNoTracking()
            .Where(row => row.ScopeKey == scopeIdentity.Key &&
                          row.DefinitionKey == definitionIdentity.Key &&
                          row.DefinitionVersion > afterVersion &&
                          row.DefinitionVersion <= throughVersion)
            .OrderBy(row => row.DefinitionVersion)
            .Take(take)
            .Select(row => new PublishedMetadata(
                row.ScopeKey,
                row.TenantId,
                row.PersistencePartition,
                row.DefinitionKey,
                row.DefinitionId,
                row.DefinitionVersion,
                row.SchemaVersion,
                row.ArtifactEnvelopeHash,
                row.PipelineHash,
                row.PublishedAtUtcTicks,
                row.FormatVersion))
            .ToListAsync(cancellationToken), cancellationToken);

        return rows.Select(row => EtlDefinitionRecordCodec.ReadPublishedVersionSummary(
                row.ScopeKey,
                row.TenantId,
                row.PersistencePartition,
                row.DefinitionKey,
                row.DefinitionId,
                row.DefinitionVersion,
                row.SchemaVersion,
                row.ArtifactEnvelopeHash,
                row.ContentHash,
                row.PublishedAtUtcTicks,
                scope,
                definitionId, row.FormatVersion))
            .ToArray();
    }

    private static void ValidateTake(int take)
    {
        if (take is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(take), take, $"The ETL catalog page size must be from 1 through {MaximumPageSize}.");
    }

    private sealed record DraftMetadata(
        string ScopeKey,
        string? TenantId,
        string PersistencePartition,
        string DefinitionKey,
        string DefinitionId,
        long Revision,
        int LastPublishedVersion,
        string SchemaVersion,
        long UpdatedAtUtcTicks,
        int FormatVersion);

    private sealed record PublishedMetadata(
        string ScopeKey,
        string? TenantId,
        string PersistencePartition,
        string DefinitionKey,
        string DefinitionId,
        int DefinitionVersion,
        string SchemaVersion,
        string ArtifactEnvelopeHash,
        string ContentHash,
        long PublishedAtUtcTicks,
        int FormatVersion);
}

using System.Data;
using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

/// <summary>EF implementation of tenant- and partition-scoped ETL design persistence.</summary>
internal sealed class EfEtlDefinitionStore(
    EtlDesignDbContext dbContext,
    IEtlArtifactCompiler artifactCompiler,
    IEtlArtifactCompilerV2? artifactCompilerV2 = null) : IEtlDefinitionStore, IEtlDefinitionStoreV2
{
    public async ValueTask<EtlDefinitionDraft?> FindDraftAsync(
        EtlScope scope,
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        var identity = Identities(scope, definitionId);
        var row = await FindDraftRowAsync(identity, cancellationToken);
        return row is null ? null : EtlDefinitionRecordCodec.ReadDraft(row, scope, definitionId);
    }

    async ValueTask<EtlDefinitionDraftV2?> IEtlDefinitionStoreV2.FindDraftAsync(
        EtlScope scope, string definitionId, CancellationToken cancellationToken)
    {
        var row = await FindDraftRowAsync(Identities(scope, definitionId), cancellationToken);
        return row is null ? null : EtlDefinitionRecordCodec.ReadDraftV2(row, scope, definitionId);
    }

    public async ValueTask<EtlDefinitionWriteResult> CreateDraftAsync(
        EtlScope scope, string definitionId, EtlPipeline pipeline, CancellationToken cancellationToken = default)
    {
        var result = await CreateDraftCoreAsync(scope, definitionId, EtlArtifact.CurrentFormatVersion,
            EtlDefinitionRecordCodec.WritePipeline(pipeline), cancellationToken);
        return new(result.Status, ProjectV1(result.Draft, scope, definitionId));
    }

    public async ValueTask<EtlDefinitionWriteResultV2> CreateDraftAsync(
        EtlScope scope, string definitionId, EtlPipelineV2 pipeline, CancellationToken cancellationToken = default)
    {
        var result = await CreateDraftCoreAsync(scope, definitionId, EtlArtifactV2Json.FormatVersion,
            EtlDefinitionRecordCodec.WritePipeline(pipeline), cancellationToken);
        return new(result.Status, ProjectV2(result.Draft, scope, definitionId));
    }

    public async ValueTask<EtlDefinitionWriteResult> UpdateDraftAsync(
        EtlScope scope, string definitionId, long expectedRevision, EtlPipeline pipeline, CancellationToken cancellationToken = default)
    {
        var result = await UpdateDraftCoreAsync(scope, definitionId, expectedRevision, EtlArtifact.CurrentFormatVersion,
            EtlDefinitionRecordCodec.WritePipeline(pipeline), cancellationToken);
        return new(result.Status, ProjectV1(result.Draft, scope, definitionId));
    }

    public async ValueTask<EtlDefinitionWriteResultV2> UpdateDraftAsync(
        EtlScope scope, string definitionId, long expectedRevision, EtlPipelineV2 pipeline, CancellationToken cancellationToken = default)
    {
        var result = await UpdateDraftCoreAsync(scope, definitionId, expectedRevision, EtlArtifactV2Json.FormatVersion,
            EtlDefinitionRecordCodec.WritePipeline(pipeline), cancellationToken);
        return new(result.Status, ProjectV2(result.Draft, scope, definitionId));
    }

    private async ValueTask<DraftWrite> CreateDraftCoreAsync(
        EtlScope scope, string definitionId, int formatVersion, string pipelineJson, CancellationToken cancellationToken)
    {
        var identity = Identities(scope, definitionId);
        var row = DraftRecord(identity, definitionId, revision: 1, lastPublishedVersion: 0, pipelineJson, DateTimeOffset.UtcNow.UtcTicks);
        row.FormatVersion = formatVersion;
        dbContext.Drafts.Add(row);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.Entry(row).State = EntityState.Detached;
            return new(EtlDefinitionWriteStatus.Created, row);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(row).State = EntityState.Detached;
            var existing = await FindDraftRowAsync(identity, cancellationToken);
            if (existing is null)
                throw;
            _ = EtlDefinitionRecordCodec.ReadDraftContent(existing, scope, definitionId);
            return new(EtlDefinitionWriteStatus.Conflict, existing);
        }
    }

    private async ValueTask<DraftWrite> UpdateDraftCoreAsync(
        EtlScope scope, string definitionId, long expectedRevision, int formatVersion, string pipelineJson, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedRevision);
        var identity = Identities(scope, definitionId);
        if (expectedRevision != long.MaxValue)
        {
            var pipelineHash = EtlDefinitionRecordCodec.HashPipeline(pipelineJson);
            var updatedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
            // A v2 update can explicitly upgrade a v1 draft. Old v1 clients cannot downgrade a v2 draft.
            var changed = await DraftQuery(identity)
                .Where(row => row.Revision == expectedRevision &&
                    (row.FormatVersion == EtlArtifact.CurrentFormatVersion ||
                     formatVersion == EtlArtifactV2Json.FormatVersion && row.FormatVersion == EtlArtifactV2Json.FormatVersion))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.PipelineJson, pipelineJson)
                    .SetProperty(row => row.PipelineHash, pipelineHash)
                    .SetProperty(row => row.FormatVersion, formatVersion)
                    .SetProperty(row => row.SchemaVersion, EtlDesignEfModule.SchemaVersion)
                    .SetProperty(row => row.Revision, row => row.Revision + 1)
                    .SetProperty(row => row.UpdatedAtUtcTicks, updatedAtUtcTicks), cancellationToken);
            if (changed == 1)
            {
                var updated = await FindDraftRowAsync(identity, cancellationToken)
                    ?? throw new InvalidDataException("The ETL draft disappeared after its update committed.");
                _ = EtlDefinitionRecordCodec.ReadDraftContent(updated, scope, definitionId);
                return new(EtlDefinitionWriteStatus.Updated, updated);
            }
        }
        var current = await FindDraftRowAsync(identity, cancellationToken);
        if (current is null)
            return new(EtlDefinitionWriteStatus.NotFound, null);
        _ = EtlDefinitionRecordCodec.ReadDraftContent(current, scope, definitionId);
        return new(EtlDefinitionWriteStatus.Conflict, current);
    }

    private static EtlDefinitionDraft? ProjectV1(EtlDefinitionDraftRecord? row, EtlScope scope, string definitionId) =>
        row is { FormatVersion: EtlArtifact.CurrentFormatVersion } ? EtlDefinitionRecordCodec.ReadDraft(row, scope, definitionId) : null;

    private static EtlDefinitionDraftV2? ProjectV2(EtlDefinitionDraftRecord? row, EtlScope scope, string definitionId) =>
        row is { FormatVersion: EtlArtifactV2Json.FormatVersion } ? EtlDefinitionRecordCodec.ReadDraftV2(row, scope, definitionId) : null;

    private sealed record DraftWrite(EtlDefinitionWriteStatus Status, EtlDefinitionDraftRecord? Draft);

    public async ValueTask<EtlArtifact?> FindPublishedAsync(
        EtlScope scope,
        string definitionId,
        int definitionVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(definitionVersion);
        var identity = Identities(scope, definitionId);
        var row = await FindPublishedRowAsync(identity, definitionVersion, cancellationToken);
        return row is null ? null : EtlDefinitionRecordCodec.ReadPublished(row, scope, definitionId);
    }

    public async ValueTask<EtlPublicationResult> PublishAsync(
        EtlScope scope,
        string definitionId,
        long expectedRevision,
        EtlArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedRevision);
        var identity = Identities(scope, definitionId);
        EtlDefinitionRecordCodec.ValidatedArtifact validated;
        try
        {
            validated = EtlDefinitionRecordCodec.ValidateArtifact(definitionId, artifact);
        }
        catch (ArgumentException)
        {
            return new(EtlPublicationStatus.InvalidArtifact, null);
        }

        var compilation = validated.Pipeline switch
        {
            EtlPipeline v1 => artifactCompiler.Compile(definitionId, artifact.DefinitionVersion, v1),
            EtlPipelineV2 v2 => artifactCompilerV2?.Compile(definitionId, artifact.DefinitionVersion, v2),
            _ => null
        };
        if (compilation is null || compilation.Diagnostics.Count != 0 || compilation.Artifact is not { } recompiled)
            return new(EtlPublicationStatus.InvalidArtifact, null);

        var recompiledEnvelope = EtlDefinitionRecordCodec.ValidateArtifact(definitionId, recompiled);
        if (!StringComparer.Ordinal.Equals(validated.ArtifactJson, recompiledEnvelope.ArtifactJson))
            return new(EtlPublicationStatus.InvalidArtifact, null);

        var existing = await FindPublishedRowAsync(identity, artifact.DefinitionVersion, cancellationToken);
        if (existing is not null)
            return ExistingPublicationResult(existing, scope, definitionId, validated);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            // Recheck after taking the transaction: retries of the same version converge, including after a later draft edit.
            existing = await FindPublishedRowAsync(identity, artifact.DefinitionVersion, cancellationToken);
            if (existing is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return ExistingPublicationResult(existing, scope, definitionId, validated);
            }

            var draft = await FindDraftRowAsync(identity, cancellationToken);
            if (draft is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(EtlPublicationStatus.NotFound, null);
            }

            _ = EtlDefinitionRecordCodec.ReadDraftContent(draft, scope, definitionId);
            var current = draft;
            if (current.Revision != expectedRevision)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(EtlPublicationStatus.Conflict, null);
            }

            if (current.LastPublishedVersion == int.MaxValue || artifact.DefinitionVersion != current.LastPublishedVersion + 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(EtlPublicationStatus.Conflict, null);
            }

            if (draft.FormatVersion != artifact.FormatVersion ||
                !StringComparer.Ordinal.Equals(validated.PipelineJson, draft.PipelineJson) ||
                !StringComparer.Ordinal.Equals(validated.PipelineHash, draft.PipelineHash))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new(EtlPublicationStatus.InvalidArtifact, null);
            }

            var versionAdvanced = await DraftQuery(identity)
                .Where(row => row.Revision == expectedRevision && row.LastPublishedVersion == current.LastPublishedVersion)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.LastPublishedVersion, artifact.DefinitionVersion), cancellationToken);
            if (versionAdvanced != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return await ResolvePublishRaceAsync(identity, scope, definitionId, validated, cancellationToken);
            }

            dbContext.PublishedArtifacts.Add(PublishedRecord(identity, definitionId, validated, DateTimeOffset.UtcNow.UtcTicks));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(EtlPublicationStatus.Published, artifact);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var published = await FindPublishedRowAsync(identity, artifact.DefinitionVersion, cancellationToken);
            if (published is not null)
                return ExistingPublicationResult(published, scope, definitionId, validated);
            var current = await FindDraftRowAsync(identity, cancellationToken);
            if (current is null)
                return new(EtlPublicationStatus.NotFound, null);
            _ = EtlDefinitionRecordCodec.ReadDraftContent(current, scope, definitionId);
            if (current.Revision != expectedRevision || current.LastPublishedVersion >= artifact.DefinitionVersion)
                return new(EtlPublicationStatus.Conflict, null);
            // A persistence failure is not evidence of a competing writer. Keep it visible so
            // the caller retains retry responsibility instead of treating it as stale input.
            throw;
        }
    }

    private IQueryable<EtlDefinitionDraftRecord> DraftQuery(IdentitySet identity) =>
        dbContext.Drafts.AsNoTracking().Where(row => row.ScopeKey == identity.Scope.Key && row.DefinitionKey == identity.Definition.Key);

    private async Task<EtlDefinitionDraftRecord?> FindDraftRowAsync(IdentitySet identity, CancellationToken cancellationToken) =>
        await DraftQuery(identity).SingleOrDefaultAsync(cancellationToken);

    private async Task<EtlPublishedArtifactRecord?> FindPublishedRowAsync(
        IdentitySet identity,
        int definitionVersion,
        CancellationToken cancellationToken) =>
        await dbContext.PublishedArtifacts.AsNoTracking().SingleOrDefaultAsync(
            row => row.ScopeKey == identity.Scope.Key &&
                   row.DefinitionKey == identity.Definition.Key &&
                   row.DefinitionVersion == definitionVersion,
            cancellationToken);

    private async Task<EtlPublicationResult> ResolvePublishRaceAsync(
        IdentitySet identity,
        EtlScope scope,
        string definitionId,
        EtlDefinitionRecordCodec.ValidatedArtifact validated,
        CancellationToken cancellationToken)
    {
        var published = await FindPublishedRowAsync(identity, validated.Artifact.DefinitionVersion, cancellationToken);
        if (published is not null)
            return ExistingPublicationResult(published, scope, definitionId, validated);

        var draft = await FindDraftRowAsync(identity, cancellationToken);
        if (draft is not null)
            _ = EtlDefinitionRecordCodec.ReadDraftContent(draft, scope, definitionId);
        return draft is null
            ? new(EtlPublicationStatus.NotFound, null)
            : new(EtlPublicationStatus.Conflict, null);
    }

    private static EtlPublicationResult ExistingPublicationResult(
        EtlPublishedArtifactRecord row,
        EtlScope scope,
        string definitionId,
        EtlDefinitionRecordCodec.ValidatedArtifact validated)
    {
        var existing = EtlDefinitionRecordCodec.ReadPublished(row, scope, definitionId);
        return StringComparer.Ordinal.Equals(row.ArtifactEnvelopeHash, validated.ArtifactEnvelopeHash) &&
               StringComparer.Ordinal.Equals(row.ArtifactJson, validated.ArtifactJson)
            ? new(EtlPublicationStatus.Replay, existing)
            : new(EtlPublicationStatus.Conflict, null);
    }

    private static EtlDefinitionDraftRecord DraftRecord(
        IdentitySet identity,
        string definitionId,
        long revision,
        int lastPublishedVersion,
        string pipelineJson,
        long updatedAtUtcTicks) => new()
    {
        ScopeKey = identity.Scope.Key,
        TenantId = identity.Scope.TenantId,
        PersistencePartition = identity.Scope.PersistencePartition,
        DefinitionKey = identity.Definition.Key,
        DefinitionId = identity.Definition.DefinitionId,
        Revision = revision,
        LastPublishedVersion = lastPublishedVersion,
        SchemaVersion = EtlDesignEfModule.SchemaVersion,
        PipelineJson = pipelineJson,
        PipelineHash = EtlDefinitionRecordCodec.HashPipeline(pipelineJson),
        UpdatedAtUtcTicks = updatedAtUtcTicks
    };

    private static EtlPublishedArtifactRecord PublishedRecord(
        IdentitySet identity,
        string definitionId,
        EtlDefinitionRecordCodec.ValidatedArtifact validated,
        long publishedAtUtcTicks) => new()
    {
        ScopeKey = identity.Scope.Key,
        TenantId = identity.Scope.TenantId,
        PersistencePartition = identity.Scope.PersistencePartition,
        DefinitionKey = identity.Definition.Key,
        DefinitionId = identity.Definition.DefinitionId,
        DefinitionVersion = validated.Artifact.DefinitionVersion,
        FormatVersion = validated.Artifact.FormatVersion,
        SchemaVersion = EtlDesignEfModule.SchemaVersion,
        ArtifactJson = validated.ArtifactJson,
        ArtifactEnvelopeHash = validated.ArtifactEnvelopeHash,
        PipelineHash = validated.PipelineHash,
        PublishedAtUtcTicks = publishedAtUtcTicks
    };

    private static IdentitySet Identities(EtlScope scope, string definitionId) =>
        new(EtlDefinitionRecordCodec.Scope(scope), EtlDefinitionRecordCodec.Definition(definitionId));

    private sealed record IdentitySet(
        EtlDefinitionRecordCodec.ScopeIdentity Scope,
        EtlDefinitionRecordCodec.DefinitionIdentity Definition);
}

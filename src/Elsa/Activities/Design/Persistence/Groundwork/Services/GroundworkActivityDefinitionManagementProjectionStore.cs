using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Querying;
using Groundwork.Documents.Store;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>Provider-bound reader for stable temporal Activity Definition management summaries.</summary>
public sealed class GroundworkActivityDefinitionManagementProjectionStore(
    IDocumentStore store,
    IBoundedDocumentStore boundedStore,
    IPersistenceAccessContextAccessor accessContextAccessor) : IActivityDefinitionManagementProjectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = GroundworkActivitiesDesignJson.Options;

    public async Task<ActivityManagementSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind,
            ActivityManagementProjectionWatermark.CurrentId,
            cancellationToken);
        if (envelope is null)
            return new ActivityManagementSnapshot(0, DateTimeOffset.UnixEpoch);
        var watermark = Deserialize<ActivityManagementProjectionWatermark>(
            envelope,
            ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind);
        return new ActivityManagementSnapshot(watermark.Sequence, watermark.AdvancedAt);
    }

    public async Task<ActivityManagementProjectionPage<ActivityDefinitionManagementProjectionRevision>> ReadDefinitionsAsync(
        ActivityManagementProjectionPageQuery query,
        CancellationToken cancellationToken = default)
    {
        Validate(query);
        accessContextAccessor.Current.EnsureTenantScope(query.TenantId);
        var snapshot = await ResolveSnapshotAsync(query.SnapshotSequence, cancellationToken);
        var clauses = CommonClauses(query, snapshot.Sequence);
        if (query.Authority is { } authority)
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ManagementAuthorityField,
                Convert.ToInt32(authority, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))));
        }
        if (!string.IsNullOrWhiteSpace(query.ProviderKey))
        {
            clauses.Add(DocumentQueryClause.AnyOf(
                DocumentQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.ManagementHeadProviderField,
                    query.ProviderKey.Trim()),
                DocumentQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.ManagementRecommendationProviderField,
                    query.ProviderKey.Trim())));
        }
        return await ReadPageAsync<ActivityDefinitionManagementProjectionRevision>(
            ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind,
            ActivitiesDesignStorageManifest.ManagementDefinitionsQuery,
            query,
            snapshot,
            clauses,
            cancellationToken);
    }

    public async Task<ActivityDefinitionManagementProjectionRevision?> FindDefinitionAsync(
        string definitionId,
        string? tenantId,
        long? snapshotSequence = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        accessContextAccessor.Current.EnsureTenantScope(tenantId);
        var snapshot = await ResolveSnapshotAsync(snapshotSequence, cancellationToken);
        var query = new ActivityManagementProjectionPageQuery(tenantId, snapshot.Sequence, 0, 1);
        var clauses = CommonClauses(query, snapshot.Sequence);
        clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.Equal(
            ActivitiesDesignStorageManifest.DefinitionIdField,
            definitionId)));
        var envelope = await boundedStore.FirstOrDefaultAsync(
            new DocumentQuery(
                ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind,
                ActivitiesDesignStorageManifest.ManagementDefinitionsQuery,
                clauses,
                [
                    new DocumentQueryOrder(ActivitiesDesignStorageManifest.ManagementSortField),
                    new DocumentQueryOrder(ActivitiesDesignStorageManifest.ManagementValidFromField)
                ],
                take: 1)
                .Select(global::Groundwork.Core.PhysicalStorage.BoundedQueryResultOperation.First),
            cancellationToken);
        return envelope is null
            ? null
            : Deserialize<ActivityDefinitionManagementProjectionRevision>(
                envelope,
                ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind);
    }

    public async Task<ActivityManagementProjectionPage<ActivityDefinitionDraftManagementProjectionRevision>> ReadDraftsAsync(
        string definitionId,
        ActivityManagementProjectionPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        Validate(query);
        accessContextAccessor.Current.EnsureTenantScope(query.TenantId);
        var snapshot = await ResolveSnapshotAsync(query.SnapshotSequence, cancellationToken);
        var clauses = CommonClauses(query, snapshot.Sequence);
        clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.Equal(
            ActivitiesDesignStorageManifest.DefinitionIdField,
            definitionId)));
        if (query.DraftStatus is { } status)
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ManagementDraftStatusField,
                Convert.ToInt32(status, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))));
        }
        AddProviderClause(clauses, query.ProviderKey);
        return await ReadPageAsync<ActivityDefinitionDraftManagementProjectionRevision>(
            ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind,
            ActivitiesDesignStorageManifest.ManagementDraftsQuery,
            query,
            snapshot,
            clauses,
            cancellationToken);
    }

    public async Task<ActivityManagementProjectionPage<ActivityDefinitionVersionManagementProjectionRevision>> ReadVersionsAsync(
        string definitionId,
        ActivityManagementProjectionPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        Validate(query);
        accessContextAccessor.Current.EnsureTenantScope(query.TenantId);
        var snapshot = await ResolveSnapshotAsync(query.SnapshotSequence, cancellationToken);
        var clauses = CommonClauses(query, snapshot.Sequence);
        clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.Equal(
            ActivitiesDesignStorageManifest.DefinitionIdField,
            definitionId)));
        if (query.VersionLifecycle is { } lifecycle)
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ManagementVersionLifecycleField,
                Convert.ToInt32(lifecycle, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))));
        }
        AddProviderClause(clauses, query.ProviderKey);
        return await ReadPageAsync<ActivityDefinitionVersionManagementProjectionRevision>(
            ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind,
            ActivitiesDesignStorageManifest.ManagementVersionsQuery,
            query,
            snapshot,
            clauses,
            cancellationToken);
    }

    private async Task<ActivityManagementProjectionPage<T>> ReadPageAsync<T>(
        string kind,
        string queryIdentity,
        ActivityManagementProjectionPageQuery query,
        ActivityManagementSnapshot snapshot,
        IReadOnlyList<DocumentQueryClause> clauses,
        CancellationToken cancellationToken)
        where T : ActivityManagementProjectionRevision
    {
        var result = await boundedStore.QueryAsync(
            new DocumentQuery(
                kind,
                queryIdentity,
                clauses,
                [
                    new DocumentQueryOrder(ActivitiesDesignStorageManifest.ManagementSortField),
                    new DocumentQueryOrder(ActivitiesDesignStorageManifest.ManagementValidFromField)
                ],
                query.Offset,
                query.Limit),
            cancellationToken);
        var items = result.Documents.Select(x => Deserialize<T>(x, kind)).ToArray();
        var nextOffset = checked(query.Offset + items.Length);
        return new ActivityManagementProjectionPage<T>(
            items,
            nextOffset < result.TotalCount ? nextOffset : null,
            result.TotalCount,
            snapshot);
    }

    private async Task<ActivityManagementSnapshot> ResolveSnapshotAsync(
        long? requestedSequence,
        CancellationToken cancellationToken)
    {
        var watermarkEnvelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind,
            ActivityManagementProjectionWatermark.CurrentId,
            cancellationToken);
        if (watermarkEnvelope is null)
        {
            if (requestedSequence is null or 0)
                return new ActivityManagementSnapshot(0, DateTimeOffset.UnixEpoch);
            throw new ActivityManagementSnapshotExpiredException(requestedSequence.Value);
        }

        var watermark = Deserialize<ActivityManagementProjectionWatermark>(
            watermarkEnvelope,
            ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind);
        var sequence = requestedSequence ?? watermark.Sequence;
        if (sequence == 0)
            return new ActivityManagementSnapshot(0, DateTimeOffset.UnixEpoch);
        if (sequence < watermark.RetainedFromSequence || sequence > watermark.Sequence)
            throw new ActivityManagementSnapshotExpiredException(sequence);
        if (sequence == watermark.Sequence)
            return new ActivityManagementSnapshot(sequence, watermark.AdvancedAt);

        var snapshotEnvelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityManagementProjectionSnapshotDocumentKind,
            GroundworkActivityManagementProjectionWriter.SequenceKey(sequence),
            cancellationToken);
        if (snapshotEnvelope is null)
            throw new ActivityManagementSnapshotExpiredException(sequence);
        var snapshot = Deserialize<ActivityManagementProjectionSnapshot>(
            snapshotEnvelope,
            ActivitiesDesignStorageManifest.ActivityManagementProjectionSnapshotDocumentKind);
        return new ActivityManagementSnapshot(snapshot.Sequence, snapshot.AsOf);
    }

    private static List<DocumentQueryClause> CommonClauses(
        ActivityManagementProjectionPageQuery query,
        long sequence)
    {
        var snapshotKey = GroundworkActivityManagementProjectionWriter.SequenceKey(sequence);
        var visibility = query.TenantId is null
            ? DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ManagementVisibilityField,
                "*"))
            : DocumentQueryClause.AnyOf(
                DocumentQueryComparison.Equal(ActivitiesDesignStorageManifest.ManagementVisibilityField, "*"),
                DocumentQueryComparison.Equal(ActivitiesDesignStorageManifest.ManagementVisibilityField, query.TenantId));
        var clauses = new List<DocumentQueryClause>
        {
            visibility,
            DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                ActivitiesDesignStorageManifest.ManagementValidFromField,
                snapshotKey)),
            DocumentQueryClause.Of(DocumentQueryComparison.GreaterThan(
                ActivitiesDesignStorageManifest.ManagementValidToField,
                snapshotKey))
        };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.Contains(
                ActivitiesDesignStorageManifest.ManagementSearchField,
                query.Search.Trim().ToUpperInvariant())));
        }
        return clauses;
    }

    private static void AddProviderClause(ICollection<DocumentQueryClause> clauses, string? providerKey)
    {
        if (!string.IsNullOrWhiteSpace(providerKey))
        {
            clauses.Add(DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ManagementProviderField,
                providerKey.Trim())));
        }
    }

    private static void Validate(ActivityManagementProjectionPageQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(query.Offset));
        if (query.Limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(query.Limit));
    }

    private static T Deserialize<T>(DocumentEnvelope envelope, string kind)
        where T : Elsa.Primitives.Entities.Entity
    {
        var document = JsonSerializer.Deserialize<GroundworkDocument<T>>(envelope.ContentJson, JsonOptions);
        return document?.Entity ?? throw new InvalidOperationException(
            $"Document '{envelope.Id}' of kind '{kind}' could not be deserialized as {typeof(T).Name}.");
    }
}

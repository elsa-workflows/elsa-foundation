using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Locking.Core;
using Elsa.Persistence.Groundwork.Querying;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

public sealed record ActivityManagementDefinitionChange(
    ActivityDefinition Definition,
    ActivityDefinitionAuthoringState Authoring);

public sealed record ActivityManagementProjectionMutation(
    DateTimeOffset ChangedAt,
    IReadOnlyList<ActivityManagementDefinitionChange> Definitions,
    IReadOnlyList<ActivityDefinitionDraft> Drafts,
    IReadOnlyList<ActivityDefinitionVersionPublication> Versions);

/// <summary>
/// Prepared projection writes plus the scope-safe lock that fences sequence allocation. Dispose only
/// after the caller's authoritative and projection requests have committed in one document unit of work.
/// </summary>
public sealed class GroundworkActivityManagementProjectionUpdate(
    IDocumentStore store,
    IDistributedSynchronizationHandle handle,
    IReadOnlyList<SaveDocumentRequest> requests,
    IReadOnlySet<string> documentKinds) : IAsyncDisposable
{
    public IReadOnlyList<SaveDocumentRequest> Requests { get; } = requests;

    public IReadOnlySet<string> DocumentKinds { get; } = documentKinds;

    public Task CommitAsync(
        IReadOnlyCollection<string> authoritativeDocumentKinds,
        IReadOnlyList<SaveDocumentRequest> authoritativeRequests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authoritativeDocumentKinds);
        ArgumentNullException.ThrowIfNull(authoritativeRequests);
        var kinds = authoritativeDocumentKinds.Concat(DocumentKinds).Distinct(StringComparer.Ordinal).ToArray();
        var writes = authoritativeRequests.Concat(Requests).ToArray();
        return store.SaveAllAsync(DocumentCommitScope.Of(kinds), writes, cancellationToken);
    }

    public ValueTask DisposeAsync() => handle.DisposeAsync();
}

/// <summary>
/// Deep Groundwork module that turns final authoritative activity facts into one coherent SCD-2
/// projection batch. It owns sequence allocation, current-revision lookup, interval closure, safe
/// summary shaping and the cross-replica allocation lock; callers only append the returned requests
/// to their existing atomic domain commit.
/// </summary>
public sealed class GroundworkActivityManagementProjectionWriter(
    IDocumentStore store,
    IDistributedLockProvider lockProvider,
    IBoundedDocumentStore? boundedStore = null)
{
    private const string DefinitionKind = ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind;
    private const string DraftKind = ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind;
    private const string VersionKind = ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind;
    private const string WatermarkKind = ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkDocumentKind;
    private const string SnapshotKind = ActivitiesDesignStorageManifest.ActivityManagementProjectionSnapshotDocumentKind;
    private static readonly JsonSerializerOptions JsonOptions = GroundworkActivitiesDesignJson.Options;

    public async Task<GroundworkActivityManagementProjectionUpdate> PrepareAsync(
        ActivityManagementProjectionMutation mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        EnsureUnique(mutation.Definitions.Select(x => x.Definition.Id), "definition");
        EnsureUnique(mutation.Drafts.Select(x => x.Id), "draft");
        EnsureUnique(mutation.Versions.Select(x => x.DefinitionVersionId), "version");
        foreach (var change in mutation.Definitions)
            ValidateDefinitionChange(change);
        if (mutation.Definitions.Count == 0 && mutation.Drafts.Count == 0 && mutation.Versions.Count == 0)
        {
            return new GroundworkActivityManagementProjectionUpdate(
                store,
                NoopSynchronizationHandle.Instance,
                [],
                new HashSet<string>(StringComparer.Ordinal));
        }
        var handle = await lockProvider.AcquireLockAsync(ProjectionLockKey(store), null, cancellationToken);
        try
        {
            var reader = boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
                "Activity-management projection writes require an admitted bounded document-store runtime.");
            var watermarkEnvelope = await store.LoadAsync(
                WatermarkKind,
                ActivityManagementProjectionWatermark.CurrentId,
                cancellationToken);
            var watermark = watermarkEnvelope is null
                ? new ActivityManagementProjectionWatermark
                {
                    Id = ActivityManagementProjectionWatermark.CurrentId,
                    Sequence = 0,
                    RetainedFromSequence = 1,
                    AdvancedAt = DateTimeOffset.UnixEpoch
                }
                : Deserialize<ActivityManagementProjectionWatermark>(watermarkEnvelope, WatermarkKind);
            var nextSequence = checked(watermark.Sequence + 1);
            var snapshotAt = mutation.ChangedAt >= watermark.AdvancedAt
                ? mutation.ChangedAt
                : watermark.AdvancedAt;
            var requests = new List<SaveDocumentRequest>();
            var currentDefinitions = new Dictionary<string, Stored<ActivityDefinitionManagementProjectionRevision>?>(StringComparer.Ordinal);
            var currentDrafts = new Dictionary<string, Stored<ActivityDefinitionDraftManagementProjectionRevision>?>(StringComparer.Ordinal);
            var currentVersions = new Dictionary<string, Stored<ActivityDefinitionVersionManagementProjectionRevision>?>(StringComparer.Ordinal);

            foreach (var definitionId in mutation.Definitions.Select(x => x.Definition.Id)
                         .Concat(mutation.Drafts.Select(x => x.DefinitionId))
                         .Concat(mutation.Versions.Select(x => x.DefinitionId))
                         .Distinct(StringComparer.Ordinal))
            {
                currentDefinitions[definitionId] = await FindCurrentAsync<ActivityDefinitionManagementProjectionRevision>(
                    reader,
                    DefinitionKind,
                    ActivitiesDesignStorageManifest.ManagementDefinitionCurrentQuery,
                    ActivitiesDesignStorageManifest.DefinitionIdField,
                    definitionId,
                    cancellationToken);
            }

            var changedDefinitions = mutation.Definitions.ToDictionary(x => x.Definition.Id, StringComparer.Ordinal);
            foreach (var change in changedDefinitions.Values)
            {
                if (currentDefinitions[change.Definition.Id] is { } current)
                    EnsureSameOwner(
                        "definition",
                        change.Definition.Id,
                        change.Definition.Id,
                        change.Definition.TenantId,
                        current.Entity.DefinitionId,
                        current.Entity.TenantId);
            }

            var newDraftCounts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var draft in mutation.Drafts)
            {
                var current = await FindCurrentAsync<ActivityDefinitionDraftManagementProjectionRevision>(
                    reader,
                    DraftKind,
                    ActivitiesDesignStorageManifest.ManagementDraftCurrentQuery,
                    ActivitiesDesignStorageManifest.DraftIdField,
                    draft.Id,
                    cancellationToken);
                currentDrafts[draft.Id] = current;
                EnsureChildOwner(
                    "draft",
                    draft.Id,
                    draft.DefinitionId,
                    draft.TenantId,
                    current?.Entity.DefinitionId,
                    current?.Entity.TenantId,
                    changedDefinitions,
                    currentDefinitions);
                if (current is null)
                    newDraftCounts[draft.DefinitionId] = newDraftCounts.GetValueOrDefault(draft.DefinitionId) + 1;
            }

            var newVersionCounts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var version in mutation.Versions)
            {
                var current = await FindCurrentAsync<ActivityDefinitionVersionManagementProjectionRevision>(
                    reader,
                    VersionKind,
                    ActivitiesDesignStorageManifest.ManagementVersionCurrentQuery,
                    ActivitiesDesignStorageManifest.DefinitionVersionIdField,
                    version.DefinitionVersionId,
                    cancellationToken);
                currentVersions[version.DefinitionVersionId] = current;
                EnsureChildOwner(
                    "version",
                    version.DefinitionVersionId,
                    version.DefinitionId,
                    version.TenantId,
                    current?.Entity.DefinitionId,
                    current?.Entity.TenantId,
                    changedDefinitions,
                    currentDefinitions);
                if (current is null)
                    newVersionCounts[version.DefinitionId] = newVersionCounts.GetValueOrDefault(version.DefinitionId) + 1;
            }
            var nextVersionRows = mutation.Versions.ToDictionary(
                x => x.DefinitionVersionId,
                x => ToVersion(x, nextSequence),
                StringComparer.Ordinal);

            foreach (var definitionId in changedDefinitions.Keys
                         .Concat(newDraftCounts.Keys)
                         .Concat(newVersionCounts.Keys)
                         .Distinct(StringComparer.Ordinal))
            {
                var current = currentDefinitions.GetValueOrDefault(definitionId);
                var change = changedDefinitions.GetValueOrDefault(definitionId);
                if (change is null && current is null)
                    throw new InvalidOperationException($"Activity definition projection '{definitionId}' does not exist.");

                var head = change is null
                    ? current!.Entity.Head
                    : await ResolveVersionReferenceAsync(
                        reader,
                        change.Authoring.HeadVersionId,
                        change.Definition.Id,
                        change.Definition.TenantId,
                        currentVersions,
                        nextVersionRows,
                        cancellationToken);
                var recommendation = change is null
                    ? current!.Entity.Recommendation
                    : await ResolveVersionReferenceAsync(
                        reader,
                        change.Authoring.RecommendedVersionId,
                        change.Definition.Id,
                        change.Definition.TenantId,
                        currentVersions,
                        nextVersionRows,
                        cancellationToken);
                var next = change is null
                    ? CopyDefinition(current!.Entity, nextSequence, snapshotAt,
                        current.Entity.DraftCount + newDraftCounts.GetValueOrDefault(definitionId),
                        current.Entity.VersionCount + newVersionCounts.GetValueOrDefault(definitionId))
                    : ToDefinition(
                        change.Definition,
                        change.Authoring,
                        nextSequence,
                        snapshotAt,
                        (current?.Entity.DraftCount ?? 0) + newDraftCounts.GetValueOrDefault(definitionId),
                        (current?.Entity.VersionCount ?? 0) + newVersionCounts.GetValueOrDefault(definitionId),
                        head,
                        recommendation);
                StageRevision(DefinitionKind, ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionCollection, current, next, requests);
            }

            foreach (var draft in mutation.Drafts)
            {
                var next = ToDraft(draft, nextSequence);
                StageRevision(DraftKind, ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionCollection, currentDrafts[draft.Id], next, requests);
            }

            foreach (var version in mutation.Versions)
            {
                var next = nextVersionRows[version.DefinitionVersionId];
                StageRevision(VersionKind, ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionCollection, currentVersions[version.DefinitionVersionId], next, requests);
            }

            watermark.Sequence = nextSequence;
            watermark.AdvancedAt = snapshotAt;
            if (watermark.RetainedFromSequence == 0)
                watermark.RetainedFromSequence = nextSequence;
            requests.Add(Save(
                WatermarkKind,
                ActivitiesDesignStorageManifest.ActivityManagementProjectionWatermarkCollection,
                watermark,
                watermarkEnvelope?.Version ?? 0));
            requests.Add(Save(
                SnapshotKind,
                ActivitiesDesignStorageManifest.ActivityManagementProjectionSnapshotCollection,
                new ActivityManagementProjectionSnapshot
                {
                    Id = SequenceKey(nextSequence),
                    Sequence = nextSequence,
                    AsOf = snapshotAt
                },
                0));

            return new GroundworkActivityManagementProjectionUpdate(
                store,
                handle,
                requests,
                new HashSet<string>(requests.Select(x => x.DocumentKind), StringComparer.Ordinal));
        }
        catch
        {
            await handle.DisposeAsync();
            throw;
        }
    }

    private static void StageRevision<T>(
        string kind,
        string collection,
        Stored<T>? current,
        T next,
        ICollection<SaveDocumentRequest> requests)
        where T : ActivityManagementProjectionRevision
    {
        if (current is not null)
        {
            current.Entity.ValidToSequenceExclusive = next.ValidFromSequence;
            current.Entity.ValidToKey = SequenceKey(next.ValidFromSequence);
            requests.Add(Save(kind, collection, current.Entity, current.Envelope.Version));
        }
        requests.Add(Save(kind, collection, next, 0));
    }

    private static ActivityDefinitionManagementProjectionRevision ToDefinition(
        ActivityDefinition definition,
        ActivityDefinitionAuthoringState authoring,
        long sequence,
        DateTimeOffset changedAt,
        long draftCount,
        long versionCount,
        ActivityManagementVersionProjectionReference? head,
        ActivityManagementVersionProjectionReference? recommendation) => new()
    {
        Id = RevisionId("definition", definition.Id, sequence),
        ResourceId = definition.Id,
        DefinitionId = definition.Id,
        TenantId = definition.TenantId,
        VisibilityKey = VisibilityKey(definition.TenantId),
        SortKey = definition.Id,
        SearchText = SearchText(definition.Id, definition.ActivityTypeKey, definition.Category, definition.DisplayName, definition.Description),
        ValidFromSequence = sequence,
        ValidFromKey = SequenceKey(sequence),
        ValidToSequenceExclusive = long.MaxValue,
        ValidToKey = SequenceKey(long.MaxValue),
        ActivityTypeKey = definition.ActivityTypeKey,
        Category = definition.Category,
        DisplayName = definition.DisplayName,
        Description = definition.Description,
        ContentAuthority = authoring.ContentAuthority,
        HeadVersionId = authoring.HeadVersionId,
        RecommendedVersionId = authoring.RecommendedVersionId,
        Head = head,
        Recommendation = recommendation,
        HeadProviderKey = head?.ProviderKey,
        RecommendationProviderKey = recommendation?.ProviderKey,
        DraftCount = draftCount,
        VersionCount = versionCount,
        UpdatedAt = definition.LastModifiedAt,
        CreatedAt = definition.CreatedAt,
        LastModifiedAt = changedAt
    };

    private static ActivityDefinitionManagementProjectionRevision CopyDefinition(
        ActivityDefinitionManagementProjectionRevision current,
        long sequence,
        DateTimeOffset changedAt,
        long draftCount,
        long versionCount) => new()
    {
        Id = RevisionId("definition", current.DefinitionId, sequence),
        ResourceId = current.ResourceId,
        DefinitionId = current.DefinitionId,
        TenantId = current.TenantId,
        VisibilityKey = current.VisibilityKey,
        SortKey = current.SortKey,
        SearchText = current.SearchText,
        ValidFromSequence = sequence,
        ValidFromKey = SequenceKey(sequence),
        ValidToSequenceExclusive = long.MaxValue,
        ValidToKey = SequenceKey(long.MaxValue),
        ActivityTypeKey = current.ActivityTypeKey,
        Category = current.Category,
        DisplayName = current.DisplayName,
        Description = current.Description,
        ContentAuthority = current.ContentAuthority,
        HeadVersionId = current.HeadVersionId,
        RecommendedVersionId = current.RecommendedVersionId,
        Head = current.Head,
        Recommendation = current.Recommendation,
        HeadProviderKey = current.HeadProviderKey,
        RecommendationProviderKey = current.RecommendationProviderKey,
        DraftCount = draftCount,
        VersionCount = versionCount,
        UpdatedAt = current.UpdatedAt,
        CreatedAt = current.CreatedAt,
        LastModifiedAt = changedAt
    };

    private static ActivityDefinitionDraftManagementProjectionRevision ToDraft(ActivityDefinitionDraft draft, long sequence) => new()
    {
        Id = RevisionId("draft", draft.Id, sequence),
        ResourceId = draft.Id,
        DefinitionId = draft.DefinitionId,
        TenantId = draft.TenantId,
        VisibilityKey = VisibilityKey(draft.TenantId),
        SortKey = draft.Id,
        SearchText = SearchText(draft.Id, draft.PresentationLabel),
        ValidFromSequence = sequence,
        ValidFromKey = SequenceKey(sequence),
        ValidToSequenceExclusive = long.MaxValue,
        ValidToKey = SequenceKey(long.MaxValue),
        DraftId = draft.Id,
        Revision = draft.Revision,
        SourceVersionId = draft.SourceVersionId,
        Status = draft.Status,
        ProviderKey = draft.State.Provider.ProviderKey,
        ProviderSchemaVersion = draft.State.Provider.SchemaVersion,
        PresentationLabel = draft.PresentationLabel,
        UpdatedAt = draft.LastModifiedAt,
        CreatedAt = draft.CreatedAt,
        LastModifiedAt = draft.LastModifiedAt
    };

    private static ActivityDefinitionVersionManagementProjectionRevision ToVersion(
        ActivityDefinitionVersionPublication version,
        long sequence) => new()
    {
        Id = RevisionId("version", version.DefinitionVersionId, sequence),
        ResourceId = version.DefinitionVersionId,
        DefinitionId = version.DefinitionId,
        TenantId = version.TenantId,
        VisibilityKey = VisibilityKey(version.TenantId),
        SortKey = version.DefinitionVersionId,
        SearchText = SearchText(version.DefinitionVersionId, version.Version),
        ValidFromSequence = sequence,
        ValidFromKey = SequenceKey(sequence),
        ValidToSequenceExclusive = long.MaxValue,
        ValidToKey = SequenceKey(long.MaxValue),
        DefinitionVersionId = version.DefinitionVersionId,
        Version = version.Version,
        Lifecycle = version.Lifecycle,
        ProviderKey = version.Provider.ProviderKey,
        ProviderSchemaVersion = version.Provider.SchemaVersion,
        PublishedAt = version.PublishedAt,
        CreatedAt = version.CreatedAt,
        LastModifiedAt = version.LastModifiedAt
    };

    private static async Task<Stored<T>?> FindCurrentAsync<T>(
        IBoundedDocumentStore reader,
        string kind,
        string queryIdentity,
        string idPath,
        string id,
        CancellationToken cancellationToken)
        where T : ActivityManagementProjectionRevision
    {
        var envelope = await reader.FirstOrDefaultAsync(
            new DocumentQuery(
                kind,
                queryIdentity,
                [
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(idPath, id)),
                    DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                        ActivitiesDesignStorageManifest.ManagementValidToField,
                        SequenceKey(long.MaxValue)))
                ],
                take: 1)
                .Select(BoundedQueryResultOperation.First),
            cancellationToken);
        return envelope is null ? null : new(Deserialize<T>(envelope, kind), envelope);
    }

    private static async Task<ActivityManagementVersionProjectionReference?> ResolveVersionReferenceAsync(
        IBoundedDocumentStore reader,
        string? versionId,
        string expectedDefinitionId,
        string? expectedTenantId,
        IDictionary<string, Stored<ActivityDefinitionVersionManagementProjectionRevision>?> currentVersions,
        IReadOnlyDictionary<string, ActivityDefinitionVersionManagementProjectionRevision> nextVersions,
        CancellationToken cancellationToken)
    {
        if (versionId is null)
            return null;
        if (nextVersions.TryGetValue(versionId, out var next))
        {
            EnsureSameOwner("version", versionId, expectedDefinitionId, expectedTenantId, next.DefinitionId, next.TenantId);
            return ToReference(next);
        }
        if (!currentVersions.TryGetValue(versionId, out var current))
        {
            current = await FindCurrentAsync<ActivityDefinitionVersionManagementProjectionRevision>(
                reader,
                VersionKind,
                ActivitiesDesignStorageManifest.ManagementVersionCurrentQuery,
                ActivitiesDesignStorageManifest.DefinitionVersionIdField,
                versionId,
                cancellationToken);
            currentVersions[versionId] = current;
        }
        if (current is null)
            return null;
        EnsureSameOwner("version", versionId, expectedDefinitionId, expectedTenantId, current.Entity.DefinitionId, current.Entity.TenantId);
        return ToReference(current.Entity);
    }

    private static ActivityManagementVersionProjectionReference ToReference(
        ActivityDefinitionVersionManagementProjectionRevision version) => new(
        version.DefinitionVersionId,
        version.Version,
        version.Lifecycle,
        version.ProviderKey,
        version.ProviderSchemaVersion);

    private static SaveDocumentRequest Save<T>(string kind, string collection, T entity, long expectedVersion)
        where T : Elsa.Primitives.Entities.Entity
    {
        var request = GroundworkDocumentWriter.ToSaveRequest(
            kind,
            collection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            entity,
            JsonOptions);
        return new SaveDocumentRequest(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, expectedVersion);
    }

    private static T Deserialize<T>(DocumentEnvelope envelope, string kind)
        where T : Elsa.Primitives.Entities.Entity
    {
        var document = JsonSerializer.Deserialize<GroundworkDocument<T>>(envelope.ContentJson, JsonOptions);
        return document?.Entity ?? throw new InvalidOperationException(
            $"Document '{envelope.Id}' of kind '{kind}' could not be deserialized as {typeof(T).Name}.");
    }

    internal static string SequenceKey(long sequence) => sequence.ToString("D20", CultureInfo.InvariantCulture);

    private static string RevisionId(string kind, string logicalId, long sequence)
    {
        var identityHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(logicalId))).ToLowerInvariant();
        return $"{kind}:{identityHash}:{SequenceKey(sequence)}";
    }

    private static string VisibilityKey(string? tenantId) => tenantId ?? "*";

    private static string SearchText(params string?[] values)
    {
        var text = string.Join(
            '\u001f',
            values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim().ToUpperInvariant()));
        return text.Length <= ActivitiesDesignStorageManifest.ManagementSearchMaximumLength
            ? text
            : text[..ActivitiesDesignStorageManifest.ManagementSearchMaximumLength];
    }

    private static void EnsureUnique(IEnumerable<string> ids, string resourceKind)
    {
        var duplicates = ids
            .GroupBy(x => x, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length > 0)
            throw new ArgumentException($"A management projection mutation contains duplicate {resourceKind} identities: {string.Join(", ", duplicates)}.");
    }

    private static void ValidateDefinitionChange(ActivityManagementDefinitionChange change)
    {
        if (!StringComparer.Ordinal.Equals(change.Definition.Id, change.Authoring.DefinitionId) ||
            !StringComparer.Ordinal.Equals(change.Definition.TenantId, change.Authoring.TenantId))
        {
            throw new ArgumentException(
                $"Activity definition projection '{change.Definition.Id}' has mismatched authoring ownership.",
                nameof(change));
        }
    }

    private static void EnsureChildOwner(
        string resourceKind,
        string resourceId,
        string definitionId,
        string? tenantId,
        string? currentDefinitionId,
        string? currentTenantId,
        IReadOnlyDictionary<string, ActivityManagementDefinitionChange> changedDefinitions,
        IReadOnlyDictionary<string, Stored<ActivityDefinitionManagementProjectionRevision>?> currentDefinitions)
    {
        if (currentDefinitionId is not null || currentTenantId is not null)
            EnsureSameOwner(resourceKind, resourceId, definitionId, tenantId, currentDefinitionId, currentTenantId);

        var ownerTenantId = changedDefinitions.TryGetValue(definitionId, out var change)
            ? change.Definition.TenantId
            : currentDefinitions.GetValueOrDefault(definitionId)?.Entity.TenantId;
        if (!changedDefinitions.ContainsKey(definitionId) && currentDefinitions.GetValueOrDefault(definitionId) is null)
            throw new InvalidOperationException($"Activity definition projection '{definitionId}' does not exist.");
        if (!StringComparer.Ordinal.Equals(ownerTenantId, tenantId))
            throw new InvalidOperationException($"Activity {resourceKind} projection '{resourceId}' does not belong to its definition scope.");
    }

    private static void EnsureSameOwner(
        string resourceKind,
        string resourceId,
        string expectedDefinitionId,
        string? expectedTenantId,
        string? actualDefinitionId,
        string? actualTenantId)
    {
        if (StringComparer.Ordinal.Equals(expectedDefinitionId, actualDefinitionId) &&
            StringComparer.Ordinal.Equals(expectedTenantId, actualTenantId))
            return;
        throw new InvalidOperationException($"Activity {resourceKind} projection '{resourceId}' cannot change definition or tenant ownership.");
    }

    internal static string ProjectionLockKey(IDocumentStore documents)
    {
        var scope = $"{documents.Access.Kind}:{documents.Access.Scope?.Value ?? "global"}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope))).ToLowerInvariant();
        return $"elsa:activities:design:management-projection:{hash}";
    }

    private sealed record Stored<T>(T Entity, DocumentEnvelope Envelope)
        where T : Elsa.Primitives.Entities.Entity;

    private sealed class NoopSynchronizationHandle : IDistributedSynchronizationHandle
    {
        public static NoopSynchronizationHandle Instance { get; } = new();
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.Groundwork.Querying;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>
/// One owner replacement staged by a Groundwork transaction that also owns the corresponding source
/// mutation. Supplying <paramref name="ResultItems"/> replaces the source owner with authoritative
/// facts; otherwise the existing source facts are cloned/repointed by exact occurrence replacement.
/// </summary>
public sealed record ActivityDependencyProjectionOwnerChange(
    ActivityDefinitionReference SourceOwner,
    ActivityDefinitionReference? ResultOwner,
    IReadOnlyList<ActivityUpgradeOccurrenceReplacement> Replacements,
    IReadOnlyList<ActivityDependencyItem>? ResultItems = null);

public sealed record ActivityDependencyProjectionPreparedUpdate(SaveDocumentRequest Request);

/// <summary>
/// Rebuildable mixed-owner dependency projection. Authoritative activity publication edges remain
/// separate; this document can be dropped and reconstructed from activity/workflow versions and drafts.
/// </summary>
public sealed class GroundworkActivityDependencyProjection(
    IDocumentStore store,
    IActivityDefinitionVersionPublicationStore publications) :
    IActivityDependencyProjectionStore,
    IActivityDependencyProjectionRebuilder
{
    private static readonly JsonSerializerOptions JsonOptions = GroundworkActivitiesDesignJson.Options;

    public async Task RebuildAsync(ActivityDependencyProjectionRebuild rebuild, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rebuild);
        if (string.IsNullOrWhiteSpace(rebuild.RebuildId) || rebuild.Sequence < 0)
            throw new ArgumentException("Projection rebuild identity and non-negative sequence are required.", nameof(rebuild));
        ValidateItems(rebuild.Items);

        var existing = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
            ActivityDependencyProjectionState.CurrentId,
            cancellationToken);
        if (existing is not null)
        {
            var current = Deserialize(existing);
            if (rebuild.Sequence < current.Sequence)
                throw new InvalidOperationException("A dependency projection rebuild cannot move its sequence backwards.");
            if (rebuild.Sequence == current.Sequence && !StringComparer.Ordinal.Equals(rebuild.RebuildId, current.RebuildId))
                throw new InvalidOperationException("A projection sequence is already bound to another rebuild identity.");
        }

        var state = new ActivityDependencyProjectionState
        {
            Id = ActivityDependencyProjectionState.CurrentId,
            RebuildId = rebuild.RebuildId,
            Sequence = rebuild.Sequence,
            AsOf = rebuild.AsOf,
            Items = rebuild.Items.OrderBy(ItemSortKey, StringComparer.Ordinal).ToList()
        };
        var save = GroundworkDocumentWriter.ToSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDependencyProjectionCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            state,
            JsonOptions);
        await store.SaveAllAsync(
            DocumentCommitScope.Of(ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind),
            [new SaveDocumentRequest(save.DocumentKind, save.Id, save.SchemaVersion, save.ContentJson, existing?.Version ?? 0)],
            cancellationToken);
    }

    public async Task<ActivityDependencyProjectionRebuild> RebuildCurrentAsync(
        string rebuildId,
        DateTimeOffset asOf,
        IReadOnlyList<ActivityDependencyItem> items,
        CancellationToken cancellationToken = default)
    {
        var envelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
            ActivityDependencyProjectionState.CurrentId,
            cancellationToken);
        var sequence = envelope is null ? 1 : checked(Deserialize(envelope).Sequence + 1);
        var rebuild = new ActivityDependencyProjectionRebuild(rebuildId, sequence, asOf, items);
        await RebuildAsync(rebuild, cancellationToken);
        return rebuild;
    }

    /// <summary>
    /// Prepares, but does not commit, the next projection document. Publishing bridges add the
    /// returned CAS request to the same <see cref="DocumentCommitScope"/> as their source mutation,
    /// preventing a successful source write with a stale derived view.
    /// </summary>
    public async Task<ActivityDependencyProjectionPreparedUpdate> PrepareUpdateAsync(
        IReadOnlyList<ActivityDependencyProjectionOwnerChange> changes,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var envelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
            ActivityDependencyProjectionState.CurrentId,
            cancellationToken);
        var current = envelope is null
            ? new ActivityDependencyProjectionState
            {
                Id = ActivityDependencyProjectionState.CurrentId,
                RebuildId = "incremental",
                Sequence = 0,
                AsOf = DateTimeOffset.MinValue
            }
            : Deserialize(envelope);
        var items = current.Items.ToList();

        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = items.Where(x => SameOwner(x.Owner, change.SourceOwner)).ToArray();
            items.RemoveAll(x => SameOwner(x.Owner, change.SourceOwner));
            if (change.ResultOwner is null)
                continue;

            IReadOnlyList<ActivityDependencyItem> resultItems;
            if (change.ResultItems is not null)
                resultItems = change.ResultItems;
            else
            {
                var replacements = change.Replacements.ToDictionary(x => x.OccurrenceId, StringComparer.Ordinal);
                var matched = new HashSet<string>(StringComparer.Ordinal);
                var rewritten = new List<ActivityDependencyItem>(source.Length);
                foreach (var item in source)
                {
                    var dependency = item.Dependency;
                    if (replacements.TryGetValue(item.Occurrence.OccurrenceId, out var replacement))
                    {
                        if (!StringComparer.Ordinal.Equals(dependency.VersionId, replacement.FromVersionId))
                            throw new InvalidOperationException($"Projection occurrence '{replacement.OccurrenceId}' no longer references the planned source version.");
                        var target = await publications.FindAsync(replacement.ToVersionId, cancellationToken)
                                     ?? throw new InvalidOperationException($"Projection target version '{replacement.ToVersionId}' is unavailable.");
                        dependency = Reference(target);
                        matched.Add(replacement.OccurrenceId);
                    }
                    rewritten.Add(DirectItem(change.ResultOwner, dependency, item.Occurrence, item.MemberUsage));
                }
                if (replacements.Keys.Any(x => !matched.Contains(x)))
                    throw new InvalidOperationException("One or more planned projection occurrences are unavailable.");
                resultItems = rewritten;
            }
            items.AddRange(resultItems);
        }

        ValidateItems(items);
        var state = new ActivityDependencyProjectionState
        {
            Id = ActivityDependencyProjectionState.CurrentId,
            RebuildId = current.RebuildId,
            Sequence = checked(current.Sequence + 1),
            AsOf = asOf,
            Items = items.OrderBy(ItemSortKey, StringComparer.Ordinal).ToList()
        };
        var save = GroundworkDocumentWriter.ToSaveRequest(
            ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDependencyProjectionCollection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            state,
            JsonOptions);
        return new(new(save.DocumentKind, save.Id, save.SchemaVersion, save.ContentJson, envelope?.Version ?? 0));
    }

    public async Task<ActivityDependencyProjectionSlice> ReadAsync(
        ActivityDependencyProjectionReadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Limit <= 0 || request.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(request));
        var publication = await publications.FindAsync(request.RootVersionId, cancellationToken)
                          ?? throw new InvalidOperationException($"Activity version publication '{request.RootVersionId}' was not found.");
        var envelope = await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
            ActivityDependencyProjectionState.CurrentId,
            cancellationToken);
        var state = envelope is null
            ? new ActivityDependencyProjectionState { Id = ActivityDependencyProjectionState.CurrentId, RebuildId = "empty", Sequence = 0, AsOf = DateTimeOffset.MinValue }
            : Deserialize(envelope);
        var watermark = $"{state.Sequence}:{state.RebuildId}";
        if (request.Watermark is not null && !StringComparer.Ordinal.Equals(request.Watermark, watermark))
            throw new ActivityDependencyWatermarkExpiredException(request.Watermark);

        var includeDrafts = request.Query.Include.Contains("Drafts");
        var includeVersions = request.Query.Include.Contains("Versions");
        var traversed = Traverse(request.RootVersionId, request.Query, state.Items)
            .Where(x => IsIncluded(x.Owner.Kind, includeDrafts, includeVersions))
            .Where(x => IsVisible(x.Owner.TenantId, request.TenantId) && IsVisible(x.Dependency.TenantId, request.TenantId))
            .Skip(request.Offset)
            .Take(checked(request.Limit + 1))
            .ToArray();
        var items = traversed.Take(request.Limit).ToArray();
        var next = request.Offset + items.Length;
        return new ActivityDependencyProjectionSlice(
            new ActivityDefinitionReference(
                "ActivityVersion",
                publication.DefinitionId,
                publication.DefinitionVersionId,
                publication.Version,
                TemplateHash: publication.TemplateHash,
                TenantId: publication.TenantId,
                Lifecycle: publication.Lifecycle),
            new ActivityDependencyConsistency(
                ActivityDependencyConsistencyKind.DerivedProjection,
                false,
                state.Sequence,
                state.AsOf,
                state.RebuildId),
            items,
            watermark,
            traversed.Length > request.Limit ? next : null);
    }

    private static IEnumerable<ActivityDependencyItem> Traverse(
        string rootVersionId,
        ActivityDependencyQuery query,
        IEnumerable<ActivityDependencyItem> source)
    {
        var direct = source
            .Where(x => x.IsDirect)
            .OrderBy(x => x.Owner.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.Owner.DraftId ?? x.Owner.VersionId, StringComparer.Ordinal)
            .ThenBy(x => x.Occurrence.OccurrenceId, StringComparer.Ordinal)
            .ThenBy(x => x.Dependency.VersionId, StringComparer.Ordinal)
            .ToArray();
        var queue = new Queue<TraversalState>();
        queue.Enqueue(new(rootVersionId, 0, [], new HashSet<string>([rootVersionId], StringComparer.Ordinal)));
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var matches = query.Direction == ActivityDependencyDirection.Inbound
                ? direct.Where(x => StringComparer.Ordinal.Equals(x.Dependency.VersionId, current.VersionId))
                : direct.Where(x => StringComparer.Ordinal.Equals(x.Owner.VersionId, current.VersionId));
            foreach (var item in matches)
            {
                var path = current.Path.Count == 0
                    ? item.Path
                    : query.Direction == ActivityDependencyDirection.Inbound
                        ? new[] { item.Owner }.Concat(current.Path).ToArray()
                        : current.Path.Concat([item.Dependency]).ToArray();
                var projected = item with { Depth = current.Depth + 1, Path = path };
                yield return projected;
                if (!query.Transitive)
                    continue;
                var next = query.Direction == ActivityDependencyDirection.Inbound ? item.Owner.VersionId : item.Dependency.VersionId;
                if (next is null || current.VisitedVersions.Contains(next))
                    continue;
                queue.Enqueue(new(
                    next,
                    current.Depth + 1,
                    path,
                    new HashSet<string>(current.VisitedVersions, StringComparer.Ordinal) { next }));
            }
        }
    }

    private sealed record TraversalState(
        string VersionId,
        int Depth,
        IReadOnlyList<ActivityDefinitionReference> Path,
        IReadOnlySet<string> VisitedVersions);

    private static ActivityDependencyProjectionState Deserialize(DocumentEnvelope envelope)
    {
        var document = JsonSerializer.Deserialize<GroundworkDocument<ActivityDependencyProjectionState>>(envelope.ContentJson, JsonOptions);
        return document?.Entity ?? throw new InvalidOperationException($"Dependency projection document '{envelope.Id}' is unreadable.");
    }

    private static void ValidateItems(IEnumerable<ActivityDependencyItem> items)
    {
        var allowed = new HashSet<string>(["ActivityVersion", "ActivityDraft", "WorkflowVersion", "WorkflowDraft"], StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (!allowed.Contains(item.Owner.Kind) || item.Dependency.Kind != "ActivityVersion" || string.IsNullOrWhiteSpace(item.Dependency.VersionId))
                throw new ArgumentException("Projection items require a supported mixed owner and exact activity-version dependency.", nameof(items));
            if (string.IsNullOrWhiteSpace(item.RelationshipId) || string.IsNullOrWhiteSpace(item.Occurrence.OccurrenceId))
                throw new ArgumentException("Projection relationship and occurrence identities are required.", nameof(items));
        }
    }

    private static bool IsIncluded(string kind, bool drafts, bool versions) => kind.EndsWith("Draft", StringComparison.Ordinal) ? drafts : versions;
    private static bool IsVisible(string? ownerTenant, string? requestTenant) => ownerTenant is null || StringComparer.Ordinal.Equals(ownerTenant, requestTenant);
    private static string ItemSortKey(ActivityDependencyItem item) => string.Join('\u001f', item.Depth, item.Owner.Kind, item.Owner.DraftId ?? item.Owner.VersionId, item.Occurrence.OccurrenceId, item.Dependency.VersionId);

    private static bool SameOwner(ActivityDefinitionReference left, ActivityDefinitionReference right) =>
        StringComparer.Ordinal.Equals(left.Kind, right.Kind) &&
        StringComparer.Ordinal.Equals(left.DraftId ?? left.VersionId, right.DraftId ?? right.VersionId);

    private static ActivityDefinitionReference Reference(ActivityDefinitionVersionPublication publication) => new(
        "ActivityVersion",
        publication.DefinitionId,
        publication.DefinitionVersionId,
        publication.Version,
        TemplateHash: publication.TemplateHash,
        TenantId: publication.TenantId,
        Lifecycle: publication.Lifecycle);

    private static ActivityDependencyItem DirectItem(
        ActivityDefinitionReference owner,
        ActivityDefinitionReference dependency,
        ActivityDependencyOccurrence occurrence,
        IReadOnlyList<ActivityContractMemberUsage>? memberUsage = null) => new(
        $"{owner.Kind}:{owner.DraftId ?? owner.VersionId}:{occurrence.OccurrenceId}:{dependency.VersionId}",
        owner,
        dependency,
        occurrence,
        true,
        1,
        [owner, dependency],
        memberUsage);
}

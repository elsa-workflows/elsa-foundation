using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;

namespace Elsa.Activities.Design.Tests.Fixtures;

/// <summary>
/// Default object-backed reusable-activity conformance fixture. Tests that need typed executable
/// artifacts can use <see cref="InMemoryReusableActivityStores{TExecutableTemplate,TSourceReference}"/>.
/// </summary>
public sealed class InMemoryReusableActivityStores : InMemoryReusableActivityStores<object, object>
{
}

/// <summary>
/// One lock-guarded in-memory implementation of the Design read/write ports. It intentionally models
/// the transaction boundary, optimistic revisions/heads, and immutable publication behavior expected
/// from the eventual Groundwork adapters rather than acting as independent per-port dictionaries.
/// </summary>
public class InMemoryReusableActivityStores<TExecutableTemplate, TSourceReference> :
    IActivityDefinitionStore,
    IActivityDefinitionAuthoringStore,
    IActivityDefinitionDraftStore,
    IActivityDefinitionVersionPublicationStore,
    IActivityDefinitionLayoutStore,
    IActivityDraftValidationStore,
    IActivityDirectDependencyStore,
    IActivityDependencyProjectionStore,
    IActivityUpgradePlanStore,
    ICreateActivityDefinitionCommand,
    IUpdateActivityDefinitionPresentationCommand,
    ICreateActivityDraftCommand,
    IReplaceActivityDraftCommand,
    IDiscardActivityDraftCommand,
    IStoreActivityDraftValidationCommand,
    IChangeActivityVersionLifecycleCommand,
    ICommitActivityPublicationCommand<TExecutableTemplate, TSourceReference>
    where TExecutableTemplate : class
    where TSourceReference : class
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ActivityDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityDefinitionAuthoringState> _authoring = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityDefinitionDraft> _drafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityDefinitionDraftLayout> _draftLayouts = new(StringComparer.Ordinal);
    private readonly Dictionary<(string DraftId, long Revision), ActivityDraftValidationState> _validations = new();
    private readonly Dictionary<string, ActivityDefinitionVersion> _catalogVersions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityDefinitionVersionPublication> _publications = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityDefinitionVersionLayout> _versionLayouts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityDependencyEdge> _edges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActivityUpgradePlan> _upgradePlans = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TExecutableTemplate> _templates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TSourceReference> _sourceReferences = new(StringComparer.Ordinal);
    private readonly HashSet<(string? TenantId, string ActivityTypeKey)> _definitionKeys = [];
    private long _sequence;

    public IReadOnlyCollection<ActivityDefinition> Definitions
    {
        get { lock (_gate) return _definitions.Values.Select(Clone).ToArray(); }
    }

    public IReadOnlyCollection<ActivityDefinitionVersion> CatalogVersions
    {
        get { lock (_gate) return _catalogVersions.Values.Select(Clone).ToArray(); }
    }

    public bool ContainsTemplate(string templateId)
    {
        lock (_gate) return _templates.ContainsKey(templateId);
    }

    public bool ContainsSourceReference(string sourceReferenceId)
    {
        lock (_gate) return _sourceReferences.ContainsKey(sourceReferenceId);
    }

    public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_definitions.TryGetValue(id, out var value)
                ? Clone(value)
                : throw Missing($"Activity definition '{id}' was not found."));
    }

    public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(Apply(filter).Select(Clone).FirstOrDefault());
    }

    public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ActivityDefinition>>(Apply(filter).Select(Clone).ToArray());
    }

    public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(
        string id,
        string activityTypeKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_definitions.Values
                .Where(x => string.Equals(x.Id, id, StringComparison.Ordinal) ||
                            string.Equals(x.ActivityTypeKey, activityTypeKey, StringComparison.Ordinal))
                .Select(Clone)
                .FirstOrDefault());
    }

    public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_definitions.Values.Any(x =>
                string.Equals(x.ActivityTypeKey, activityTypeKey, StringComparison.Ordinal)));
    }

    public void SeedPublication(ActivityDefinitionVersionPublication publication, ActivityDefinitionVersionLayout layout)
    {
        lock (_gate)
        {
            if (!string.Equals(publication.DefinitionVersionId, layout.DefinitionVersionId, StringComparison.Ordinal))
                throw new ArgumentException("Publication and layout version identities must match.", nameof(layout));
            _publications.Add(publication.DefinitionVersionId, Clone(publication));
            _versionLayouts.Add(layout.DefinitionVersionId, Clone(layout));
            _sequence++;
        }
    }

    public void SeedDependency(ActivityDependencyEdge edge)
    {
        lock (_gate)
        {
            _edges.Add(edge.Id, Clone(edge));
            _sequence++;
        }
    }

    public Task<ActivityDefinitionAuthoringState?> FindAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_authoring.GetValueOrDefault(definitionId) is { } value ? Clone(value) : null);
    }

    public Task<IReadOnlyList<ActivityDefinitionAuthoringState>> ListAsync(
        IEnumerable<string> definitionIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = definitionIds.ToHashSet(StringComparer.Ordinal);
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ActivityDefinitionAuthoringState>>(
                _authoring.Values.Where(x => ids.Contains(x.DefinitionId)).Select(Clone).ToArray());
    }

    Task<ActivityDefinitionDraft?> IActivityDefinitionDraftStore.FindAsync(string draftId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_drafts.GetValueOrDefault(draftId) is { } value ? Clone(value) : null);
    }

    public Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ActivityDefinitionDraft>>(
                _drafts.Values
                    .Where(x => StringComparer.Ordinal.Equals(x.DefinitionId, definitionId))
                    .OrderBy(x => x.Id, StringComparer.Ordinal)
                    .Select(Clone)
                    .ToArray());
    }

    Task<ActivityDefinitionVersionPublication?> IActivityDefinitionVersionPublicationStore.FindAsync(
        string definitionVersionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_publications.GetValueOrDefault(definitionVersionId) is { } value ? Clone(value) : null);
    }

    Task<IReadOnlyList<ActivityDefinitionVersionPublication>> IActivityDefinitionVersionPublicationStore.ListByDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ActivityDefinitionVersionPublication>>(
                _publications.Values
                    .Where(x => StringComparer.Ordinal.Equals(x.DefinitionId, definitionId))
                    .OrderBy(x => x.Version, StringComparer.Ordinal)
                    .Select(Clone)
                    .ToArray());
    }

    public Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(string draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_draftLayouts.GetValueOrDefault(draftId) is { } value ? Clone(value) : null);
    }

    public Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(
        string definitionVersionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_versionLayouts.GetValueOrDefault(definitionVersionId) is { } value ? Clone(value) : null);
    }

    Task<ActivityDraftValidationState?> IActivityDraftValidationStore.FindAsync(
        string draftId,
        long revision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult(_validations.GetValueOrDefault((draftId, revision)) is { } value ? Clone(value) : null);
    }

    public Task<IReadOnlyList<ActivityDependencyEdge>> ListOutboundAsync(
        string ownerVersionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            return Task.FromResult<IReadOnlyList<ActivityDependencyEdge>>(
                _edges.Values
                    .Where(x => StringComparer.Ordinal.Equals(x.OwnerVersionId, ownerVersionId))
                    .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal)
                    .ThenBy(x => x.DependencyVersionId, StringComparer.Ordinal)
                    .Select(Clone)
                    .ToArray());
    }

    public Task<ActivityDependencyProjectionSlice> ReadAsync(
        ActivityDependencyProjectionReadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Limit));
        if (request.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(request.Offset));

        lock (_gate)
        {
            var root = Publication(request.RootVersionId);
            var watermark = _sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (request.Watermark is not null && !StringComparer.Ordinal.Equals(request.Watermark, watermark))
                throw new ActivityDependencyWatermarkExpiredException(request.Watermark);
            var relationships = Traverse(request.RootVersionId, request.Query.Direction, request.Query.Transitive);
            var visibleRelationships = relationships
                .Where(x => IsVisible(Publication(x.Edge.OwnerVersionId).TenantId, request.TenantId) &&
                            IsVisible(Publication(x.Edge.DependencyVersionId).TenantId, request.TenantId))
                .Where(_ => request.Query.Include.Contains("Versions"))
                .ToArray();
            var items = visibleRelationships.Skip(request.Offset).Take(request.Limit).Select(x => ToDependencyItem(x.Edge, x.Depth, MaterializePath(x.Path))).ToArray();
            var nextOffset = request.Offset + items.Length;

            return Task.FromResult(new ActivityDependencyProjectionSlice(
                ToReference(root),
                new ActivityDependencyConsistency(
                    ActivityDependencyConsistencyKind.DerivedProjection,
                    false,
                    _sequence,
                    null),
                items,
                watermark,
                nextOffset < visibleRelationships.Length ? nextOffset : null));
        }
    }

    Task<ActivityUpgradePlan?> IActivityUpgradePlanStore.FindAsync(string planId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_upgradePlans.GetValueOrDefault(planId));
    }

    public Task SaveAsync(ActivityUpgradePlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_upgradePlans.TryAdd(plan.PlanId, plan))
                throw Conflict($"Upgrade plan '{plan.PlanId}' already exists.");
            _sequence++;
        }
        return Task.CompletedTask;
    }

    public Task ExecuteAsync(CreateActivityDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateCreate(request);

        lock (_gate)
        {
            var key = (request.Definition.TenantId, request.Definition.ActivityTypeKey);
            if (_definitions.ContainsKey(request.Definition.Id) || _definitionKeys.Contains(key))
                throw Conflict($"Activity definition '{request.Definition.Id}' or key '{request.Definition.ActivityTypeKey}' already exists.");
            if (_authoring.ContainsKey(request.AuthoringState.DefinitionId) ||
                _drafts.ContainsKey(request.InitialDraft.Id) ||
                _draftLayouts.ContainsKey(request.InitialLayout.DraftId))
                throw Conflict("Activity definition authoring state or initial draft already exists.");

            _definitionKeys.Add(key);
            _definitions.Add(request.Definition.Id, Clone(request.Definition));
            _authoring.Add(request.AuthoringState.DefinitionId, Clone(request.AuthoringState));
            _drafts.Add(request.InitialDraft.Id, Clone(request.InitialDraft));
            _draftLayouts.Add(request.InitialDraft.Id, Clone(request.InitialLayout));
            _sequence++;
        }
        return Task.CompletedTask;
    }

    public Task<ActivityDefinition> ExecuteAsync(
        UpdateActivityDefinitionPresentationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var current = _definitions.GetValueOrDefault(request.DefinitionId)
                ?? throw Missing($"Activity definition '{request.DefinitionId}' was not found.");
            var authoring = Authoring(request.DefinitionId);
            EnsureDesignAuthority(authoring);
            if (!StringComparer.Ordinal.Equals(current.TenantId, authoring.TenantId))
                throw Conflict($"Activity definition '{request.DefinitionId}' tenant does not match its authoring state.");
            if (!IsVisible(current.TenantId, request.TenantId) || !IsVisible(authoring.TenantId, request.TenantId))
                throw Conflict($"Activity definition '{request.DefinitionId}' is outside the caller tenant scope.");

            var updated = Clone(current);
            updated.Category = request.Category;
            updated.DisplayName = request.DisplayName;
            updated.Description = request.Description;
            updated.LastModifiedAt = request.LastModifiedAt;
            _definitions[request.DefinitionId] = updated;
            _sequence++;
            return Task.FromResult(Clone(updated));
        }
    }

    public Task ExecuteAsync(CreateActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDraftAndLayout(request.Draft, request.Layout);

        lock (_gate)
        {
            var authoring = Authoring(request.Draft.DefinitionId);
            EnsureDesignAuthority(authoring);
            EnsureExpectedHead(authoring, request.ExpectedDefinitionHeadVersionId);
            if (_drafts.ContainsKey(request.Draft.Id) || _draftLayouts.ContainsKey(request.Layout.DraftId))
                throw Conflict($"Activity draft '{request.Draft.Id}' already exists.");

            _drafts.Add(request.Draft.Id, Clone(request.Draft));
            _draftLayouts.Add(request.Draft.Id, Clone(request.Layout));
            _sequence++;
        }
        return Task.CompletedTask;
    }

    public Task<ActivityDefinitionDraft> ExecuteAsync(
        ReplaceActivityDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var draft = ActiveDraft(request.DraftId, request.ExpectedRevision);
            EnsureDesignAuthority(Authoring(draft.DefinitionId));
            var nextRevision = checked(draft.Revision + 1);
            var updated = Clone(draft);
            updated.Revision = nextRevision;
            updated.State = Clone(request.State);
            updated.LastModifiedAt = DateTimeOffset.UtcNow;

            var layout = Clone(_draftLayouts[request.DraftId]);
            layout.Revision = nextRevision;
            layout.Records = request.Layout.Select(Clone).ToList();
            layout.LastModifiedAt = updated.LastModifiedAt;

            _drafts[request.DraftId] = updated;
            _draftLayouts[request.DraftId] = layout;
            _sequence++;
            return Task.FromResult(Clone(updated));
        }
    }

    public Task ExecuteAsync(DiscardActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var draft = ActiveDraft(request.DraftId, request.ExpectedRevision);
            EnsureDesignAuthority(Authoring(draft.DefinitionId));
            var discarded = Clone(draft);
            discarded.Revision = checked(discarded.Revision + 1);
            discarded.Status = ActivityDefinitionDraftStatus.Discarded;
            discarded.LastModifiedAt = DateTimeOffset.UtcNow;
            _drafts[request.DraftId] = discarded;
            _sequence++;
        }
        return Task.CompletedTask;
    }

    public Task ExecuteAsync(ActivityDraftValidationState validation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var draft = Draft(validation.DraftId);
            if (draft.Revision != validation.Revision)
                throw Conflict($"Draft '{validation.DraftId}' is at revision {draft.Revision}, not {validation.Revision}.");
            _validations[(validation.DraftId, validation.Revision)] = Clone(validation);
            _sequence++;
        }
        return Task.CompletedTask;
    }

    public Task<ActivityDefinitionVersionPublication> ExecuteAsync(
        ChangeActivityVersionLifecycleRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var current = Publication(request.DefinitionVersionId);
            if (current.Lifecycle != request.ExpectedLifecycle)
                throw Conflict($"Activity version '{request.DefinitionVersionId}' is {current.Lifecycle}, not {request.ExpectedLifecycle}.");
            if (!IsAllowedTransition(current.Lifecycle, request.Lifecycle))
                throw Conflict($"Activity version lifecycle cannot transition from {current.Lifecycle} to {request.Lifecycle}.");

            var updated = Clone(current);
            updated.Lifecycle = request.Lifecycle;
            updated.LastModifiedAt = DateTimeOffset.UtcNow;
            _publications[request.DefinitionVersionId] = updated;
            _sequence++;
            return Task.FromResult(Clone(updated));
        }
    }

    public Task<ActivityPublicationResult> ExecuteAsync(
        ActivityPublicationCommit<TExecutableTemplate, TSourceReference> commit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var mutation = commit.Design;

        lock (_gate)
        {
            var draft = ActiveDraft(mutation.DraftId, mutation.ExpectedDraftRevision);
            var authoring = Authoring(mutation.DefinitionId);
            EnsureDesignAuthority(authoring);
            EnsureExpectedHead(authoring, mutation.ExpectedDefinitionHeadVersionId);
            ValidatePublication(mutation, draft);

            if (_catalogVersions.ContainsKey(mutation.CatalogVersion.Id) ||
                _publications.ContainsKey(mutation.Publication.DefinitionVersionId) ||
                _versionLayouts.ContainsKey(mutation.Layout.DefinitionVersionId) ||
                _catalogVersions.Values.Any(x =>
                    StringComparer.Ordinal.Equals(x.DefinitionId, mutation.DefinitionId) &&
                    StringComparer.Ordinal.Equals(x.SemVerSortKey, mutation.CatalogVersion.SemVerSortKey)))
                throw Conflict($"Activity version '{mutation.CatalogVersion.Id}' already exists.");
            if (_sourceReferences.ContainsKey(mutation.Publication.SourceReferenceId))
                throw Conflict($"Source reference '{mutation.Publication.SourceReferenceId}' already exists.");
            if (mutation.DirectDependencies.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != mutation.DirectDependencies.Count ||
                mutation.DirectDependencies.Any(x => _edges.ContainsKey(x.Id)))
                throw Conflict("One or more direct dependency edge identities already exist.");

            _templates.TryAdd(mutation.Publication.TemplateId, commit.ExecutableTemplate);
            _sourceReferences.Add(mutation.Publication.SourceReferenceId, commit.SourceReference);
            _catalogVersions.Add(mutation.CatalogVersion.Id, Clone(mutation.CatalogVersion));
            _publications.Add(mutation.Publication.DefinitionVersionId, Clone(mutation.Publication));
            _versionLayouts.Add(mutation.Layout.DefinitionVersionId, Clone(mutation.Layout));
            foreach (var edge in mutation.DirectDependencies)
                _edges.Add(edge.Id, Clone(edge));

            var publishedDraft = Clone(draft);
            publishedDraft.Status = ActivityDefinitionDraftStatus.Published;
            publishedDraft.PublishedVersionId = mutation.Publication.DefinitionVersionId;
            publishedDraft.LastModifiedAt = mutation.Publication.PublishedAt;
            _drafts[draft.Id] = publishedDraft;

            var advanced = Clone(authoring);
            advanced.HeadVersionId = mutation.Publication.DefinitionVersionId;
            advanced.LastModifiedAt = mutation.Publication.PublishedAt;
            _authoring[mutation.DefinitionId] = advanced;
            _sequence++;

            return Task.FromResult(new ActivityPublicationResult(
                mutation.DefinitionId,
                mutation.Publication.DefinitionVersionId,
                mutation.DraftId,
                mutation.Publication.TemplateId,
                mutation.Publication.SourceReferenceId,
                mutation.Publication.PublishedAt));
        }
    }

    private IReadOnlyList<TraversedEdge> Traverse(
        string rootVersionId,
        ActivityDependencyDirection direction,
        bool transitive)
    {
        var result = new List<TraversedEdge>();
        var queue = new Queue<(string VersionId, int Depth, DependencyPathNode Path)>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { rootVersionId };
        var adjacency = _edges.Values
            .GroupBy(x => direction == ActivityDependencyDirection.Outbound ? x.OwnerVersionId : x.DependencyVersionId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(edge => edge.OccurrenceId, StringComparer.Ordinal).ThenBy(edge => edge.Id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        queue.Enqueue((rootVersionId, 1, new(rootVersionId, null)));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current.VersionId, out var edges)) continue;
            foreach (var edge in edges)
            {
                var next = direction == ActivityDependencyDirection.Outbound ? edge.DependencyVersionId : edge.OwnerVersionId;
                var path = new DependencyPathNode(next, current.Path);
                result.Add(new TraversedEdge(edge, current.Depth, path));
                if (transitive && visited.Add(next))
                    queue.Enqueue((next, current.Depth + 1, path));
            }

            if (!transitive)
                break;
        }

        return result
            .OrderBy(x => x.Depth)
            .ThenBy(x => x.Edge.OwnerVersionId, StringComparer.Ordinal)
            .ThenBy(x => x.Edge.OccurrenceId, StringComparer.Ordinal)
            .ThenBy(x => x.Edge.DependencyVersionId, StringComparer.Ordinal)
            .ThenBy(x => x.Edge.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> MaterializePath(DependencyPathNode path)
    {
        var values = new string[path.Depth];
        for (var current = path; current is not null; current = current.Parent)
            values[current.Depth - 1] = current.VersionId;
        return values;
    }

    private IEnumerable<ActivityDefinition> Apply(ActivityDefinitionFilter filter)
    {
        IEnumerable<ActivityDefinition> query = _definitions.Values;
        if (filter.Id is not null) query = query.Where(x => string.Equals(x.Id, filter.Id, StringComparison.Ordinal));
        if (filter.Ids is not null)
        {
            var ids = filter.Ids.ToHashSet(StringComparer.Ordinal);
            query = query.Where(x => ids.Contains(x.Id));
        }
        if (filter.ActivityTypeKey is not null)
            query = query.Where(x => string.Equals(x.ActivityTypeKey, filter.ActivityTypeKey, StringComparison.Ordinal));
        if (filter.ActivityTypeKeys is not null)
        {
            var keys = filter.ActivityTypeKeys.ToHashSet(StringComparer.Ordinal);
            query = query.Where(x => keys.Contains(x.ActivityTypeKey));
        }
        if (filter.Category is not null) query = query.Where(x => string.Equals(x.Category, filter.Category, StringComparison.Ordinal));
        if (filter.DisplayName is not null) query = query.Where(x => string.Equals(x.DisplayName, filter.DisplayName, StringComparison.Ordinal));
        if (filter.Description is not null) query = query.Where(x => x.Description?.Contains(filter.Description, StringComparison.OrdinalIgnoreCase) == true);
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            query = query.Where(x => new[] { x.Id, x.ActivityTypeKey, x.Category, x.DisplayName, x.Description }
                .Any(value => value?.Contains(filter.SearchTerm, StringComparison.OrdinalIgnoreCase) == true));
        return query.OrderBy(x => x.ActivityTypeKey, StringComparer.Ordinal);
    }

    private ActivityDependencyItem ToDependencyItem(
        ActivityDependencyEdge edge,
        int depth,
        IReadOnlyList<string> path) => new(
        edge.Id,
        ToReference(Publication(edge.OwnerVersionId)),
        ToReference(Publication(edge.DependencyVersionId)),
        new ActivityDependencyOccurrence(edge.OccurrenceId, edge.NodeOrigin.ToArray()),
        depth == 1,
        depth,
        path.Select(x => ToReference(Publication(x))).ToArray());

    private static ActivityDefinitionReference ToReference(ActivityDefinitionVersionPublication publication) => new(
        "ActivityVersion",
        publication.DefinitionId,
        publication.DefinitionVersionId,
        publication.Version,
        TemplateHash: publication.TemplateHash,
        TenantId: publication.TenantId,
        Lifecycle: publication.Lifecycle);

    private static void ValidateCreate(CreateActivityDefinitionRequest request)
    {
        if (!StringComparer.Ordinal.Equals(request.Definition.Id, request.AuthoringState.DefinitionId) ||
            !StringComparer.Ordinal.Equals(request.Definition.Id, request.InitialDraft.DefinitionId))
            throw new ArgumentException("Definition, authoring-state, and draft definition identities must match.", nameof(request));
        if (!StringComparer.Ordinal.Equals(request.Definition.TenantId, request.AuthoringState.TenantId) ||
            !StringComparer.Ordinal.Equals(request.Definition.TenantId, request.InitialDraft.TenantId))
            throw new ArgumentException("Definition, authoring-state, and draft tenants must match.", nameof(request));
        ValidateDraftAndLayout(request.InitialDraft, request.InitialLayout);
    }

    private static void ValidateDraftAndLayout(ActivityDefinitionDraft draft, ActivityDefinitionDraftLayout layout)
    {
        if (!StringComparer.Ordinal.Equals(draft.Id, layout.DraftId))
            throw new ArgumentException("Draft and layout identities must match.");
        if (draft.Revision != layout.Revision)
            throw new ArgumentException("Draft and layout revisions must match.");
        if (draft.Status != ActivityDefinitionDraftStatus.Active)
            throw new ArgumentException("A newly created draft must be active.");
    }

    private static void ValidatePublication(ActivityPublicationDesignMutation mutation, ActivityDefinitionDraft draft)
    {
        var publication = mutation.Publication;
        if (!StringComparer.Ordinal.Equals(draft.DefinitionId, mutation.DefinitionId) ||
            !StringComparer.Ordinal.Equals(mutation.CatalogVersion.DefinitionId, mutation.DefinitionId) ||
            !StringComparer.Ordinal.Equals(publication.DefinitionId, mutation.DefinitionId))
            throw new ArgumentException("Publication definition identities must match.", nameof(mutation));
        if (!StringComparer.Ordinal.Equals(mutation.CatalogVersion.Id, publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(mutation.Layout.DefinitionVersionId, publication.DefinitionVersionId))
            throw new ArgumentException("Publication version identities must match.", nameof(mutation));
        if (!StringComparer.Ordinal.Equals(mutation.CatalogVersion.Version, publication.Version))
            throw new ArgumentException("Catalog and publication semantic versions must match.", nameof(mutation));
        if (publication.DirectDependencyCount != mutation.DirectDependencies.Count)
            throw new ArgumentException("Publication direct-dependency count must match the persisted edge set.", nameof(mutation));
        if (mutation.DirectDependencies.Any(x =>
                !StringComparer.Ordinal.Equals(x.OwnerVersionId, publication.DefinitionVersionId) ||
                !StringComparer.Ordinal.Equals(x.OwnerTemplateHash, publication.TemplateHash)))
            throw new ArgumentException("Every direct edge must belong to the published version and template.", nameof(mutation));
    }

    private ActivityDefinitionAuthoringState Authoring(string definitionId) =>
        _authoring.GetValueOrDefault(definitionId)
        ?? throw Missing($"Activity definition authoring state '{definitionId}' was not found.");

    private ActivityDefinitionDraft Draft(string draftId) =>
        _drafts.GetValueOrDefault(draftId)
        ?? throw Missing($"Activity draft '{draftId}' was not found.");

    private ActivityDefinitionDraft ActiveDraft(string draftId, long expectedRevision)
    {
        var draft = Draft(draftId);
        if (draft.Status != ActivityDefinitionDraftStatus.Active)
            throw Conflict($"Activity draft '{draftId}' is {draft.Status}, not Active.");
        if (draft.Revision != expectedRevision)
            throw Conflict($"Activity draft '{draftId}' is at revision {draft.Revision}, not {expectedRevision}.");
        return draft;
    }

    private ActivityDefinitionVersionPublication Publication(string definitionVersionId) =>
        _publications.GetValueOrDefault(definitionVersionId)
        ?? throw Missing($"Activity version publication '{definitionVersionId}' was not found.");

    private static void EnsureDesignAuthority(ActivityDefinitionAuthoringState authoring)
    {
        if (authoring.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
            throw Conflict($"Activity definition '{authoring.DefinitionId}' is owned by provider source '{authoring.ContentAuthority.AuthorityKey}'.");
    }

    private static void EnsureExpectedHead(ActivityDefinitionAuthoringState authoring, string? expectedHead)
    {
        if (!StringComparer.Ordinal.Equals(authoring.HeadVersionId, expectedHead))
            throw Conflict($"Activity definition '{authoring.DefinitionId}' head is '{authoring.HeadVersionId}', not '{expectedHead}'.");
    }

    private static bool IsAllowedTransition(ActivityDefinitionVersionLifecycle current, ActivityDefinitionVersionLifecycle next) =>
        (current, next) switch
        {
            (ActivityDefinitionVersionLifecycle.Active, ActivityDefinitionVersionLifecycle.Retired) => true,
            (ActivityDefinitionVersionLifecycle.Retired, ActivityDefinitionVersionLifecycle.Active) => true,
            (ActivityDefinitionVersionLifecycle.Active or ActivityDefinitionVersionLifecycle.Retired, ActivityDefinitionVersionLifecycle.Revoked) => true,
            _ => false
        };

    private static bool IsVisible(string? itemTenantId, string? tenantId) =>
        itemTenantId is null || StringComparer.Ordinal.Equals(itemTenantId, tenantId);

    private static ActivityDefinition Clone(ActivityDefinition source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        ActivityTypeKey = source.ActivityTypeKey,
        Category = source.Category,
        DisplayName = source.DisplayName,
        Description = source.Description,
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static ActivityDefinitionAuthoringState Clone(ActivityDefinitionAuthoringState source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        DefinitionId = source.DefinitionId,
        ContentAuthority = source.ContentAuthority,
        ForkedFrom = source.ForkedFrom,
        HeadVersionId = source.HeadVersionId,
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static ActivityDefinitionDraft Clone(ActivityDefinitionDraft source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        DefinitionId = source.DefinitionId,
        Revision = source.Revision,
        SourceVersionId = source.SourceVersionId,
        Status = source.Status,
        State = Clone(source.State),
        PublishedVersionId = source.PublishedVersionId,
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static ActivityDefinitionDraftLayout Clone(ActivityDefinitionDraftLayout source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        DraftId = source.DraftId,
        Revision = source.Revision,
        Records = source.Records.Select(Clone).ToList(),
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static ActivityLayoutRecord Clone(ActivityLayoutRecord source) => new(source.NodeId, source.Data.Clone());

    private static ActivityDraftValidationState Clone(ActivityDraftValidationState source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        DraftId = source.DraftId,
        Revision = source.Revision,
        ValidatedAt = source.ValidatedAt,
        Diagnostics = source.Diagnostics.Select(Clone).ToList(),
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static ActivityDefinitionVersion Clone(ActivityDefinitionVersion source) => new(
        source.Version,
        source.DefinitionId,
        executionType: source.ExecutionType)
    {
        Id = source.Id,
        TenantId = source.TenantId,
        ProviderKey = source.ProviderKey,
        ProviderSchemaVersion = source.ProviderSchemaVersion,
        ConsumerKey = source.ConsumerKey,
        ConsumerSchemaVersion = source.ConsumerSchemaVersion,
        DescriptorType = source.DescriptorType,
        DescriptorPayload = source.DescriptorPayload.Clone(),
        Inputs = source.Inputs.ToArray(),
        Outputs = source.Outputs.ToArray(),
        DesignFacets = source.DesignFacets.ToArray(),
        SourceKind = source.SourceKind,
        SourceId = source.SourceId,
        Hash = source.Hash,
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static ActivityDefinitionVersionPublication Clone(ActivityDefinitionVersionPublication source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        DefinitionVersionId = source.DefinitionVersionId,
        DefinitionId = source.DefinitionId,
        ActivityTypeKey = source.ActivityTypeKey,
        ResolutionKind = source.ResolutionKind,
        Version = source.Version,
        SourceDraftId = source.SourceDraftId,
        SourceVersionId = source.SourceVersionId,
        Contract = Clone(source.Contract),
        Provider = Clone(source.Provider),
        TemplateId = source.TemplateId,
        TemplateHash = source.TemplateHash,
        SourceReferenceId = source.SourceReferenceId,
        ProviderFingerprint = source.ProviderFingerprint,
        DirectDependencyCount = source.DirectDependencyCount,
        ClosedTemplateCount = source.ClosedTemplateCount,
        RuntimeRequirements = source.RuntimeRequirements.ToList(),
        ResourceMeasurements = source.ResourceMeasurements,
        ResumeTargetCount = source.ResumeTargetCount,
        Lifecycle = source.Lifecycle,
        PublishedAt = source.PublishedAt,
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static ActivityDefinitionVersionLayout Clone(ActivityDefinitionVersionLayout source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        DefinitionVersionId = source.DefinitionVersionId,
        Records = source.Records.Select(Clone).ToList(),
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static ActivityDependencyEdge Clone(ActivityDependencyEdge source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        OwnerVersionId = source.OwnerVersionId,
        OwnerTemplateHash = source.OwnerTemplateHash,
        DependencyVersionId = source.DependencyVersionId,
        DependencyTemplateHash = source.DependencyTemplateHash,
        OccurrenceId = source.OccurrenceId,
        NodeOrigin = source.NodeOrigin.ToList(),
        CreatedAt = source.CreatedAt,
        LastModifiedAt = source.LastModifiedAt
    };

    private static ActivityDefinitionDraftState Clone(ActivityDefinitionDraftState source) => new(
        Clone(source.Contract),
        Clone(source.Provider),
        source.Options.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));

    private static ActivityContract Clone(ActivityContract source) => new(
        source.ContractSchemaVersion,
        source.Inputs.Select(Clone).ToArray(),
        source.Outputs.Select(Clone).ToArray(),
        source.Outcomes.ToArray());

    private static ActivityInputContract Clone(ActivityInputContract source) => source with
    {
        Default = source.Default is null ? null : source.Default with { Value = source.Default.Value.Clone() },
        UiSpecifications = Clone(source.UiSpecifications)
    };

    private static ActivityOutputContract Clone(ActivityOutputContract source) => source with
    {
        UiSpecifications = Clone(source.UiSpecifications)
    };

    private static ActivityProviderManifest Clone(ActivityProviderManifest source) =>
        source with { Payload = source.Payload.Clone() };

    private static ActivityDiagnostic Clone(ActivityDiagnostic source) => source with
    {
        Location = source.Location is null
            ? null
            : source.Location with
            {
                NodeOrigin = source.Location.NodeOrigin?.ToArray(),
                DependencyPath = source.Location.DependencyPath?.ToArray()
            },
        Metadata = source.Metadata?.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)
    };

    private static System.Text.Json.JsonElement? Clone(System.Text.Json.JsonElement? source) =>
        source is { } value ? value.Clone() : null;

    private static InvalidOperationException Conflict(string message, Exception? innerException = null) =>
        new(message, innerException);

    private static KeyNotFoundException Missing(string message) => new(message);

    private sealed record TraversedEdge(ActivityDependencyEdge Edge, int Depth, DependencyPathNode Path);

    private sealed record DependencyPathNode(string VersionId, DependencyPathNode? Parent)
    {
        public int Depth { get; } = (Parent?.Depth ?? 0) + 1;
    }
}

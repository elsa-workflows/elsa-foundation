using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Core.Contracts;
using Groundwork.Query.Model;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork recovery path for the deliberately derived dependency view. It reads every current
/// activity/workflow source, asks activity providers to disclose their own manifest dependencies,
/// and replaces the projection at a new visible watermark.
/// </summary>
public sealed class GroundworkActivityDependencyProjectionRebuildCoordinator(
    GroundworkV2ActivityDesignStore activityStore,
    GroundworkDesignStorage designStorage,
    IPayloadSerializer payloadSerializer,
    IActivityDefinitionVersionPublicationStore publications,
    IActivityTemplateDependencyDiscovererRegistry discoverers,
    IActivityStructureService structureService,
    IActivityDependencyProjectionRebuilder rebuilder,
    IIdentityGenerator identityGenerator,
    TimeProvider timeProvider) : IActivityDependencyProjectionRebuildCoordinator
{
    private static readonly JsonSerializerOptions ActivityJson = GroundworkActivitiesDesignJson.Options;
    private readonly JsonSerializerOptions _workflowJson = GroundworkDesignDocumentSerialization.Create(payloadSerializer);

    private const string ActivityListByDefinitionQuery = "list-by-definition";
    private const string ActivityListByOwnerVersionQuery = "list-by-owner-version";

    public async Task<ActivityDependencyProjectionRebuild> RebuildAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<ActivityDependencyItem>();
        await AddActivityVersionsAsync(items, cancellationToken);
        await AddActivityDraftsAsync(items, cancellationToken);
        await AddWorkflowDraftsAsync(items, cancellationToken);
        await AddWorkflowVersionsAsync(items, cancellationToken);
        return await rebuilder.RebuildCurrentAsync(
            $"rebuild-{identityGenerator.Generate()}",
            timeProvider.GetUtcNow(),
            items.OrderBy(SortKey, StringComparer.Ordinal).ToArray(),
            cancellationToken);
    }

    private async Task AddActivityVersionsAsync(ICollection<ActivityDependencyItem> items, CancellationToken cancellationToken)
    {
        foreach (var document in await ListActivitiesAsync(
                     ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
                     ActivityListByOwnerVersionQuery,
                     ActivitiesDesignStorageManifest.ByOwnerVersionDocumentOrder,
                     cancellationToken))
        {
            var edge = DeserializeActivity<ActivityDependencyEdge>(document);
            var ownerPublication = await RequiredPublicationAsync(edge.OwnerVersionId, cancellationToken);
            var dependencyPublication = await RequiredPublicationAsync(edge.DependencyVersionId, cancellationToken);
            var owner = Reference(ownerPublication);
            var dependency = Reference(dependencyPublication);
            items.Add(new(
                edge.Id,
                owner,
                dependency,
                new(edge.OccurrenceId, edge.NodeOrigin.ToArray()),
                true,
                1,
                [owner, dependency],
                edge.MemberUsage.ToArray()));
        }
    }

    private async Task AddActivityDraftsAsync(ICollection<ActivityDependencyItem> items, CancellationToken cancellationToken)
    {
        foreach (var document in await ListActivitiesAsync(
                     ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                     ActivityListByDefinitionQuery,
                     ActivitiesDesignStorageManifest.ByDefinitionDocumentOrder,
                     cancellationToken))
        {
            var draft = DeserializeActivity<ActivityDefinitionDraft>(document);
            if (draft.Status != ActivityDefinitionDraftStatus.Active)
                continue;
            var discovery = await discoverers.Resolve(draft.State.Provider.ProviderKey, draft.State.Provider.SchemaVersion)
                .DiscoverDependenciesAsync(new(draft.DefinitionId, draft.Id, draft.Revision, draft.State.Provider), cancellationToken);
            var blocking = discovery.Diagnostics.Where(x => x.Severity == ActivityDiagnosticSeverity.Error).ToArray();
            if (blocking.Length != 0)
                throw new InvalidOperationException($"Activity draft '{draft.Id}' dependency discovery failed: {string.Join(", ", blocking.Select(x => x.Code))}.");
            var owner = new ActivityDefinitionReference(
                "ActivityDraft", draft.DefinitionId, DraftId: draft.Id, Revision: draft.Revision, TenantId: draft.TenantId);
            foreach (var declaration in discovery.Dependencies)
            {
                var dependency = Reference(await RequiredPublicationAsync(declaration.DefinitionVersionId, cancellationToken));
                items.Add(DirectItem(owner, dependency, declaration.OccurrenceId, declaration.NodeOrigin, declaration.MemberUsage));
            }
        }
    }

    private async Task AddWorkflowDraftsAsync(ICollection<ActivityDependencyItem> items, CancellationToken cancellationToken)
    {
        // The workflow-draft kind has no doc-id-sorted route after the by-collection removal; a zero-clause
        // traversal on the by-definition route (declared drafts order) still visits every draft across
        // definitions, and the rebuild re-keys the result so only completeness matters, not the visit order.
        var unit = WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind;
        foreach (var entry in ListWorkflows(
                     unit,
                     [
                         designStorage.Order(unit, WorkflowsDesignStorageManifest.DraftDefinitionIdField),
                         designStorage.Order(unit, WorkflowsDesignStorageManifest.DraftLastModifiedAtField, descending: true),
                         designStorage.Order(unit, WorkflowsDesignStorageManifest.DraftCreatedAtField, descending: true),
                         designStorage.Order(unit, WorkflowsDesignStorageManifest.DraftIdField, descending: true)
                     ],
                     WorkflowsDesignStorageManifest.DraftByDefinitionIndex,
                     cancellationToken))
        {
            var draft = designStorage.MapDraft(entry, _workflowJson);
            var owner = new ActivityDefinitionReference(
                "WorkflowDraft",
                draft.WorkflowDefinitionId,
                DraftId: draft.Id,
                Revision: entry.Entry.Version,
                TenantId: draft.TenantId);
            await AddWorkflowStateAsync(items, owner, draft.State, cancellationToken);
        }
    }

    private async Task AddWorkflowVersionsAsync(ICollection<ActivityDependencyItem> items, CancellationToken cancellationToken)
    {
        // As with drafts, the workflow-version kind keeps no doc-id-sorted route; the by-definition route's
        // declared version order performs the exhaustive traversal, and only completeness matters here.
        var unit = WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind;
        foreach (var entry in ListWorkflows(
                     unit,
                     [
                         designStorage.Order(unit, WorkflowsDesignStorageManifest.VersionDefinitionIdField),
                         designStorage.Order(unit, WorkflowsDesignStorageManifest.VersionSemVerSortKeyField),
                         designStorage.Order(unit, WorkflowsDesignStorageManifest.VersionIdField)
                     ],
                     WorkflowsDesignStorageManifest.VersionByDefinitionIndex,
                     cancellationToken))
        {
            var version = designStorage.MapVersion(entry, _workflowJson);
            var owner = new ActivityDefinitionReference(
                "WorkflowVersion",
                version.DefinitionId,
                version.Id,
                version.Version,
                TenantId: version.TenantId);
            await AddWorkflowStateAsync(items, owner, version.State, cancellationToken);
        }
    }

    private async Task AddWorkflowStateAsync(
        ICollection<ActivityDependencyItem> items,
        ActivityDefinitionReference owner,
        WorkflowDefinitionState state,
        CancellationToken cancellationToken)
    {
        if (state.RootActivity is null)
            return;
        var stack = new Stack<(ActivityNode Node, IReadOnlyCollection<ActivityContractMemberUsage> StructureMemberUsage)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        stack.Push((state.RootActivity, []));
        while (stack.TryPop(out var entry))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = entry.Node;
            if (!seen.Add(node.NodeId))
                throw new InvalidOperationException($"Workflow owner '{owner.DraftId ?? owner.VersionId}' has duplicate node id '{node.NodeId}'.");
            // Workflow graphs contain every activity kind. Only exact reusable-activity publications
            // participate in this projection; CLR/provider catalog activities remain ordinary nodes.
            var publication = await publications.FindAsync(node.ActivityVersionId, cancellationToken);
            if (publication is not null)
                items.Add(DirectItem(
                    owner,
                    Reference(publication),
                    node.NodeId,
                    [new("AuthoredNode", node.NodeId)],
                    PublicMemberUsage(node)
                        .Concat(entry.StructureMemberUsage)
                        .Distinct()
                        .OrderBy(x => x.MemberKind, StringComparer.Ordinal)
                        .ThenBy(x => x.ReferenceKey, StringComparer.Ordinal)
                        .ToArray()));
            var structureUsage = structureService.ProjectChildContractMemberUsage(node)
                .GroupBy(x => x.NodeId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyCollection<ActivityContractMemberUsage>)group
                        .SelectMany(x => x.MemberUsage)
                        .Distinct()
                        .ToArray(),
                    StringComparer.Ordinal);
            foreach (var slot in structureService.ProjectChildren(node).Reverse())
                foreach (var child in slot.Activities.Reverse())
                    stack.Push((
                        child,
                        structureUsage.GetValueOrDefault(child.NodeId) ?? []));
        }
    }

    // Deterministic full-kind traversal on a declared route: a clause-free bounded request the route
    // admits, exhausted with the route's own declared order.
    private Task<IReadOnlyList<ActivityDesignDocument>> ListActivitiesAsync(
        string kind,
        string queryIdentity,
        IReadOnlyList<ActivityDesignQueryOrder> order,
        CancellationToken cancellationToken) =>
        ActivityDesignQueryPager.QueryAllAsync(activityStore, kind, queryIdentity, [], order, cancellationToken);

    /// <summary>
    /// Walks a workflow-design route end to end. The clause-free predicate is how an exhaustive traversal
    /// is spelled in the public v2 query model: it admits every row the named index covers, in that index's
    /// own order, rather than asking the provider for an unrouted scan.
    /// </summary>
    private IReadOnlyList<GroundworkDesignEntry> ListWorkflows(
        string unitId,
        IReadOnlyList<OrderTerm> order,
        string index,
        CancellationToken cancellationToken) =>
        designStorage.Query(
            unitId,
            Predicate.AlwaysTrue.Instance,
            order,
            index,
            cancellationToken: cancellationToken);

    private async Task<ActivityDefinitionVersionPublication> RequiredPublicationAsync(string versionId, CancellationToken cancellationToken) =>
        await publications.FindAsync(versionId, cancellationToken)
        ?? throw new InvalidOperationException($"Activity publication '{versionId}' referenced by the dependency projection is unavailable.");

    private static T DeserializeActivity<T>(ActivityDesignDocument document) where T : Entity =>
        JsonSerializer.Deserialize<GroundworkV2ActivityDesignDocument<T>>(document.ContentJson, ActivityJson)?.Entity
        ?? throw new InvalidOperationException($"Activity document '{document.Id}' is unreadable.");

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
        string occurrenceId,
        IReadOnlyList<ActivityNodeOrigin> origin,
        IReadOnlyList<ActivityContractMemberUsage>? memberUsage = null) => new(
        $"{owner.Kind}:{owner.DraftId ?? owner.VersionId}:{occurrenceId}:{dependency.VersionId}",
        owner,
        dependency,
        new(occurrenceId, origin),
        true,
        1,
        [owner, dependency],
        memberUsage);

    private static IReadOnlyList<ActivityContractMemberUsage> PublicMemberUsage(ActivityNode node) =>
        node.Inputs
            .Select(x => new ActivityContractMemberUsage("Input", x.ReferenceKey, "Bound"))
            .Concat(node.Outputs.Select(x => new ActivityContractMemberUsage("Output", x.ReferenceKey, "Bound")))
            .Where(x => !string.IsNullOrWhiteSpace(x.ReferenceKey))
            .Distinct()
            .OrderBy(x => x.MemberKind, StringComparer.Ordinal)
            .ThenBy(x => x.ReferenceKey, StringComparer.Ordinal)
            .ToArray();

    private static string SortKey(ActivityDependencyItem item) =>
        $"{item.Owner.Kind}\u001f{item.Owner.DraftId ?? item.Owner.VersionId}\u001f{item.Occurrence.OccurrenceId}\u001f{item.Dependency.VersionId}";
}

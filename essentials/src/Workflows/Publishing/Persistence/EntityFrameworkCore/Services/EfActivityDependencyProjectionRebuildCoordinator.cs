using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;

/// <summary>
/// EF Core recovery path for the deliberately derived dependency view. It reads every current
/// activity/workflow source, asks activity providers to disclose their own manifest dependencies, and
/// replaces the projection at a new visible watermark.
/// </summary>
/// <remarks>
/// <para>
/// The rebuild replaces one global projection row that holds every tenant's facts, so it requires the
/// explicit privileged-across-scopes persistence context the Activities Design EF projection writer
/// demands of any replacement. That precondition is checked here, before the first read, so a caller in
/// an ordinary tenant scope is refused rather than converging the projection onto a partial view of the
/// catalog.
/// </para>
/// <para>
/// Restart safety comes from the replacement being one row written in one transaction at a sequence
/// derived from the sequence it read: an interrupted rebuild leaves the previous projection intact and
/// readable at its own watermark, and a rerun recomputes the whole view from current sources, so no work
/// is lost and none is applied twice. A rebuild can never move the sequence backwards, and two rebuilds
/// cannot share one sequence under different identities; the writer refuses both.
/// </para>
/// </remarks>
public sealed class EfActivityDependencyProjectionRebuildCoordinator(
    ActivitiesDesignDbContext activitiesDb,
    WorkflowsDesignDbContext workflowsDb,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IPayloadSerializer payloadSerializer,
    IActivityDefinitionVersionPublicationStore publications,
    IActivityTemplateDependencyDiscovererRegistry discoverers,
    IActivityStructureService structureService,
    IActivityDependencyProjectionRebuilder rebuilder,
    IIdentityGenerator identityGenerator,
    TimeProvider timeProvider) : IActivityDependencyProjectionRebuildCoordinator
{
    public async Task<ActivityDependencyProjectionRebuild> RebuildAsync(CancellationToken cancellationToken = default)
    {
        EnsurePrivilegedAcrossScopes();
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

    private void EnsurePrivilegedAcrossScopes()
    {
        var context = accessContextAccessor.Current;
        if (context.AccessPolicy != PersistenceAccessPolicy.Privileged || !context.AcrossScopes)
            throw new InvalidOperationException(
                "Rebuilding the activity dependency projection replaces one row holding every tenant's facts; it requires an explicit privileged-across-scopes persistence context.");
    }

    private async Task AddActivityVersionsAsync(ICollection<ActivityDependencyItem> items, CancellationToken cancellationToken)
    {
        foreach (var edge in await EfUpgradeKeysetPager.ReadAllAsync(activitiesDb, activitiesDb.ActivityDependencyEdges.AsNoTracking(), cancellationToken))
        {
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
        foreach (var draft in await EfUpgradeKeysetPager.ReadAllAsync(activitiesDb, activitiesDb.ActivityDefinitionDrafts.AsNoTracking(), cancellationToken))
        {
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
        foreach (var draft in await EfUpgradeKeysetPager.ReadAllAsync(workflowsDb, workflowsDb.Drafts.AsNoTracking(), cancellationToken))
        {
            var owner = new ActivityDefinitionReference(
                "WorkflowDraft",
                draft.WorkflowDefinitionId,
                DraftId: draft.Id,
                Revision: EfWorkflowDraftRevision.Of(draft.LastModifiedAt),
                TenantId: draft.TenantId);
            await AddWorkflowStateAsync(items, owner, ReadState(draft.StateSource, "WorkflowDraft", draft.Id), cancellationToken);
        }
    }

    private async Task AddWorkflowVersionsAsync(ICollection<ActivityDependencyItem> items, CancellationToken cancellationToken)
    {
        foreach (var version in await EfUpgradeKeysetPager.ReadAllAsync(workflowsDb, workflowsDb.Versions.AsNoTracking(), cancellationToken))
        {
            var owner = new ActivityDefinitionReference(
                "WorkflowVersion",
                version.DefinitionId,
                version.Id,
                version.Version,
                TenantId: version.TenantId);
            await AddWorkflowStateAsync(items, owner, ReadState(version.StateSource, "WorkflowVersion", version.Id), cancellationToken);
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
                    stack.Push((child, structureUsage.GetValueOrDefault(child.NodeId) ?? []));
        }
    }

    /// <summary>
    /// A workflow row with no serialized state is drift, not an empty graph: rebuilding around it would
    /// silently drop that owner's dependencies from the converged view.
    /// </summary>
    private WorkflowDefinitionState ReadState(string? source, string kind, string id)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidOperationException($"{kind} '{id}' has no serialized workflow state to rebuild its dependencies from.");
        return payloadSerializer.Deserialize<WorkflowDefinitionState>(source);
    }

    private async Task<ActivityDefinitionVersionPublication> RequiredPublicationAsync(string versionId, CancellationToken cancellationToken) =>
        await publications.FindAsync(versionId, cancellationToken)
        ?? throw new InvalidOperationException($"Activity publication '{versionId}' referenced by the dependency projection is unavailable.");

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
        $"{item.Owner.Kind}{item.Owner.DraftId ?? item.Owner.VersionId}{item.Occurrence.OccurrenceId}{item.Dependency.VersionId}";
}

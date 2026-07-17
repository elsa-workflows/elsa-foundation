using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Core.Contracts;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork bridge for authoritative upgrade discovery and one atomic activity/workflow draft
/// mutation. Opaque activity manifests are delegated to their provider-owned reference rewriter.
/// </summary>
public sealed class GroundworkActivityUpgradePlanStore(
    IDocumentStore store,
    IBoundedDocumentStore boundedStore,
    IPayloadSerializer payloadSerializer,
    IActivityDependencyProjectionStore dependencyProjection,
    GroundworkActivityDependencyProjection dependencyProjectionWriter,
    IActivityDefinitionDraftStore activityDrafts,
    IActivityDefinitionAuthoringStore activityAuthoring,
    IActivityDefinitionVersionPublicationStore activityVersions,
    IActivityDefinitionLayoutStore activityLayouts,
    IWorkflowDefinitionVersionStore workflowVersions,
    IWorkflowDefinitionVersionLayoutStore workflowLayouts,
    IActivityStructureService structureService,
    IActivityProviderRegistry activityProviders,
    ActivityContractAuthoringValidator contractValidator,
    IEnumerable<IActivityProviderReferenceRewriter> activityRewriters,
    IIdentityGenerator identityGenerator) : IActivityUpgradeDiscoverySource, IActivityUpgradePlanMutationStore
{
    private static readonly JsonSerializerOptions ActivityJson = GroundworkActivitiesDesignJson.Options;
    private readonly JsonSerializerOptions _workflowJson = GroundworkDesignDocumentSerialization.Create(payloadSerializer);

    public async ValueTask<ActivityUpgradeDiscovery> DiscoverAsync(
        ActivityUpgradePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ActivityDiagnostic>();
        var replacements = new Dictionary<string, ActivityVersionReplacement>(StringComparer.Ordinal);
        foreach (var replacement in request.Replacements)
        {
            var from = await activityVersions.FindAsync(replacement.FromVersionId, cancellationToken);
            var to = await activityVersions.FindAsync(replacement.ToVersionId, cancellationToken);
            if (from is null || to is null || !StringComparer.Ordinal.Equals(from.DefinitionId, to.DefinitionId))
            {
                diagnostics.Add(Diagnostic("activity.upgrade.replacement-invalid", "Replacement versions must exist in the same activity definition.", "ActivityVersion", replacement.FromVersionId));
                continue;
            }
            if (to.Lifecycle != ActivityDefinitionVersionLifecycle.Active)
            {
                diagnostics.Add(Diagnostic("activity.upgrade.target-not-active", "The replacement target must be active.", "ActivityVersion", to.DefinitionVersionId));
                continue;
            }
            replacements.Add(replacement.FromVersionId, replacement);
        }

        var candidateItems = new List<ActivityDependencyItem>();
        foreach (var fromVersionId in replacements.Keys.Order(StringComparer.Ordinal))
        {
            var offset = 0;
            string? watermark = null;
            while (true)
            {
                var page = await dependencyProjection.ReadAsync(new(
                    fromVersionId,
                    new(ActivityDependencyDirection.Inbound, request.IncludeTransitiveDependents, new HashSet<string>(["Versions", "Drafts"], StringComparer.Ordinal)),
                    request.TenantId,
                    watermark,
                    offset,
                    500), cancellationToken);
                watermark ??= page.Watermark;
                candidateItems.AddRange(page.Items);
                if (page.NextOffset is null)
                    break;
                offset = page.NextOffset.Value;
            }
        }

        var owners = new List<ActivityUpgradeOwnerSnapshot>();
        foreach (var root in request.Roots)
        {
            var scopedReferences = candidateItems
                .Where(x => MatchesRoot(x.Owner, root) || request.IncludeTransitiveDependents && x.Path.Count > 0 && MatchesRoot(x.Path[0], root))
                .SelectMany(x => x.Path.Append(x.Owner))
                .Select(ReferenceKey)
                .ToHashSet(StringComparer.Ordinal);
            var matching = candidateItems.Where(x => scopedReferences.Contains(ReferenceKey(x.Owner))).ToArray();
            if (matching.Length == 0)
            {
                diagnostics.Add(Diagnostic("activity.upgrade.root-not-affected", "The selected root does not depend on any requested source version.", root.Kind, root.Id));
                continue;
            }

            foreach (var group in matching.GroupBy(x => ReferenceKey(x.Owner), StringComparer.Ordinal))
            {
                var item = group.First();
                var direct = group.Where(x => replacements.ContainsKey(x.Dependency.VersionId!))
                    .Select(x => new ActivityUpgradeOccurrenceReplacement(
                        x.Occurrence.OccurrenceId,
                        x.Dependency.VersionId!,
                        replacements[x.Dependency.VersionId!].ToVersionId))
                    .Distinct()
                    .ToArray();
                try
                {
                    var owner = await ResolveOwnerAsync(item.Owner, direct, item.Path, direct.Length == 0, request.CreateDraftsForPublishedDependents, cancellationToken);
                    if (owner is null)
                        diagnostics.Add(Diagnostic("activity.upgrade.owner-unavailable", "The affected owner could not be read or is not mutable under this plan.", item.Owner.Kind, item.Owner.DraftId ?? item.Owner.VersionId ?? item.Owner.DefinitionId));
                    else
                        owners.Add(owner);
                }
                catch (ActivityUpgradeApplyException exception)
                {
                    diagnostics.Add(Diagnostic(
                        exception.ErrorCode,
                        exception.Message,
                        item.Owner.Kind,
                        item.Owner.DraftId ?? item.Owner.VersionId ?? item.Owner.DefinitionId));
                }
            }
        }

        return new(
            owners.GroupBy(x => ReferenceKey(x.Owner), StringComparer.Ordinal).Select(x => x.First()).ToArray(),
            diagnostics.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Subject.Id, StringComparer.Ordinal).ToArray());
    }

    public async ValueTask<ActivityUpgradeApplyResult> ApplyAsync(
        ActivityUpgradePlan plan,
        IReadOnlyList<ActivityUpgradeStep> selectedSteps,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken = default)
    {
        var planEnvelope = await RequiredAsync(ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind, plan.PlanId, cancellationToken);
        var persistedPlan = JsonSerializer.Deserialize<UpgradePlanDocument>(planEnvelope.ContentJson, ActivityJson)?.Plan
                            ?? throw new InvalidOperationException($"Upgrade plan '{plan.PlanId}' is unreadable.");
        if (!SamePlan(persistedPlan, plan) || persistedPlan.Status != ActivityUpgradePlanStatus.Ready)
            throw Stale("The upgrade plan changed before apply.");

        await RecheckTargetsAsync(plan.Replacements, cancellationToken);
        var requests = new List<SaveDocumentRequest>();
        var scopes = new HashSet<string>(StringComparer.Ordinal) { ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind };
        var applied = new List<ActivityUpgradeAppliedDraft>();
        foreach (var step in selectedSteps.OrderBy(x => x.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (step.Diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error))
                throw new ActivityUpgradeApplyException(422, "activity.upgrade.plan-blocked", "A selected upgrade step is blocked.", step.Diagnostics);
            switch (step.Action)
            {
                case ActivityUpgradeAction.UpdateDraft when step.Target.Kind == "ActivityDraft":
                    await StageActivityDraftUpdateAsync(step, requests, scopes, applied, appliedAt, cancellationToken);
                    break;
                case ActivityUpgradeAction.CloneActivityVersion:
                    await StageActivityDraftCloneAsync(step, requests, scopes, applied, appliedAt, cancellationToken);
                    break;
                case ActivityUpgradeAction.UpdateDraft when step.Target.Kind == "WorkflowDraft":
                    await StageWorkflowDraftUpdateAsync(step, requests, scopes, applied, appliedAt, cancellationToken);
                    break;
                case ActivityUpgradeAction.CloneWorkflowVersion:
                    await StageWorkflowDraftCloneAsync(step, requests, scopes, applied, appliedAt, cancellationToken);
                    break;
                default:
                    throw new ActivityUpgradeApplyException(422, "activity.upgrade.step-unsupported", $"Upgrade step '{step.StepId}' is unsupported.");
            }
        }

        ActivityDependencyProjectionPreparedUpdate projectionUpdate;
        try
        {
            projectionUpdate = await dependencyProjectionWriter.PrepareUpdateAsync(
                BuildProjectionChanges(selectedSteps, applied, plan.TenantId),
                appliedAt,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw Stale(exception.Message);
        }
        requests.Add(projectionUpdate.Request);
        scopes.Add(ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind);

        var appliedPlan = plan with { Status = ActivityUpgradePlanStatus.Applied, AppliedAt = appliedAt, AppliedDrafts = applied.ToArray() };
        requests.Add(SaveSimple(
            ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind,
            plan.PlanId,
            new UpgradePlanDocument(ActivitiesDesignStorageManifest.ActivityUpgradePlanCollection, appliedPlan),
            planEnvelope.Version));
        try
        {
            await store.SaveAllAsync(DocumentCommitScope.Of(scopes.ToArray()), requests, cancellationToken);
        }
        catch (DocumentAtomicWriteException)
        {
            throw Stale("One or more upgrade snapshots changed before the atomic commit.");
        }
        return new(plan.PlanId, ActivityUpgradePlanStatus.Applied, appliedAt, applied, []);
    }

    private static IReadOnlyList<ActivityDependencyProjectionOwnerChange> BuildProjectionChanges(
        IReadOnlyList<ActivityUpgradeStep> selectedSteps,
        IReadOnlyList<ActivityUpgradeAppliedDraft> appliedDrafts,
        string? tenantId)
    {
        var ordered = selectedSteps.OrderBy(x => x.Order).ToArray();
        if (ordered.Length != appliedDrafts.Count)
            throw new InvalidOperationException("Every selected upgrade step must produce one draft projection owner.");
        return ordered.Select((step, index) =>
        {
            var source = step.Action switch
            {
                ActivityUpgradeAction.CloneActivityVersion => new ActivityDefinitionReference(
                    "ActivityVersion", step.Target.DefinitionId, VersionId: step.Target.SourceVersionId, TenantId: tenantId),
                ActivityUpgradeAction.CloneWorkflowVersion => new ActivityDefinitionReference(
                    "WorkflowVersion", step.Target.DefinitionId, VersionId: step.Target.SourceVersionId, TenantId: tenantId),
                _ => new ActivityDefinitionReference(
                    step.Target.Kind, step.Target.DefinitionId, DraftId: step.Target.DraftId, Revision: step.ExpectedRevision, TenantId: tenantId)
            };
            var applied = appliedDrafts[index];
            var result = new ActivityDefinitionReference(
                applied.Kind,
                applied.DefinitionId,
                DraftId: applied.DraftId,
                Revision: applied.Revision,
                TenantId: tenantId);
            return new ActivityDependencyProjectionOwnerChange(source, result, step.Replacements);
        }).ToArray();
    }

    private async Task<ActivityUpgradeOwnerSnapshot?> ResolveOwnerAsync(
        ActivityDefinitionReference owner,
        IReadOnlyList<ActivityUpgradeOccurrenceReplacement> direct,
        IReadOnlyList<ActivityDefinitionReference> path,
        bool blocked,
        bool allowClone,
        CancellationToken cancellationToken)
    {
        switch (owner.Kind)
        {
            case "ActivityDraft":
                {
                    var draft = await activityDrafts.FindAsync(owner.DraftId!, cancellationToken);
                    if (draft is null || draft.Status != ActivityDefinitionDraftStatus.Active)
                        return null;
                    var authoring = await activityAuthoring.FindAsync(draft.DefinitionId, cancellationToken);
                    var layout = await activityLayouts.FindDraftLayoutAsync(draft.Id, cancellationToken);
                    if (authoring is null || layout is null)
                        return null;
                    var candidate = blocked || direct.Count == 0
                        ? null
                        : new ActivityDraftDiffCandidateRequest(
                            draft.DefinitionId,
                            draft.Id,
                            draft.Revision + 1,
                            draft.State with { Provider = await RewriteActivityAsync(draft.State.Provider, direct, cancellationToken) },
                            layout.Records.ToArray(),
                            authoring.HeadVersionId);
                    return new(owner with { Revision = draft.Revision, TenantId = draft.TenantId }, authoring.HeadVersionId, draft.SourceVersionId, direct, path, blocked, DiffCandidate: candidate);
                }
            case "ActivityVersion" when allowClone:
                {
                    var version = await activityVersions.FindAsync(owner.VersionId!, cancellationToken);
                    if (version is null)
                        return null;
                    var authoring = await activityAuthoring.FindAsync(version.DefinitionId, cancellationToken);
                    if (authoring?.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
                        return null;
                    var layout = await activityLayouts.FindVersionLayoutAsync(version.DefinitionVersionId, cancellationToken);
                    var candidate = blocked || direct.Count == 0
                        ? null
                        : new ActivityDraftDiffCandidateRequest(
                            version.DefinitionId,
                            $"upgrade-preview:{version.DefinitionVersionId}",
                            1,
                            new(version.Contract, await RewriteActivityAsync(version.Provider, direct, cancellationToken), new Dictionary<string, string>()),
                            layout?.Records.ToArray() ?? [],
                            version.DefinitionVersionId);
                    return new(owner with { TenantId = version.TenantId }, authoring.HeadVersionId, version.DefinitionVersionId, direct, path, blocked, DiffCandidate: candidate);
                }
            case "WorkflowDraft":
                {
                    var envelope = await store.LoadAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, owner.DraftId!, cancellationToken);
                    if (envelope is null)
                        return null;
                    var document = DeserializeWorkflowDraft(envelope);
                    var head = await FindWorkflowHeadAsync(document.Entity.WorkflowDefinitionId, cancellationToken);
                    return new(owner with { DefinitionId = document.Entity.WorkflowDefinitionId, Revision = envelope.Version, TenantId = document.Entity.TenantId }, head, document.Entity.SourceVersionId, direct, path, blocked);
                }
            case "WorkflowVersion" when allowClone:
                {
                    var version = await workflowVersions.FindByIdAsync(owner.VersionId!, cancellationToken);
                    if (version is null)
                        return null;
                    return new(owner with { DefinitionId = version.DefinitionId, TenantId = version.TenantId }, await FindWorkflowHeadAsync(version.DefinitionId, cancellationToken), version.Id, direct, path, blocked);
                }
            default:
                return null;
        }
    }

    private async Task StageActivityDraftUpdateAsync(
        ActivityUpgradeStep step,
        ICollection<SaveDocumentRequest> requests,
        ISet<string> scopes,
        ICollection<ActivityUpgradeAppliedDraft> applied,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var draftEnvelope = await RequiredAsync(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, step.Target.DraftId!, cancellationToken);
        var draft = DeserializeActivity<ActivityDefinitionDraft>(draftEnvelope);
        var layoutEnvelope = await RequiredActivityByIndexAsync<ActivityDefinitionDraftLayout>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind,
            "list-by-draft",
            ActivitiesDesignStorageManifest.DraftIdField,
            draft.Id,
            cancellationToken);
        var layout = DeserializeActivity<ActivityDefinitionDraftLayout>(layoutEnvelope);
        var authoringEnvelope = await RequiredActivityByIndexAsync<ActivityDefinitionAuthoringState>(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            "list-by-definition",
            ActivitiesDesignStorageManifest.DefinitionIdField,
            draft.DefinitionId,
            cancellationToken);
        var authoring = DeserializeActivity<ActivityDefinitionAuthoringState>(authoringEnvelope);
        EnsureActivitySnapshot(step, draft, authoring);
        draft.State = draft.State with { Provider = await RewriteActivityAsync(draft.State.Provider, step.Replacements, cancellationToken) };
        EnsureMutableActivityState(draft);
        draft.Revision++;
        draft.LastModifiedAt = now;
        layout.Revision = draft.Revision;
        layout.LastModifiedAt = now;
        requests.Add(SaveActivity(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, draft, draftEnvelope.Version));
        requests.Add(SaveActivity(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, layout, layoutEnvelope.Version));
        scopes.Add(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind);
        scopes.Add(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind);
        applied.Add(new("ActivityDraft", draft.Id, draft.DefinitionId, draft.Revision, false));
    }

    private async Task StageActivityDraftCloneAsync(
        ActivityUpgradeStep step,
        ICollection<SaveDocumentRequest> requests,
        ISet<string> scopes,
        ICollection<ActivityUpgradeAppliedDraft> applied,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var source = await activityVersions.FindAsync(step.Target.SourceVersionId!, cancellationToken) ?? throw Stale("The source activity version is unavailable.");
        var authoring = await activityAuthoring.FindAsync(source.DefinitionId, cancellationToken) ?? throw Stale("The source activity definition is unavailable.");
        if (authoring.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
            throw new ActivityUpgradeApplyException(422, "activity.upgrade.source-owned-definition", "A source-owned activity version must be forked before it can be upgraded.");
        if (!StringComparer.Ordinal.Equals(authoring.HeadVersionId, step.ExpectedDefinitionHeadVersionId))
            throw Stale("The activity definition head changed.");
        var id = identityGenerator.Generate();
        var draft = new ActivityDefinitionDraft
        {
            Id = id,
            DefinitionId = source.DefinitionId,
            TenantId = source.TenantId,
            Revision = 1,
            SourceVersionId = source.DefinitionVersionId,
            State = new(source.Contract, await RewriteActivityAsync(source.Provider, step.Replacements, cancellationToken), new Dictionary<string, string>()),
            CreatedAt = now,
            LastModifiedAt = now
        };
        var sourceLayout = await activityLayouts.FindVersionLayoutAsync(source.DefinitionVersionId, cancellationToken);
        var layout = new ActivityDefinitionDraftLayout { Id = identityGenerator.Generate(), DraftId = id, TenantId = source.TenantId, Revision = 1, Records = sourceLayout?.Records.ToList() ?? [], CreatedAt = now, LastModifiedAt = now };
        EnsureMutableActivityState(draft);
        requests.Add(SaveActivity(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, draft, 0));
        requests.Add(SaveActivity(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, layout, 0));
        scopes.Add(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind);
        scopes.Add(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind);
        applied.Add(new("ActivityDraft", id, source.DefinitionId, 1, true));
    }

    private void EnsureMutableActivityState(ActivityDefinitionDraft draft)
    {
        IActivityProvider provider;
        try
        {
            provider = activityProviders.Resolve(draft.State.Provider.ProviderKey, draft.State.Provider.SchemaVersion);
        }
        catch (InvalidOperationException)
        {
            throw new ActivityUpgradeApplyException(
                422,
                "activity.provider.schema-unavailable",
                "The upgraded draft's exact provider schema is unavailable for authoring.");
        }
        if (provider.AuthoringCapabilities.ManifestSchemas.All(x =>
                !StringComparer.Ordinal.Equals(x.SchemaVersion, draft.State.Provider.SchemaVersion) || !x.IsAuthorable))
            throw new ActivityUpgradeApplyException(
                422,
                "activity.provider.schema-not-authorable",
                "The upgraded draft's exact provider schema is not authorable.");

        var diagnostics = contractValidator.Validate(
            draft.State.Contract,
            new("ActivityDraft", draft.Id, draft.DefinitionId, Revision: draft.Revision));
        if (diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error))
            throw new ActivityUpgradeApplyException(
                422,
                "activity.contract.capability-rejected",
                "The upgraded mutable contract uses unavailable authoring capabilities.",
                diagnostics);
    }

    private async Task StageWorkflowDraftUpdateAsync(
        ActivityUpgradeStep step,
        ICollection<SaveDocumentRequest> requests,
        ISet<string> scopes,
        ICollection<ActivityUpgradeAppliedDraft> applied,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var envelope = await RequiredAsync(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind, step.Target.DraftId!, cancellationToken);
        var document = DeserializeWorkflowDraft(envelope);
        if (envelope.Version != step.ExpectedRevision || !StringComparer.Ordinal.Equals(await FindWorkflowHeadAsync(document.Entity.WorkflowDefinitionId, cancellationToken), step.ExpectedDefinitionHeadVersionId))
            throw Stale("The workflow draft revision or definition head changed.");
        document.Entity.State = RewriteWorkflow(document.Entity.State, step.Replacements, cancellationToken);
        document.Entity.LastModifiedAt = now;
        requests.Add(SaveWorkflowDraft(document, envelope.Version));
        scopes.Add(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind);
        applied.Add(new("WorkflowDraft", document.Entity.Id, document.Entity.WorkflowDefinitionId, envelope.Version + 1, false));
    }

    private async Task StageWorkflowDraftCloneAsync(
        ActivityUpgradeStep step,
        ICollection<SaveDocumentRequest> requests,
        ISet<string> scopes,
        ICollection<ActivityUpgradeAppliedDraft> applied,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var source = await workflowVersions.FindByIdAsync(step.Target.SourceVersionId!, cancellationToken) ?? throw Stale("The source workflow version is unavailable.");
        if (!StringComparer.Ordinal.Equals(await FindWorkflowHeadAsync(source.DefinitionId, cancellationToken), step.ExpectedDefinitionHeadVersionId))
            throw Stale("The workflow definition head changed.");
        var id = identityGenerator.Generate();
        var layout = await workflowLayouts.FindByVersionIdAsync(source.Id, cancellationToken);
        var draft = new WorkflowDefinitionDraft
        {
            Id = id,
            WorkflowDefinitionId = source.DefinitionId,
            TenantId = source.TenantId,
            SourceVersionId = source.Id,
            State = RewriteWorkflow(source.State, step.Replacements, cancellationToken),
            CreatedAt = now,
            LastModifiedAt = now
        };
        requests.Add(SaveWorkflowDraft(new(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftCollection, draft, layout?.Records.ToArray() ?? []), 0));
        scopes.Add(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind);
        applied.Add(new("WorkflowDraft", id, source.DefinitionId, 1, true));
    }

    private async Task RecheckTargetsAsync(IEnumerable<ActivityVersionReplacement> replacements, CancellationToken cancellationToken)
    {
        foreach (var replacement in replacements)
        {
            var from = await activityVersions.FindAsync(replacement.FromVersionId, cancellationToken);
            var to = await activityVersions.FindAsync(replacement.ToVersionId, cancellationToken);
            if (from is null || to is null || to.Lifecycle != ActivityDefinitionVersionLifecycle.Active || !StringComparer.Ordinal.Equals(from.DefinitionId, to.DefinitionId) || !StringComparer.Ordinal.Equals(from.TenantId, to.TenantId))
                throw Stale("An exact replacement identity, lifecycle, or tenant snapshot changed.");
        }
    }

    private void EnsureActivitySnapshot(ActivityUpgradeStep step, ActivityDefinitionDraft draft, ActivityDefinitionAuthoringState authoring)
    {
        if (draft.Revision != step.ExpectedRevision || draft.Status != ActivityDefinitionDraftStatus.Active ||
            !StringComparer.Ordinal.Equals(authoring.HeadVersionId, step.ExpectedDefinitionHeadVersionId))
            throw Stale("The activity draft revision or definition head changed.");
    }

    private async ValueTask<ActivityProviderManifest> RewriteActivityAsync(ActivityProviderManifest manifest, IReadOnlyList<ActivityUpgradeOccurrenceReplacement> replacements, CancellationToken cancellationToken)
    {
        var matches = activityRewriters.Where(x => StringComparer.Ordinal.Equals(x.ProviderKey, manifest.ProviderKey) && x.SupportedManifestSchemas.Contains(manifest.SchemaVersion)).ToArray();
        if (matches.Length != 1)
            throw new ActivityUpgradeApplyException(422, "activity.upgrade.provider-rewriter-unavailable", "Exactly one provider-owned reference rewriter is required.");
        return await matches[0].RewriteReferencesAsync(manifest, replacements, cancellationToken);
    }

    private WorkflowDefinitionState RewriteWorkflow(WorkflowDefinitionState state, IReadOnlyList<ActivityUpgradeOccurrenceReplacement> replacements, CancellationToken cancellationToken)
    {
        if (state.RootActivity is null)
            throw new ActivityUpgradeApplyException(422, "activity.upgrade.occurrence-not-found", "The workflow has no activity graph to rewrite.");
        var byOccurrence = replacements.ToDictionary(x => x.OccurrenceId, StringComparer.Ordinal);
        var updated = new Dictionary<string, ActivityNode>(StringComparer.Ordinal);
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<(ActivityNode Node, bool Visited)>();
        stack.Push((state.RootActivity, false));
        while (stack.TryPop(out var frame))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!frame.Visited)
            {
                stack.Push((frame.Node, true));
                foreach (var slot in structureService.ProjectChildren(frame.Node).Reverse())
                    foreach (var child in slot.Activities.Reverse())
                        stack.Push((child, false));
                continue;
            }
            var slots = structureService.ProjectChildren(frame.Node).Select(x => x with { Activities = x.Activities.Select(child => updated[child.NodeId]).ToArray() }).ToArray();
            var node = structureService.ReplaceChildren(frame.Node, slots);
            if (byOccurrence.TryGetValue(node.NodeId, out var replacement))
            {
                if (!StringComparer.Ordinal.Equals(node.ActivityVersionId, replacement.FromVersionId))
                    throw Stale($"Workflow occurrence '{node.NodeId}' changed before apply.");
                node = node with { ActivityVersionId = replacement.ToVersionId };
                matched.Add(node.NodeId);
            }
            if (!updated.TryAdd(node.NodeId, node))
                throw new ActivityUpgradeApplyException(422, "activity.upgrade.node-id-duplicate", $"Workflow node '{node.NodeId}' is not unique.");
        }
        if (byOccurrence.Keys.Any(x => !matched.Contains(x)))
            throw Stale("One or more planned workflow occurrences no longer exist.");
        return state with { RootActivity = updated[state.RootActivity.NodeId] };
    }

    private async Task<string?> FindWorkflowHeadAsync(string definitionId, CancellationToken cancellationToken)
    {
        var versions = await workflowVersions.ListByDefinitionAsync(definitionId, cancellationToken);
        return versions.OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal).ThenByDescending(x => x.Id, StringComparer.Ordinal).FirstOrDefault()?.Id;
    }

    private static bool MatchesRoot(ActivityDefinitionReference reference, ActivityUpgradeRoot root) =>
        StringComparer.Ordinal.Equals(reference.Kind, root.Kind) &&
        StringComparer.Ordinal.Equals(reference.DraftId ?? reference.VersionId, root.Id);
    private static string ReferenceKey(ActivityDefinitionReference reference) => $"{reference.Kind}\u001f{reference.DraftId ?? reference.VersionId}";

    private async Task<DocumentEnvelope> RequiredAsync(string kind, string id, CancellationToken cancellationToken) =>
        await store.LoadAsync(kind, id, cancellationToken) ?? throw Stale($"Required snapshot '{kind}/{id}' is unavailable.");

    private async Task<DocumentEnvelope> RequiredActivityByIndexAsync<TEntity>(
        string kind,
        string queryIdentity,
        string fieldPath,
        string value,
        CancellationToken cancellationToken) where TEntity : Entity
    {
        var matches = await boundedStore.QueryAsync(
            new DocumentQuery(
                kind,
                queryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value))]),
            cancellationToken);
        return matches.Documents.Count == 1
            ? matches.Documents[0]
            : throw Stale($"Expected one '{kind}' snapshot for '{value}'.");
    }

    private static TEntity DeserializeActivity<TEntity>(DocumentEnvelope envelope) where TEntity : Entity =>
        JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(envelope.ContentJson, ActivityJson)?.Entity
        ?? throw new InvalidOperationException($"Activity document '{envelope.Id}' is unreadable.");

    private WorkflowDraftDocument DeserializeWorkflowDraft(DocumentEnvelope envelope) =>
        JsonSerializer.Deserialize<WorkflowDraftDocument>(envelope.ContentJson, _workflowJson)
        ?? throw new InvalidOperationException($"Workflow draft '{envelope.Id}' is unreadable.");

    private static SaveDocumentRequest SaveActivity<TEntity>(string kind, string collection, TEntity entity, long expectedVersion) where TEntity : Entity
    {
        var request = GroundworkDocumentWriter.ToSaveRequest(kind, collection, ActivitiesDesignStorageManifest.SchemaVersion, entity, ActivityJson);
        return new(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, expectedVersion);
    }

    private static SaveDocumentRequest SaveSimple<T>(string kind, string id, T value, long expectedVersion)
    {
        var request = JsonDocumentStoreExtensions.ToSaveDocumentRequest(kind, id, ActivitiesDesignStorageManifest.SchemaVersion, value, ActivityJson);
        return new(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, expectedVersion);
    }

    private SaveDocumentRequest SaveWorkflowDraft(WorkflowDraftDocument document, long expectedVersion)
    {
        var request = JsonDocumentStoreExtensions.ToSaveDocumentRequest(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind,
            document.Entity.Id,
            WorkflowsDesignStorageManifest.SchemaVersion,
            document,
            _workflowJson);
        return new(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, expectedVersion);
    }

    private static ActivityDiagnostic Diagnostic(string code, string message, string kind, string id) => new(
        code, ActivityDiagnosticSeverity.Error, message, new ActivityDiagnosticSubject(kind, id));
    private static ActivityUpgradeApplyException Stale(string message) => new(409, "activity.upgrade.stale-plan", message);

    private static bool SamePlan(ActivityUpgradePlan left, ActivityUpgradePlan right) =>
        JsonNode.DeepEquals(
            JsonNode.Parse(JsonSerializer.Serialize(left, ActivityJson)),
            JsonNode.Parse(JsonSerializer.Serialize(right, ActivityJson)));

    private sealed record UpgradePlanDocument(string Collection, ActivityUpgradePlan Plan);
    private sealed record WorkflowDraftDocument(string Collection, WorkflowDefinitionDraft Entity, IReadOnlyCollection<DesignMetadataRecord> Layout);
}

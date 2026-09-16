using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Constants;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Locking.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;

/// <summary>
/// EF Core bridge for authoritative upgrade discovery and one atomic activity/workflow draft mutation.
/// It is the counterpart of the Groundwork bridge the Publishing Groundwork feature registers, and keeps
/// the same semantics: bounded deterministic discovery, compare-and-swap on every snapshot the plan
/// asserts, an idempotent apply whose result is written with the plan and its receipt, and a derived
/// dependency projection that can never be left behind by a committed source mutation.
/// </summary>
/// <remarks>
/// <para>
/// Atomicity across the two Design catalogs is owned by <see cref="EfSharedTransaction"/>: one physical
/// connection, one transaction, both contexts enlisted, one commit or one rollback. A host that splits
/// Activities Design and Workflows Design across databases is refused when the transaction opens, which
/// is the same refusal Groundwork makes when its lanes name different targets.
/// </para>
/// <para>
/// Opaque activity manifests are never interpreted here; they are handed to their provider-owned
/// reference rewriter, exactly as in Groundwork.
/// </para>
/// </remarks>
public sealed class EfActivityUpgradePlanStore(
    ActivitiesDesignDbContext activitiesDb,
    WorkflowsDesignDbContext workflowsDb,
    IPersistenceAccessContextAccessor accessContextAccessor,
    IPayloadSerializer payloadSerializer,
    IActivityStructureService structureService,
    IActivityProviderRegistry activityProviders,
    ActivityContractAuthoringValidator contractValidator,
    IEnumerable<IActivityProviderReferenceRewriter> activityRewriters,
    IIdentityGenerator identityGenerator,
    IDistributedLockProvider lockProvider,
    TimeProvider? timeProvider = null) :
    IActivityUpgradeDiscoverySource,
    IActivityUpgradePlanMutationStore,
    IActivityUpgradePublishedDraftResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int DiscoveryPageSize = 500;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    private EfActivityUpgradeLane Ambient() => new(activitiesDb, workflowsDb, accessContextAccessor, payloadSerializer);

    public ValueTask<ActivityUpgradeDiscovery> DiscoverAsync(
        ActivityUpgradePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DiscoverAsync(Ambient(), request, cancellationToken);
    }

    public async ValueTask<ActivityUpgradePublishedDraft?> ResolveAsync(
        string publishedVersionId,
        string expectedKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedVersionId);
        var lane = Ambient();
        if (expectedKind == "ActivityVersion")
        {
            var activity = await Publications(lane).FindAsync(publishedVersionId, cancellationToken);
            return activity?.SourceDraftId is null
                ? null
                : new("ActivityVersion", activity.SourceDraftId, activity.DefinitionId, activity.DefinitionVersionId);
        }

        if (expectedKind != "WorkflowVersion")
            return null;
        var workflow = await lane.WorkflowVersions.FindByIdAsync(publishedVersionId, cancellationToken);
        return workflow?.SourceDraftId is null
            ? null
            : new("WorkflowVersion", workflow.SourceDraftId, workflow.DefinitionId, workflow.Id);
    }

    public async ValueTask<ActivityUpgradeApplyResult> ApplyAsync(
        ActivityUpgradePlan plan,
        IReadOnlyList<ActivityUpgradeStep> selectedSteps,
        ActivityUpgradeApplyReceipt receipt,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selectedSteps);
        ArgumentNullException.ThrowIfNull(receipt);

        var ambient = Ambient();
        await EnsurePlanAndReceiptAreCurrentAsync(ambient, plan, receipt, cancellationToken);
        await RecheckPlanBindingAsync(ambient, plan, cancellationToken);
        await RecheckTargetsAsync(ambient, plan.Replacements, cancellationToken);

        // The stage must be exactly one, and exactly the receipt's, before anything is locked or opened.
        var appliedStageIds = selectedSteps.Select(x => x.StageId).Where(x => x is not null).ToHashSet(StringComparer.Ordinal);
        if (appliedStageIds.Count != 1 || !appliedStageIds.Contains(receipt.StageId))
            throw new ActivityUpgradeApplyException(422, "activity.upgrade.stage-invalid", "One exact stage must be applied per operation.");

        await using var applyLocks = await AcquireApplyLocksAsync(ambient, plan, cancellationToken);
        return await CommitAsync(plan, selectedSteps, receipt, appliedAt, cancellationToken);
    }

    /// <summary>
    /// Commits the upgrade as one act across both design catalogs. The plan binding and its targets are
    /// rechecked inside the transaction, so nothing they assert can change between the check and the
    /// commit — which is the whole point of applying an upgrade atomically rather than draft by draft.
    /// <para>
    /// Every write is staged and flushed as the step that produced it is processed, so a later step that
    /// finds drift rolls back the earlier lane's rows with it. Nothing leaves the transaction half-applied,
    /// and a rejected apply writes nothing at all.
    /// </para>
    /// </summary>
    private async Task<ActivityUpgradeApplyResult> CommitAsync(
        ActivityUpgradePlan plan,
        IReadOnlyList<ActivityUpgradeStep> selectedSteps,
        ActivityUpgradeApplyReceipt receipt,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        await using var shared = await EfSharedTransaction.BeginAsync([activitiesDb, workflowsDb], cancellationToken);
        var lane = new EfActivityUpgradeLane(
            shared.Context<ActivitiesDesignDbContext>(),
            shared.Context<WorkflowsDesignDbContext>(),
            accessContextAccessor,
            payloadSerializer);
        try
        {
            var planRow = await TrackedPlanRowAsync(lane, plan.PlanId, cancellationToken);
            var receiptRow = await TrackedReceiptRowAsync(lane, receipt.ReceiptId, cancellationToken);
            EnsurePlanIsCurrent(planRow.PlanJson, plan);
            EnsureReceiptIsCurrent(receiptRow.ReceiptJson, receipt);
            await RecheckPlanBindingAsync(lane, plan, cancellationToken);
            await RecheckTargetsAsync(lane, plan.Replacements, cancellationToken);

            var applied = new List<ActivityUpgradeAppliedDraft>();
            var changedActivityDrafts = new List<ActivityDefinitionDraft>();
            foreach (var step in selectedSteps.OrderBy(x => x.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (step.Diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error))
                    throw new ActivityUpgradeApplyException(422, "activity.upgrade.plan-blocked", "A selected upgrade step is blocked.", step.Diagnostics);
                switch (step.Action)
                {
                    case ActivityUpgradeAction.UpdateDraft when step.Target.Kind == "ActivityDraft":
                        await UpdateActivityDraftAsync(lane, step, applied, changedActivityDrafts, appliedAt, cancellationToken);
                        break;
                    case ActivityUpgradeAction.CloneActivityVersion:
                        await CloneActivityVersionAsync(lane, step, applied, changedActivityDrafts, appliedAt, cancellationToken);
                        break;
                    case ActivityUpgradeAction.UpdateDraft when step.Target.Kind == "WorkflowDraft":
                        await UpdateWorkflowDraftAsync(lane, step, applied, appliedAt, cancellationToken);
                        break;
                    case ActivityUpgradeAction.CloneWorkflowVersion:
                        await CloneWorkflowVersionAsync(lane, step, applied, appliedAt, cancellationToken);
                        break;
                    default:
                        throw new ActivityUpgradeApplyException(422, "activity.upgrade.step-unsupported", $"Upgrade step '{step.StepId}' is unsupported.");
                }

                await FlushAsync(lane, cancellationToken);
            }

            await ApplyDependencyProjectionAsync(
                lane,
                BuildProjectionChanges(selectedSteps, applied, plan.TenantId),
                appliedAt,
                cancellationToken);

            var result = BuildResult(plan, selectedSteps, applied, receipt, appliedAt, out var appliedPlan);
            planRow.PlanJson = JsonSerializer.Serialize(appliedPlan, Json);
            receiptRow.ReceiptJson = JsonSerializer.Serialize(
                receipt with
                {
                    Status = ActivityUpgradeApplyReceiptStatus.Applied,
                    UpdatedAt = appliedAt,
                    Revision = checked(receipt.Revision + 1),
                    Result = result
                },
                Json);

            if (changedActivityDrafts.Count != 0)
                await new EfActivityManagementProjectionWriter(lane.Activities, accessContextAccessor)
                    .WriteInCurrentTransactionAsync(new(appliedAt, [], changedActivityDrafts, []), cancellationToken);
            await FlushAsync(lane, cancellationToken);
            await shared.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (IsWriteConflict(exception))
        {
            throw Stale("One or more upgrade snapshots changed before the atomic commit.");
        }
    }

    private static async Task FlushAsync(EfActivityUpgradeLane lane, CancellationToken cancellationToken)
    {
        if (lane.Workflows.ChangeTracker.HasChanges())
            await lane.Workflows.SaveChangesAsync(cancellationToken);
        if (lane.Activities.ChangeTracker.HasChanges())
            await lane.Activities.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A failure that means the durable state moved under this apply. Every one of them leaves the shared
    /// transaction uncommitted, so the plan, its receipt and both catalogs are exactly as they were.
    /// <para>
    /// A commit whose outcome is unknown is deliberately not one of them. Reporting it as a stale plan
    /// would tell the caller nothing was written when the transaction may in fact have committed; it is
    /// raised as itself, so the durable Preparing receipt and its lease remain the only authority on what
    /// happened.
    /// </para>
    /// </summary>
    private static bool IsWriteConflict(Exception exception)
    {
        if (exception is ActivityUpgradeApplyException or OperationCanceledException or EfCommitOutcomeUnknownException)
            return false;
        if (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception) ||
            EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            return true;
        for (var current = (Exception?)exception; current is not null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException or EfSharedTransactionRollbackOnlyException)
                return true;
        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // Discovery
    // ---------------------------------------------------------------------------------------------

    private async ValueTask<ActivityUpgradeDiscovery> DiscoverAsync(
        EfActivityUpgradeLane lane,
        ActivityUpgradePlanRequest request,
        CancellationToken cancellationToken)
    {
        var publications = Publications(lane);
        var diagnostics = new List<ActivityDiagnostic>();
        var replacements = new Dictionary<string, ActivityVersionReplacement>(StringComparer.Ordinal);
        foreach (var replacement in request.Replacements)
        {
            var from = await publications.FindAsync(replacement.FromVersionId, cancellationToken);
            var to = await publications.FindAsync(replacement.ToVersionId, cancellationToken);
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
                cancellationToken.ThrowIfCancellationRequested();
                var page = await lane.Design.ReadAsync(new(
                    fromVersionId,
                    new(ActivityDependencyDirection.Inbound, request.IncludeTransitiveDependents, new HashSet<string>(["Versions", "Drafts"], StringComparer.Ordinal)),
                    request.TenantId,
                    watermark,
                    offset,
                    DiscoveryPageSize), cancellationToken);
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

            foreach (var group in matching.GroupBy(x => $"{ReferenceKey(x.Owner)}{PathKey(x.Path)}", StringComparer.Ordinal))
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
                    var owner = await ResolveOwnerAsync(lane, item.Owner, direct, item.Path, direct.Length == 0, request.CreateDraftsForPublishedDependents, cancellationToken);
                    if (owner is null)
                        diagnostics.Add(Diagnostic("activity.upgrade.owner-unavailable", "The affected owner could not be read or is not mutable under this plan.", item.Owner.Kind, item.Owner.DraftId ?? item.Owner.VersionId ?? item.Owner.DefinitionId));
                    else
                        owners.Add(owner);
                }
                catch (ActivityUpgradeApplyException exception)
                {
                    diagnostics.Add(Diagnostic(exception.ErrorCode, exception.Message, item.Owner.Kind, item.Owner.DraftId ?? item.Owner.VersionId ?? item.Owner.DefinitionId));
                }
            }
        }

        return new(
            owners.ToArray(),
            diagnostics.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Subject.Id, StringComparer.Ordinal).ToArray());
    }

    private async Task<ActivityUpgradeOwnerSnapshot?> ResolveOwnerAsync(
        EfActivityUpgradeLane lane,
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
                var draft = await Drafts(lane).FindAsync(owner.DraftId!, cancellationToken);
                if (draft is null || draft.Status != ActivityDefinitionDraftStatus.Active)
                    return null;
                var authoring = await Authoring(lane).FindAsync(draft.DefinitionId, cancellationToken);
                var layout = await lane.Design.FindDraftLayoutAsync(draft.Id, cancellationToken);
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
                return new(owner with { Revision = draft.Revision, TenantId = draft.TenantId }, authoring.HeadVersionId, draft.SourceVersionId, direct, [path], blocked, DiffCandidate: candidate);
            }
            case "ActivityVersion" when allowClone:
            {
                var version = await Publications(lane).FindAsync(owner.VersionId!, cancellationToken);
                if (version is null)
                    return null;
                var authoring = await Authoring(lane).FindAsync(version.DefinitionId, cancellationToken);
                if (authoring?.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
                    return null;
                var layout = await lane.Design.FindVersionLayoutAsync(version.DefinitionVersionId, cancellationToken);
                var candidate = blocked || direct.Count == 0
                    ? null
                    : new ActivityDraftDiffCandidateRequest(
                        version.DefinitionId,
                        $"upgrade-preview:{version.DefinitionVersionId}",
                        1,
                        new(version.Contract, await RewriteActivityAsync(version.Provider, direct, cancellationToken), new Dictionary<string, string>()),
                        layout?.Records.ToArray() ?? [],
                        version.DefinitionVersionId);
                return new(owner with { TenantId = version.TenantId }, authoring.HeadVersionId, version.DefinitionVersionId, direct, [path], blocked, DiffCandidate: candidate);
            }
            case "WorkflowDraft":
            {
                var draft = await lane.WorkflowDrafts.FindByIdAsync(owner.DraftId!, cancellationToken);
                if (draft is null)
                    return null;
                var head = await FindWorkflowHeadAsync(lane, draft.WorkflowDefinitionId, cancellationToken);
                return new(
                    owner with
                    {
                        DefinitionId = draft.WorkflowDefinitionId,
                        Revision = EfWorkflowDraftRevision.Of(draft.LastModifiedAt),
                        TenantId = draft.TenantId
                    },
                    head,
                    draft.SourceVersionId,
                    direct,
                    [path],
                    blocked);
            }
            case "WorkflowVersion" when allowClone:
            {
                var version = await lane.WorkflowVersions.FindByIdAsync(owner.VersionId!, cancellationToken);
                if (version is null)
                    return null;
                return new(
                    owner with { DefinitionId = version.DefinitionId, TenantId = version.TenantId },
                    await FindWorkflowHeadAsync(lane, version.DefinitionId, cancellationToken),
                    version.Id,
                    direct,
                    [path],
                    blocked);
            }
            default:
                return null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Staging
    // ---------------------------------------------------------------------------------------------

    private async Task UpdateActivityDraftAsync(
        EfActivityUpgradeLane lane,
        ActivityUpgradeStep step,
        ICollection<ActivityUpgradeAppliedDraft> applied,
        ICollection<ActivityDefinitionDraft> managementDrafts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var draft = await SingleAsync(
            EfActivityUpgradeScope.ById(Scoped(lane.Activities.ActivityDefinitionDrafts), step.Target.DraftId!),
            $"ActivityDraft/{step.Target.DraftId}",
            cancellationToken);
        var layout = await SingleAsync(
            EfActivityUpgradeScope.ByReference(Scoped(lane.Activities.ActivityDefinitionDraftLayouts), nameof(ActivityDefinitionDraftLayout.DraftId), draft.Id),
            $"ActivityDraftLayout/{draft.Id}",
            cancellationToken);
        var authoring = await SingleAsync(
            EfActivityUpgradeScope.ByReference(Scoped(lane.Activities.ActivityDefinitionAuthoringStates), nameof(ActivityDefinitionAuthoringState.DefinitionId), draft.DefinitionId),
            $"ActivityAuthoring/{draft.DefinitionId}",
            cancellationToken);
        if (draft.Revision != step.ExpectedRevision ||
            draft.Status != ActivityDefinitionDraftStatus.Active ||
            !StringComparer.Ordinal.Equals(authoring.HeadVersionId, step.ExpectedDefinitionHeadVersionId))
            throw Stale("The activity draft revision or definition head changed.");

        draft.State = draft.State with { Provider = await RewriteActivityAsync(draft.State.Provider, step.Replacements, cancellationToken) };
        EnsureMutableActivityState(draft);
        draft.Revision = checked(draft.Revision + 1);
        draft.LastModifiedAt = now;
        layout.Revision = draft.Revision;
        layout.LastModifiedAt = now;
        applied.Add(new("ActivityDraft", draft.Id, draft.DefinitionId, draft.Revision, false, draft.SourceVersionId));
        managementDrafts.Add(draft);
    }

    private async Task CloneActivityVersionAsync(
        EfActivityUpgradeLane lane,
        ActivityUpgradeStep step,
        ICollection<ActivityUpgradeAppliedDraft> applied,
        ICollection<ActivityDefinitionDraft> managementDrafts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var source = await Publications(lane).FindAsync(step.Target.SourceVersionId!, cancellationToken)
                     ?? throw Stale("The source activity version is unavailable.");
        var authoring = await Authoring(lane).FindAsync(source.DefinitionId, cancellationToken)
                        ?? throw Stale("The source activity definition is unavailable.");
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
        EnsureMutableActivityState(draft);
        var sourceLayout = await lane.Design.FindVersionLayoutAsync(source.DefinitionVersionId, cancellationToken);
        var layout = new ActivityDefinitionDraftLayout
        {
            Id = identityGenerator.Generate(),
            DraftId = id,
            TenantId = source.TenantId,
            Revision = 1,
            Records = sourceLayout?.Records.ToList() ?? [],
            CreatedAt = now,
            LastModifiedAt = now
        };
        lane.Activities.ActivityDefinitionDrafts.Add(draft);
        lane.Activities.ActivityDefinitionDraftLayouts.Add(layout);
        applied.Add(new("ActivityDraft", id, source.DefinitionId, 1, true, source.DefinitionVersionId));
        managementDrafts.Add(draft);
    }

    /// <summary>
    /// Rewrites one workflow draft under a compare-and-swap on the exact instant the row was read at.
    /// The draft carries no EF concurrency token, so the swap is an explicit conditional update: it either
    /// changes exactly one row or reports the drift the plan was built against.
    /// </summary>
    private async Task UpdateWorkflowDraftAsync(
        EfActivityUpgradeLane lane,
        ActivityUpgradeStep step,
        ICollection<ActivityUpgradeAppliedDraft> applied,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var draft = await lane.WorkflowDrafts.FindByIdAsync(step.Target.DraftId!, cancellationToken)
                    ?? throw Stale($"Required snapshot 'WorkflowDraft/{step.Target.DraftId}' is unavailable.");
        var observed = draft.LastModifiedAt;
        if (EfWorkflowDraftRevision.Of(observed) != step.ExpectedRevision ||
            !StringComparer.Ordinal.Equals(await FindWorkflowHeadAsync(lane, draft.WorkflowDefinitionId, cancellationToken), step.ExpectedDefinitionHeadVersionId))
            throw Stale("The workflow draft revision or definition head changed.");

        var state = RewriteWorkflow(draft.State, step.Replacements, cancellationToken);
        var stateSource = lane.Payloads.Serialize(state);
        var stamped = EfWorkflowDraftRevision.Next(observed, now);
        // The read entity is only the source of the compare-and-swap operands; the conditional update below
        // is the write, so it must not linger in the tracker with stale values.
        lane.Workflows.Entry(draft).State = EntityState.Detached;
        var affected = await WorkflowDraftRow(lane, draft.Id, draft.TenantId)
            .Where(x => x.LastModifiedAt == observed)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(x => x.StateSource, stateSource)
                    .SetProperty(x => x.LastModifiedAt, stamped),
                cancellationToken);
        if (affected != 1)
            throw Stale("The workflow draft revision or definition head changed.");
        applied.Add(new("WorkflowDraft", draft.Id, draft.WorkflowDefinitionId, EfWorkflowDraftRevision.Of(stamped), false, draft.SourceVersionId));
    }

    private async Task CloneWorkflowVersionAsync(
        EfActivityUpgradeLane lane,
        ActivityUpgradeStep step,
        ICollection<ActivityUpgradeAppliedDraft> applied,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var source = await lane.WorkflowVersions.FindByIdAsync(step.Target.SourceVersionId!, cancellationToken)
                     ?? throw Stale("The source workflow version is unavailable.");
        if (!StringComparer.Ordinal.Equals(await FindWorkflowHeadAsync(lane, source.DefinitionId, cancellationToken), step.ExpectedDefinitionHeadVersionId))
            throw Stale("The workflow definition head changed.");

        var id = identityGenerator.Generate();
        var sourceLayout = await lane.WorkflowVersionLayouts.FindByVersionIdAsync(source.Id, cancellationToken);
        var stamped = EfWorkflowDraftRevision.Align(now);
        var state = RewriteWorkflow(source.State, step.Replacements, cancellationToken);
        var draft = new WorkflowDefinitionDraft
        {
            Id = id,
            WorkflowDefinitionId = source.DefinitionId,
            TenantId = source.TenantId,
            SourceVersionId = source.Id,
            State = state,
            StateSource = lane.Payloads.Serialize(state),
            CreatedAt = stamped,
            LastModifiedAt = stamped
        };
        var layout = WorkflowDefinitionDraftLayout.CreateFor(
            identityGenerator,
            id,
            sourceLayout?.Records.ToArray() ?? [],
            sourceLayout?.ActivityPresentation.ToArray() ?? []);
        layout.TenantId = source.TenantId;
        layout.CreatedAt = stamped;
        layout.LastModifiedAt = stamped;
        // Layout columns carry the same Web-defaults JSON the Workflows Design EF lane writes them with.
        layout.RecordsJson = JsonSerializer.Serialize(layout.Records.ToArray(), JsonSerializerOptions.Web);
        layout.ActivityPresentationJson = JsonSerializer.Serialize(layout.ActivityPresentation.ToArray(), JsonSerializerOptions.Web);
        lane.Workflows.Drafts.Add(draft);
        lane.Workflows.DraftLayouts.Add(layout);
        applied.Add(new("WorkflowDraft", id, source.DefinitionId, EfWorkflowDraftRevision.Of(stamped), true, source.Id));
    }

    // ---------------------------------------------------------------------------------------------
    // Derived dependency projection
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Replaces each mutated owner's derived dependency facts in the same transaction as the source
    /// mutation, so a committed upgrade can never leave the derived view pointing at the versions it
    /// replaced. The projection row is one global row; an owner is matched on its tenant as well as its
    /// kind and identity, exactly as the EF publication commit matches it.
    /// </summary>
    private async Task ApplyDependencyProjectionAsync(
        EfActivityUpgradeLane lane,
        IReadOnlyList<ActivityDependencyProjectionOwnerChange> changes,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var publications = Publications(lane);
        var current = await EfActivityUpgradeScope
            .ById(lane.Activities.ActivityDependencyProjections, ActivityDependencyProjectionState.CurrentId)
            .SingleOrDefaultAsync(cancellationToken);
        var items = current?.Items.ToList() ?? [];

        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = items.Where(x => SameOwner(x.Owner, change.SourceOwner)).ToArray();
            items.RemoveAll(x => SameOwner(x.Owner, change.SourceOwner));
            var replacements = change.Replacements.ToDictionary(x => x.OccurrenceId, StringComparer.Ordinal);
            var matched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in source)
            {
                var dependency = item.Dependency;
                if (replacements.TryGetValue(item.Occurrence.OccurrenceId, out var replacement))
                {
                    if (!StringComparer.Ordinal.Equals(dependency.VersionId, replacement.FromVersionId))
                        throw Stale($"Projection occurrence '{replacement.OccurrenceId}' no longer references the planned source version.");
                    var target = await publications.FindAsync(replacement.ToVersionId, cancellationToken)
                                 ?? throw Stale($"Projection target version '{replacement.ToVersionId}' is unavailable.");
                    dependency = Reference(target);
                    matched.Add(replacement.OccurrenceId);
                }

                items.Add(DirectItem(change.ResultOwner, dependency, item.Occurrence, item.MemberUsage));
            }

            if (replacements.Keys.Any(x => !matched.Contains(x)))
                throw Stale("One or more planned projection occurrences are unavailable.");
        }

        ValidateProjectionItems(items);
        var ordered = items.OrderBy(ItemSortKey, StringComparer.Ordinal).ToList();
        if (current is null)
        {
            lane.Activities.ActivityDependencyProjections.Add(new ActivityDependencyProjectionState
            {
                Id = ActivityDependencyProjectionState.CurrentId,
                RebuildId = "incremental",
                Sequence = 1,
                AsOf = asOf,
                Items = ordered
            });
            return;
        }

        current.Sequence = checked(current.Sequence + 1);
        current.AsOf = asOf;
        current.Items = ordered;
    }

    // ---------------------------------------------------------------------------------------------
    // Revalidation
    // ---------------------------------------------------------------------------------------------

    private async Task EnsurePlanAndReceiptAreCurrentAsync(
        EfActivityUpgradeLane lane,
        ActivityUpgradePlan plan,
        ActivityUpgradeApplyReceipt receipt,
        CancellationToken cancellationToken)
    {
        var planRow = await SingleAsync(
            EfActivityUpgradeScope.ById(Scoped(lane.Activities.ActivityUpgradePlans.AsNoTracking()), plan.PlanId),
            $"ActivityUpgradePlan/{plan.PlanId}",
            cancellationToken);
        EnsurePlanIsCurrent(planRow.PlanJson, plan);
        var receiptRow = await SingleAsync(
            EfActivityUpgradeScope.ById(Scoped(lane.Activities.ActivityUpgradeApplyReceipts.AsNoTracking()), receipt.ReceiptId),
            $"ActivityUpgradeApplyReceipt/{receipt.ReceiptId}",
            cancellationToken);
        EnsureReceiptIsCurrent(receiptRow.ReceiptJson, receipt);
    }

    private static void EnsurePlanIsCurrent(string planJson, ActivityUpgradePlan plan)
    {
        var persisted = Deserialize<ActivityUpgradePlan>(planJson, $"Upgrade plan '{plan.PlanId}'");
        if (!SameMaterial(persisted, plan) || persisted.Status != ActivityUpgradePlanStatus.Ready)
            throw Stale("The upgrade plan changed before apply.");
    }

    private static void EnsureReceiptIsCurrent(string receiptJson, ActivityUpgradeApplyReceipt receipt)
    {
        var persisted = Deserialize<ActivityUpgradeApplyReceipt>(receiptJson, $"Upgrade receipt '{receipt.ReceiptId}'");
        if (!SameMaterial(persisted, receipt) || persisted.Status != ActivityUpgradeApplyReceiptStatus.Preparing)
            throw Stale("The upgrade apply receipt changed before apply.");
    }

    private async Task RecheckTargetsAsync(
        EfActivityUpgradeLane lane,
        IEnumerable<ActivityVersionReplacement> replacements,
        CancellationToken cancellationToken)
    {
        var publications = Publications(lane);
        foreach (var replacement in replacements)
        {
            var from = await publications.FindAsync(replacement.FromVersionId, cancellationToken);
            var to = await publications.FindAsync(replacement.ToVersionId, cancellationToken);
            if (from is null || to is null ||
                to.Lifecycle != ActivityDefinitionVersionLifecycle.Active ||
                !StringComparer.Ordinal.Equals(from.DefinitionId, to.DefinitionId) ||
                !StringComparer.Ordinal.Equals(from.TenantId, to.TenantId))
                throw Stale("An exact replacement identity, lifecycle, or tenant snapshot changed.");
        }
    }

    private async Task RecheckPlanBindingAsync(
        EfActivityUpgradeLane lane,
        ActivityUpgradePlan plan,
        CancellationToken cancellationToken)
    {
        if (plan.Binding is null)
            throw Stale("The upgrade plan has no exact selected closure.");

        await RecheckExpectedSnapshotsAsync(lane, plan.ExpectedSnapshots, cancellationToken);
        foreach (var expected in plan.Binding.SelectedClosure)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expected.TenantId is not null && !StringComparer.Ordinal.Equals(expected.TenantId, plan.TenantId))
                throw Stale("The selected closure is no longer visible in the bound tenant scope.");

            var current = await ResolveCurrentReferenceAsync(lane, expected, cancellationToken);
            if (current is null || !SameReferenceSnapshot(expected, current))
                throw Stale($"Selected closure snapshot '{ReferenceKey(expected)}' changed before apply.");
        }

        var discovery = await DiscoverAsync(lane, new(
            plan.Replacements,
            plan.Binding.Roots,
            plan.Binding.IncludeTransitiveDependents,
            plan.Binding.CreateDraftsForPublishedDependents,
            plan.TenantId,
            plan.Binding.AccessProfileFingerprint,
            plan.PredecessorPlanId), cancellationToken);
        var appliedDraftKeys = (plan.AppliedDrafts ?? [])
            .Select(x => $"{x.Kind}{x.DraftId}")
            .ToHashSet(StringComparer.Ordinal);
        if (discovery.Diagnostics.Any(x =>
                x.Severity == ActivityDiagnosticSeverity.Error &&
                !(x.Code == "activity.upgrade.root-not-affected" &&
                  appliedDraftKeys.Contains($"{x.Subject.Kind}{x.Subject.Id}"))))
            throw Stale("The bound root closure can no longer be discovered without errors.");
        var currentClosure = discovery.Owners
            .SelectMany(owner => owner.DependencyPaths.SelectMany(path => path.Append(owner.Owner)))
            .Concat(plan.Binding.SelectedClosure.Where(reference =>
                reference.DraftId is not null &&
                appliedDraftKeys.Contains($"{reference.Kind}{reference.DraftId}")))
            .GroupBy(ReferenceKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(ReferenceKey, StringComparer.Ordinal)
            .ToArray();
        var expectedClosure = plan.Binding.SelectedClosure
            .OrderBy(ReferenceKey, StringComparer.Ordinal)
            .ToArray();
        if (currentClosure.Length != expectedClosure.Length ||
            !currentClosure.Zip(expectedClosure).All(pair => SameReferenceSnapshot(pair.First, pair.Second)))
            throw Stale("The selected dependency closure changed before apply.");
    }

    private async Task RecheckExpectedSnapshotsAsync(
        EfActivityUpgradeLane lane,
        IReadOnlyList<ActivityUpgradeExpectedSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (snapshot.Kind)
            {
                case "ActivityDefinition":
                {
                    var authoring = await Authoring(lane).FindAsync(snapshot.DefinitionId, cancellationToken)
                                    ?? throw Stale($"Required snapshot 'ActivityDefinition/{snapshot.DefinitionId}' is unavailable.");
                    if (!StringComparer.Ordinal.Equals(authoring.HeadVersionId, snapshot.HeadVersionId))
                        throw Stale($"Activity definition head '{snapshot.DefinitionId}' changed before apply.");
                    break;
                }
                case "WorkflowDefinition":
                    if (!StringComparer.Ordinal.Equals(
                            await FindWorkflowHeadAsync(lane, snapshot.DefinitionId, cancellationToken),
                            snapshot.HeadVersionId))
                        throw Stale($"Workflow definition head '{snapshot.DefinitionId}' changed before apply.");
                    break;
                case "ActivityDraft":
                case "WorkflowDraft":
                {
                    var expected = new ActivityDefinitionReference(snapshot.Kind, snapshot.DefinitionId, DraftId: snapshot.Id, Revision: snapshot.Revision);
                    var current = await ResolveCurrentReferenceAsync(lane, expected, cancellationToken);
                    if (current is null || current.Revision != snapshot.Revision)
                        throw Stale($"Draft snapshot '{snapshot.Kind}/{snapshot.Id}' changed before apply.");
                    break;
                }
            }
        }
    }

    private async Task<ActivityDefinitionReference?> ResolveCurrentReferenceAsync(
        EfActivityUpgradeLane lane,
        ActivityDefinitionReference expected,
        CancellationToken cancellationToken)
    {
        switch (expected.Kind)
        {
            case "ActivityVersion":
            {
                var version = await Publications(lane).FindAsync(expected.VersionId!, cancellationToken);
                return version is null
                    ? null
                    : new(
                        expected.Kind,
                        version.DefinitionId,
                        version.DefinitionVersionId,
                        version.Version,
                        TemplateHash: version.TemplateHash,
                        TenantId: version.TenantId,
                        Lifecycle: version.Lifecycle);
            }
            case "ActivityDraft":
            {
                var draft = await Drafts(lane).FindAsync(expected.DraftId!, cancellationToken);
                return draft is null || draft.Status != ActivityDefinitionDraftStatus.Active
                    ? null
                    : new(expected.Kind, draft.DefinitionId, DraftId: draft.Id, Revision: draft.Revision, TenantId: draft.TenantId);
            }
            case "WorkflowVersion":
            {
                var version = await lane.WorkflowVersions.FindByIdAsync(expected.VersionId!, cancellationToken);
                return version is null
                    ? null
                    : new(expected.Kind, version.DefinitionId, version.Id, version.Version, TenantId: version.TenantId);
            }
            case "WorkflowDraft":
            {
                var draft = await lane.WorkflowDrafts.FindByIdAsync(expected.DraftId!, cancellationToken);
                return draft is null
                    ? null
                    : new(
                        expected.Kind,
                        draft.WorkflowDefinitionId,
                        DraftId: draft.Id,
                        Revision: EfWorkflowDraftRevision.Of(draft.LastModifiedAt),
                        TenantId: draft.TenantId);
            }
            default:
                return null;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Locking, rewriting and result shaping
    // ---------------------------------------------------------------------------------------------

    private async Task<DistributedLockScope> AcquireApplyLocksAsync(
        EfActivityUpgradeLane lane,
        ActivityUpgradePlan plan,
        CancellationToken cancellationToken)
    {
        var publications = Publications(lane);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snapshot in plan.ExpectedSnapshots)
        {
            if (snapshot.Kind == "ActivityDefinition")
                keys.Add(ActivityDesignPersistenceLockKeys.PublicationDefinitionKey(snapshot.DefinitionId));
            else if (snapshot.Kind == "WorkflowDefinition")
                keys.Add(WorkflowDesignPersistenceLockKeys.DefinitionKey(snapshot.DefinitionId));
        }

        foreach (var reference in plan.Binding!.SelectedClosure)
        {
            if (reference.Kind.StartsWith("Activity", StringComparison.Ordinal))
                keys.Add(ActivityDesignPersistenceLockKeys.PublicationDefinitionKey(reference.DefinitionId));
            else if (reference.Kind.StartsWith("Workflow", StringComparison.Ordinal))
                keys.Add(WorkflowDesignPersistenceLockKeys.DefinitionKey(reference.DefinitionId));
            if (reference.Kind == "WorkflowDraft" && reference.DraftId is not null)
                keys.Add(WorkflowDesignPersistenceLockKeys.DraftKey(reference.DraftId));
            else if (reference.Kind == "ActivityDraft" && reference.DraftId is not null)
                keys.Add(ActivityDesignPersistenceLockKeys.DraftKey(reference.DraftId));
        }

        foreach (var replacement in plan.Replacements)
        {
            var from = await publications.FindAsync(replacement.FromVersionId, cancellationToken);
            var to = await publications.FindAsync(replacement.ToVersionId, cancellationToken);
            if (from is not null)
                keys.Add(ActivityDesignPersistenceLockKeys.PublicationDefinitionKey(from.DefinitionId));
            if (to is not null)
                keys.Add(ActivityDesignPersistenceLockKeys.PublicationDefinitionKey(to.DefinitionId));
        }

        var handles = new List<IDistributedSynchronizationHandle>();
        try
        {
            foreach (var key in keys.Order(StringComparer.Ordinal))
                handles.Add(await lockProvider.AcquireLockAsync(key, null, cancellationToken));
            return new(handles);
        }
        catch
        {
            for (var index = handles.Count - 1; index >= 0; index--)
                await handles[index].DisposeAsync();
            throw;
        }
    }

    private sealed class DistributedLockScope(IReadOnlyList<IDistributedSynchronizationHandle> handles) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            for (var index = handles.Count - 1; index >= 0; index--)
                await handles[index].DisposeAsync();
        }
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

    private async ValueTask<ActivityProviderManifest> RewriteActivityAsync(
        ActivityProviderManifest manifest,
        IReadOnlyList<ActivityUpgradeOccurrenceReplacement> replacements,
        CancellationToken cancellationToken)
    {
        var matches = activityRewriters
            .Where(x => StringComparer.Ordinal.Equals(x.ProviderKey, manifest.ProviderKey) && x.SupportedManifestSchemas.Contains(manifest.SchemaVersion))
            .ToArray();
        if (matches.Length != 1)
            throw new ActivityUpgradeApplyException(422, "activity.upgrade.provider-rewriter-unavailable", "Exactly one provider-owned reference rewriter is required.");
        return await matches[0].RewriteReferencesAsync(manifest, replacements, cancellationToken);
    }

    private WorkflowDefinitionState RewriteWorkflow(
        WorkflowDefinitionState state,
        IReadOnlyList<ActivityUpgradeOccurrenceReplacement> replacements,
        CancellationToken cancellationToken)
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

            var slots = structureService.ProjectChildren(frame.Node)
                .Select(x => x with { Activities = x.Activities.Select(child => updated[child.NodeId]).ToArray() })
                .ToArray();
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

    private static ActivityUpgradeApplyResult BuildResult(
        ActivityUpgradePlan plan,
        IReadOnlyList<ActivityUpgradeStep> selectedSteps,
        IReadOnlyList<ActivityUpgradeAppliedDraft> applied,
        ActivityUpgradeApplyReceipt receipt,
        DateTimeOffset appliedAt,
        out ActivityUpgradePlan appliedPlan)
    {
        var appliedStageIds = selectedSteps.Select(x => x.StageId).Where(x => x is not null).ToHashSet(StringComparer.Ordinal);
        if (appliedStageIds.Count != 1 || !appliedStageIds.Contains(receipt.StageId))
            throw new ActivityUpgradeApplyException(422, "activity.upgrade.stage-invalid", "One exact stage must be applied per operation.");
        var stages = plan.Stages
            .Select(x => appliedStageIds.Contains(x.StageId) ? x with { Status = ActivityUpgradeStageStatus.Applied } : x)
            .ToArray();
        var awaitingStages = stages
            .Where(x => x.Status == ActivityUpgradeStageStatus.AwaitingPublication &&
                        x.DependsOnStageIds.Contains(receipt.StageId, StringComparer.Ordinal))
            .ToArray();
        var handoffs = applied
            .Where(x => x.SourceVersionId is not null)
            .Select(x => new ActivityUpgradePublicationHandoff(
                x.Kind,
                x.DraftId,
                x.DefinitionId,
                x.Revision,
                x.SourceVersionId!,
                awaitingStages.Select(y => y.StageId).Order(StringComparer.Ordinal).ToArray()))
            .Where(x => x.RequiredByStageIds.Count != 0)
            .ToArray();
        var status = stages.All(x => x.Status == ActivityUpgradeStageStatus.Applied)
            ? ActivityUpgradePlanStatus.Applied
            : stages.Any(x => x.Status == ActivityUpgradeStageStatus.Ready)
                ? ActivityUpgradePlanStatus.Ready
                : stages.Any(x => x.Status == ActivityUpgradeStageStatus.AwaitingPublication)
                    ? ActivityUpgradePlanStatus.AwaitingPublication
                    : ActivityUpgradePlanStatus.Blocked;
        var allAppliedDrafts = (plan.AppliedDrafts ?? []).Concat(applied).ToArray();
        appliedPlan = RebaseAfterCommittedStage(
            plan,
            selectedSteps,
            applied,
            status,
            status == ActivityUpgradePlanStatus.Applied ? appliedAt : null,
            allAppliedDrafts,
            stages);
        return new(plan.PlanId, status, appliedAt, applied, [], receipt.ReceiptId, receipt.StageId, handoffs);
    }

    private static ActivityUpgradePlan RebaseAfterCommittedStage(
        ActivityUpgradePlan plan,
        IReadOnlyList<ActivityUpgradeStep> selectedSteps,
        IReadOnlyList<ActivityUpgradeAppliedDraft> appliedDrafts,
        ActivityUpgradePlanStatus status,
        DateTimeOffset? appliedAt,
        IReadOnlyList<ActivityUpgradeAppliedDraft> allAppliedDrafts,
        IReadOnlyList<ActivityUpgradeStage> stages)
    {
        var changes = BuildProjectionChanges(selectedSteps, appliedDrafts, plan.TenantId)
            .ToDictionary(
                x => ReferenceKey(x.SourceOwner),
                x => x.ResultOwner,
                StringComparer.Ordinal);
        var selectedClosure = plan.Binding!.SelectedClosure
            .Select(reference => changes.GetValueOrDefault(ReferenceKey(reference)) ?? reference)
            .GroupBy(ReferenceKey, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToArray();
        var snapshots = plan.ExpectedSnapshots
            .Select(snapshot =>
            {
                var key = $"{snapshot.Kind}{snapshot.Id}";
                if (!changes.TryGetValue(key, out var result))
                    return snapshot;
                return new ActivityUpgradeExpectedSnapshot(
                    result.Kind,
                    result.DraftId ?? result.VersionId
                    ?? throw new InvalidOperationException("An applied upgrade result must carry an exact identity."),
                    result.Revision,
                    result.DefinitionId,
                    snapshot.HeadVersionId);
            })
            .GroupBy(x => (x.Kind, x.Id))
            .Select(x => x.First())
            .ToArray();
        return plan with
        {
            Status = status,
            AppliedAt = appliedAt,
            AppliedDrafts = allAppliedDrafts,
            ExpectedSnapshots = snapshots,
            Binding = plan.Binding with { SelectedClosure = selectedClosure },
            Stages = stages
        };
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

    /// <summary>One owner replacement staged with the source mutation that produced it.</summary>
    private sealed record ActivityDependencyProjectionOwnerChange(
        ActivityDefinitionReference SourceOwner,
        ActivityDefinitionReference ResultOwner,
        IReadOnlyList<ActivityUpgradeOccurrenceReplacement> Replacements);

    // ---------------------------------------------------------------------------------------------
    // Row access
    // ---------------------------------------------------------------------------------------------

    private IQueryable<T> Scoped<T>(IQueryable<T> query) where T : class =>
        EfActivityUpgradeScope.InScope(query, accessContextAccessor);

    /// <summary>
    /// The exact workflow-draft row. The physical scope key is a pure function of the tenant id, so
    /// constraining the tenant is equivalent to constraining the partition half of the primary key, and
    /// the hashed identity constrains the other half.
    /// </summary>
    private static IQueryable<WorkflowDefinitionDraft> WorkflowDraftRow(EfActivityUpgradeLane lane, string draftId, string? tenantId)
    {
        var rows = lane.Workflows.Drafts.Where(x => x.Id == draftId);
        return tenantId is null ? rows.Where(x => x.TenantId == null) : rows.Where(x => x.TenantId == tenantId);
    }

    private async Task<ActivityUpgradePlanRecord> TrackedPlanRowAsync(EfActivityUpgradeLane lane, string planId, CancellationToken cancellationToken) =>
        await SingleAsync(
            EfActivityUpgradeScope.ById(Scoped(lane.Activities.ActivityUpgradePlans), planId),
            $"ActivityUpgradePlan/{planId}",
            cancellationToken);

    private async Task<ActivityUpgradeApplyReceiptRecord> TrackedReceiptRowAsync(EfActivityUpgradeLane lane, string receiptId, CancellationToken cancellationToken) =>
        await SingleAsync(
            EfActivityUpgradeScope.ById(Scoped(lane.Activities.ActivityUpgradeApplyReceipts), receiptId),
            $"ActivityUpgradeApplyReceipt/{receiptId}",
            cancellationToken);

    /// <summary>Exactly one row, or a truthful stale-plan conflict. Two rows are drift, not a pick.</summary>
    private static async Task<T> SingleAsync<T>(IQueryable<T> query, string identity, CancellationToken cancellationToken)
        where T : class
    {
        var matches = await query.Take(2).ToListAsync(cancellationToken);
        return matches.Count == 1
            ? matches[0]
            : throw Stale($"Required snapshot '{identity}' is unavailable.");
    }

    private static IActivityDefinitionVersionPublicationStore Publications(EfActivityUpgradeLane lane) => lane.Design;

    private static IActivityDefinitionDraftStore Drafts(EfActivityUpgradeLane lane) => lane.Design;

    private static IActivityDefinitionAuthoringStore Authoring(EfActivityUpgradeLane lane) => lane.Design;

    private static async Task<string?> FindWorkflowHeadAsync(EfActivityUpgradeLane lane, string definitionId, CancellationToken cancellationToken)
    {
        var versions = await lane.WorkflowVersions.ListByDefinitionAsync(definitionId, cancellationToken);
        return versions
            .OrderByDescending(x => x.SemVerSortKey, StringComparer.Ordinal)
            .ThenByDescending(x => x.Id, StringComparer.Ordinal)
            .FirstOrDefault()?.Id;
    }

    // ---------------------------------------------------------------------------------------------
    // Shared shapes
    // ---------------------------------------------------------------------------------------------

    private static bool MatchesRoot(ActivityDefinitionReference reference, ActivityUpgradeRoot root) =>
        StringComparer.Ordinal.Equals(reference.Kind, root.Kind) &&
        StringComparer.Ordinal.Equals(reference.DraftId ?? reference.VersionId, root.Id);

    private static string ReferenceKey(ActivityDefinitionReference reference) =>
        $"{reference.Kind}{reference.DraftId ?? reference.VersionId}";

    private static string PathKey(IReadOnlyList<ActivityDefinitionReference> path) =>
        string.Join('', path.Select(ReferenceKey));

    private static bool SameReferenceSnapshot(ActivityDefinitionReference left, ActivityDefinitionReference right) =>
        StringComparer.Ordinal.Equals(left.Kind, right.Kind) &&
        StringComparer.Ordinal.Equals(left.DefinitionId, right.DefinitionId) &&
        StringComparer.Ordinal.Equals(left.VersionId, right.VersionId) &&
        StringComparer.Ordinal.Equals(left.Version, right.Version) &&
        StringComparer.Ordinal.Equals(left.DraftId, right.DraftId) &&
        left.Revision == right.Revision &&
        StringComparer.Ordinal.Equals(left.TemplateHash, right.TemplateHash) &&
        StringComparer.Ordinal.Equals(left.TenantId, right.TenantId) &&
        left.Lifecycle == right.Lifecycle;

    /// <summary>
    /// The EF dependency projection is one global row holding every tenant's facts, so an owner is matched
    /// on its tenant as well as its kind and identity.
    /// </summary>
    private static bool SameOwner(ActivityDefinitionReference left, ActivityDefinitionReference right) =>
        StringComparer.Ordinal.Equals(left.Kind, right.Kind) &&
        StringComparer.Ordinal.Equals(left.DraftId ?? left.VersionId, right.DraftId ?? right.VersionId) &&
        StringComparer.Ordinal.Equals(left.TenantId, right.TenantId);

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
        IReadOnlyList<ActivityContractMemberUsage>? memberUsage) => new(
        $"{owner.Kind}:{owner.DraftId ?? owner.VersionId}:{occurrence.OccurrenceId}:{dependency.VersionId}",
        owner,
        dependency,
        occurrence,
        true,
        1,
        [owner, dependency],
        memberUsage);

    /// <summary>The order the Activities Design EF projection writer stores its items in.</summary>
    internal static string ItemSortKey(ActivityDependencyItem item) =>
        string.Join('', item.Depth, item.Owner.Kind, item.Owner.DraftId ?? item.Owner.VersionId,
            item.Occurrence.OccurrenceId, item.Dependency.VersionId);

    internal static void ValidateProjectionItems(IEnumerable<ActivityDependencyItem> items)
    {
        var supportedOwners = new HashSet<string>(["ActivityVersion", "ActivityDraft", "WorkflowVersion", "WorkflowDraft"], StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.RelationshipId) ||
                string.IsNullOrWhiteSpace(item.Occurrence.OccurrenceId) ||
                !supportedOwners.Contains(item.Owner.Kind) ||
                !HasOwnerIdentity(item.Owner) ||
                !StringComparer.Ordinal.Equals(item.Dependency.Kind, "ActivityVersion") ||
                string.IsNullOrWhiteSpace(item.Dependency.VersionId))
                throw new ArgumentException("Projection items require a supported mixed owner, occurrence, and exact activity-version dependency.", nameof(items));
        }
    }

    private static bool HasOwnerIdentity(ActivityDefinitionReference owner) =>
        owner.Kind.EndsWith("Draft", StringComparison.Ordinal)
            ? !string.IsNullOrWhiteSpace(owner.DraftId)
            : !string.IsNullOrWhiteSpace(owner.VersionId);

    private static T Deserialize<T>(string json, string identity) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException($"{identity} is unreadable.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{identity} is unreadable.", exception);
        }
    }

    private static bool SameMaterial<T>(T left, T right) =>
        JsonNode.DeepEquals(
            JsonNode.Parse(JsonSerializer.Serialize(left, Json)),
            JsonNode.Parse(JsonSerializer.Serialize(right, Json)));

    private static ActivityDiagnostic Diagnostic(string code, string message, string kind, string id) =>
        new(code, ActivityDiagnosticSeverity.Error, message, new ActivityDiagnosticSubject(kind, id));

    private static ActivityUpgradeApplyException Stale(string message) =>
        new(409, "activity.upgrade.stale-plan", message);
}

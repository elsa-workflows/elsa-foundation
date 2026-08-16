using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Contracts;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>
/// Builds immutable, exact-version upgrade plans. Discovery is inverted so this Design service can
/// plan across activity and workflow owners without referencing workflow persistence.
/// </summary>
public sealed class ActivityUpgradePlanner(
    IActivityUpgradeDiscoverySource discoverySource,
    IActivityUpgradePlanStore planStore,
    IActivityUpgradeApplyReceiptStore receiptStore,
    IActivityUpgradePublishedDraftResolver publishedDraftResolver,
    IActivityUpgradeDiffBuilder diffBuilder,
    IActivityDependencyAuthorizationContextAsync authorization,
    IIdentityGenerator identityGenerator,
    ISystemClock clock) : IActivityUpgradePlanner, IActivityUpgradePlanRefresher
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public async ValueTask<ActivityUpgradePlan> PlanAsync(ActivityUpgradePlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var discovery = await discoverySource.DiscoverAsync(request, cancellationToken);
        var authorizedOwners = await AuthorizeOwnersAsync(discovery.Owners, request, cancellationToken);
        var groupedOwners = authorizedOwners
            .GroupBy(owner => ReferenceKey(owner.Owner), StringComparer.Ordinal)
            .Select(MergeOwnerPaths)
            .ToArray();
        var authorizedIds = groupedOwners
            .SelectMany(owner => owner.DependencyPaths.SelectMany(path => path.Append(owner.Owner)))
            .Select(ReferenceIdentity)
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal);
        var callerSuppliedIds = request.Roots.Select(x => x.Id)
            .Concat(request.Replacements.SelectMany(x => new[] { x.FromVersionId, x.ToVersionId }))
            .ToHashSet(StringComparer.Ordinal);
        var diagnostics = discovery.Diagnostics
            .Where(diagnostic => authorizedIds.Contains(diagnostic.Subject.Id) || callerSuppliedIds.Contains(diagnostic.Subject.Id))
            .ToList();
        foreach (var root in request.Roots.Where(root => groupedOwners.All(owner => !MatchesRoot(owner, root))))
        {
            if (diagnostics.All(x => x.Code != "activity.upgrade.root-not-affected" || !StringComparer.Ordinal.Equals(x.Subject.Id, root.Id)))
                diagnostics.Add(RootNotAffected(root));
        }
        var owners = groupedOwners
            .OrderBy(x => x.DependencyPaths.Min(path => path.Count))
            .ThenBy(x => KindOrder(x.Owner.Kind))
            .ThenBy(x => x.Owner.DefinitionId, StringComparer.Ordinal)
            .ThenBy(x => x.Owner.DraftId ?? x.Owner.VersionId, StringComparer.Ordinal)
            .ToArray();
        var steps = new List<ActivityUpgradeStep>(owners.Length);

        for (var index = 0; index < owners.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = owners[index];
            var stepDiagnostics = new List<ActivityDiagnostic>();
            if (owner.RequiresPublishedChildVersion)
                stepDiagnostics.Add(PublishedVersionRequired(owner));

            var action = owner.Owner.Kind switch
            {
                "ActivityDraft" or "WorkflowDraft" => ActivityUpgradeAction.UpdateDraft,
                "ActivityVersion" => ActivityUpgradeAction.CloneActivityVersion,
                "WorkflowVersion" => ActivityUpgradeAction.CloneWorkflowVersion,
                _ => throw new InvalidOperationException($"Unsupported upgrade owner kind '{owner.Owner.Kind}'.")
            };
            var resultingDiff = await diffBuilder.BuildAsync(owner, cancellationToken);
            if (owner.Owner.Kind.StartsWith("Activity", StringComparison.Ordinal) &&
                !owner.RequiresPublishedChildVersion && resultingDiff is null)
                stepDiagnostics.Add(DiffUnavailable(owner));
            var stepId = $"step-{identityGenerator.Generate()}";
            var stageId = $"stage-{identityGenerator.Generate()}";
            steps.Add(new ActivityUpgradeStep(
                stepId,
                (index + 1) * 10,
                new ActivityUpgradeTarget(
                    action is ActivityUpgradeAction.CloneActivityVersion ? "ActivityDraft" :
                    action is ActivityUpgradeAction.CloneWorkflowVersion ? "WorkflowDraft" : owner.Owner.Kind,
                    owner.Owner.DefinitionId,
                    owner.Owner.DraftId,
                    owner.Owner.Revision,
                    action is ActivityUpgradeAction.CloneActivityVersion or ActivityUpgradeAction.CloneWorkflowVersion ? owner.Owner.VersionId : null),
                action,
                FindPrerequisites(owner, owners, steps),
                owner.DirectReplacements.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal).ToArray(),
                owner.Owner.Revision,
                owner.DefinitionHeadVersionId,
                resultingDiff,
                ActivityDiagnosticOrderer.Order(stepDiagnostics),
                stageId));
        }

        var hasBlockingGlobalError = diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error);
        diagnostics.AddRange(steps.SelectMany(x => x.Diagnostics));
        var snapshots = owners
            .SelectMany(ToSnapshots)
            .GroupBy(x => (x.Kind, x.Id), SnapshotKeyComparer.Instance)
            .Select(x => x.First())
            .OrderBy(x => KindOrder(x.Kind))
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
        var stages = steps.Select(step => new ActivityUpgradeStage(
                step.StageId!,
                step.Order,
                StageStatus(step),
                [step.StepId],
                step.DependsOnStepIds
                    .Select(dependency => steps.Single(x => StringComparer.Ordinal.Equals(x.StepId, dependency)).StageId!)
                    .Order(StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();
        var selectedClosure = owners
            .SelectMany(x => x.DependencyPaths.SelectMany(path => path.Append(x.Owner)))
            .GroupBy(ReferenceKey, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderBy(x => KindOrder(x.Kind))
            .ThenBy(x => x.DraftId ?? x.VersionId, StringComparer.Ordinal)
            .ToArray();
        var now = clock.UtcNow;
        var orderedDiagnostics = ActivityDiagnosticOrderer.Order(diagnostics);
        var status = hasBlockingGlobalError
            ? ActivityUpgradePlanStatus.Blocked
            : stages.Any(x => x.Status == ActivityUpgradeStageStatus.Ready)
            ? ActivityUpgradePlanStatus.Ready
            : stages.Any(x => x.Status == ActivityUpgradeStageStatus.AwaitingPublication)
                ? ActivityUpgradePlanStatus.AwaitingPublication
                : ActivityUpgradePlanStatus.Blocked;
        var plan = new ActivityUpgradePlan(
            $"upgrade-plan-{identityGenerator.Generate()}",
            now,
            now.Add(Lifetime),
            status,
            request.Replacements.OrderBy(x => x.FromVersionId, StringComparer.Ordinal).ToArray(),
            snapshots,
            steps,
            orderedDiagnostics,
            TenantId: request.TenantId,
            Binding: new(
                request.Roots.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray(),
                request.IncludeTransitiveDependents,
                request.CreateDraftsForPublishedDependents,
                request.AccessProfileFingerprint!,
                selectedClosure),
            Stages: stages,
            PredecessorPlanId: request.PredecessorPlanId);
        await planStore.SaveAsync(plan, cancellationToken);
        return plan;
    }

    public async ValueTask<ActivityUpgradePlan> RefreshAsync(
        ActivityUpgradePlanRefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var predecessor = await planStore.FindAsync(request.PlanId, cancellationToken)
                          ?? throw new ActivityUpgradeApplyException(404, "activity.upgrade.plan-not-found", "The activity upgrade plan was not found.");
        if (predecessor.Binding is null ||
            !StringComparer.Ordinal.Equals(predecessor.TenantId, request.TenantId) ||
            !StringComparer.Ordinal.Equals(predecessor.Binding.AccessProfileFingerprint, request.AccessProfileFingerprint))
            throw new ActivityUpgradeApplyException(404, "activity.upgrade.plan-not-found", "The activity upgrade plan was not found.");
        if (request.Publications.Count == 0 ||
            request.Publications.GroupBy(x => x.ReceiptId, StringComparer.Ordinal).Any(x => x.Count() != 1))
            throw new ActivityUpgradeApplyException(422, "activity.upgrade.publication-selection-invalid", "At least one unique publication receipt is required.");

        var replacements = new List<ActivityVersionReplacement>();
        foreach (var publication in request.Publications.OrderBy(x => x.ReceiptId, StringComparer.Ordinal))
        {
            var receipt = await receiptStore.FindAsync(publication.ReceiptId, cancellationToken);
            if (receipt?.Result is null ||
                receipt.Status != ActivityUpgradeApplyReceiptStatus.Applied ||
                !StringComparer.Ordinal.Equals(receipt.PlanId, predecessor.PlanId) ||
                !StringComparer.Ordinal.Equals(receipt.TenantId, request.TenantId) ||
                !StringComparer.Ordinal.Equals(receipt.AccessProfileFingerprint, request.AccessProfileFingerprint))
                throw new ActivityUpgradeApplyException(404, "activity.upgrade.receipt-not-found", "An activity upgrade apply receipt was not found.");
            var handoffs = receipt.Result.AwaitingPublications;
            if (handoffs.Count == 0)
                throw new ActivityUpgradeApplyException(422, "activity.upgrade.publication-not-required", "An applied stage has no publication handoff.");
            if (publication.PublishedDrafts.Count != handoffs.Count ||
                publication.PublishedDrafts.GroupBy(x => x.DraftId, StringComparer.Ordinal).Any(x => x.Count() != 1))
                throw new ActivityUpgradeApplyException(422, "activity.upgrade.publication-selection-invalid", "Every exact handoff draft requires one published version.");

            foreach (var handoff in handoffs.OrderBy(x => x.DraftId, StringComparer.Ordinal))
            {
                var selection = publication.PublishedDrafts.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.DraftId, handoff.DraftId))
                                ?? throw new ActivityUpgradeApplyException(422, "activity.upgrade.publication-selection-invalid", "A handoff draft selection is missing.");
                var expectedVersionKind = handoff.DraftKind == "ActivityDraft" ? "ActivityVersion" : "WorkflowVersion";
                var published = await publishedDraftResolver.ResolveAsync(selection.PublishedVersionId, expectedVersionKind, cancellationToken)
                                ?? throw new ActivityUpgradeApplyException(422, "activity.upgrade.publication-selection-invalid", "A selected publication was not produced from the handoff draft.");
                if (!StringComparer.Ordinal.Equals(published.DraftId, handoff.DraftId) ||
                    !StringComparer.Ordinal.Equals(published.DefinitionId, handoff.DefinitionId) ||
                    !StringComparer.Ordinal.Equals(published.Kind, expectedVersionKind))
                    throw new ActivityUpgradeApplyException(422, "activity.upgrade.publication-selection-invalid", "A selected publication was not produced from the exact handoff draft.");
                replacements.Add(new(handoff.SourceVersionId, published.PublishedVersionId));
            }
        }

        var latest = predecessor;
        while (latest.SuccessorPlanId is not null)
            latest = await planStore.FindAsync(latest.SuccessorPlanId, cancellationToken)
                     ?? throw new InvalidOperationException($"Successor plan '{latest.SuccessorPlanId}' is unavailable.");
        var combined = (StringComparer.Ordinal.Equals(latest.PlanId, predecessor.PlanId)
                ? []
                : latest.Replacements)
            .Concat(replacements)
            .GroupBy(x => x.FromVersionId, StringComparer.Ordinal)
            .Select(x => x.Select(y => y.ToVersionId).Distinct(StringComparer.Ordinal).Count() == 1
                ? x.First()
                : throw new ActivityUpgradeApplyException(422, "activity.upgrade.publication-selection-invalid", "One source version cannot resolve to multiple publications."))
            .OrderBy(x => x.FromVersionId, StringComparer.Ordinal)
            .ToArray();
        if (SameReplacements(latest.Replacements, combined))
            return latest;

        var successor = await PlanAsync(new(
            combined,
            latest.Binding!.Roots,
            latest.Binding.IncludeTransitiveDependents,
            latest.Binding.CreateDraftsForPublishedDependents,
            request.TenantId,
            request.AccessProfileFingerprint,
            latest.PlanId), cancellationToken);
        await planStore.LinkSuccessorAsync(latest.PlanId, successor.PlanId, cancellationToken);
        return successor;
    }

    private static void ValidateRequest(ActivityUpgradePlanRequest request)
    {
        if (request.Replacements.Count == 0 || request.Roots.Count == 0)
            throw new ArgumentException("At least one exact replacement and explicit root are required.", nameof(request));
        if (request.Replacements.Any(x => string.IsNullOrWhiteSpace(x.FromVersionId) ||
                                          string.IsNullOrWhiteSpace(x.ToVersionId) ||
                                          StringComparer.Ordinal.Equals(x.FromVersionId, x.ToVersionId)))
            throw new ArgumentException("Replacement identities must be non-empty and different.", nameof(request));
        if (request.Replacements.GroupBy(x => x.FromVersionId, StringComparer.Ordinal).Any(x => x.Count() != 1))
            throw new ArgumentException("A source version may have only one exact replacement.", nameof(request));
        if (request.Roots.Any(x => x.Kind is not ("ActivityDraft" or "WorkflowDraft" or "ActivityVersion" or "WorkflowVersion") || string.IsNullOrWhiteSpace(x.Id)))
            throw new ArgumentException("Every root must use a supported kind and exact identity.", nameof(request));
        if (request.Roots.GroupBy(x => (x.Kind, x.Id)).Any(x => x.Count() != 1))
            throw new ArgumentException("Every upgrade root kind and identity pair must be unique.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.AccessProfileFingerprint))
            throw new ArgumentException("An access-profile fingerprint is required.", nameof(request));
    }

    private static IReadOnlyList<string> FindPrerequisites(
        ActivityUpgradeOwnerSnapshot owner,
        IReadOnlyList<ActivityUpgradeOwnerSnapshot> owners,
        IReadOnlyList<ActivityUpgradeStep> steps)
    {
        var descendants = owner.DependencyPaths
            .Where(path => path.Count >= 3)
            .SelectMany(path => path.Skip(1).SkipLast(1))
            .Select(x => x.DraftId ?? x.VersionId)
            .Where(x => x is not null)
            .ToHashSet(StringComparer.Ordinal);
        return steps.Where((_, index) => descendants.Contains(owners[index].Owner.DraftId ?? owners[index].Owner.VersionId))
            .Select(x => x.StepId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<ActivityUpgradeExpectedSnapshot> ToSnapshots(ActivityUpgradeOwnerSnapshot owner)
    {
        var id = owner.Owner.DraftId ?? owner.Owner.VersionId
                 ?? throw new InvalidOperationException("An upgrade owner must carry an exact identity.");
        yield return new(owner.Owner.Kind, id, owner.Owner.Revision, owner.Owner.DefinitionId, owner.DefinitionHeadVersionId);
        var kind = owner.Owner.Kind.StartsWith("Activity", StringComparison.Ordinal) ? "ActivityDefinition" : "WorkflowDefinition";
        yield return new(kind, owner.Owner.DefinitionId, null, owner.Owner.DefinitionId, owner.DefinitionHeadVersionId);
    }

    private static ActivityDiagnostic PublishedVersionRequired(ActivityUpgradeOwnerSnapshot owner) => new(
        "activity.upgrade.requires-published-version",
        ActivityDiagnosticSeverity.Error,
        "A dependent activity draft must be published before this parent can select its resulting exact version.",
        new ActivityDiagnosticSubject(owner.Owner.Kind, owner.Owner.DraftId ?? owner.Owner.VersionId ?? owner.Owner.DefinitionId, owner.Owner.DefinitionId, owner.Owner.VersionId, owner.Owner.Revision),
        Metadata: owner.RequiredChildStepId is null ? null : new Dictionary<string, string>(StringComparer.Ordinal) { ["requiredStepId"] = owner.RequiredChildStepId });

    private static ActivityDiagnostic DiffUnavailable(ActivityUpgradeOwnerSnapshot owner) => new(
        "activity.upgrade.diff-unavailable",
        ActivityDiagnosticSeverity.Error,
        "The exact post-rewrite activity candidate could not be compiled for compatibility analysis.",
        new ActivityDiagnosticSubject(owner.Owner.Kind, owner.Owner.DraftId ?? owner.Owner.VersionId ?? owner.Owner.DefinitionId, owner.Owner.DefinitionId, owner.Owner.VersionId, owner.Owner.Revision),
        Remediation: "Resolve provider rewrite or compilation diagnostics before applying the upgrade.");

    private static ActivityUpgradeStageStatus StageStatus(ActivityUpgradeStep step)
    {
        if (step.Diagnostics.Any(x => x.Code == "activity.upgrade.requires-published-version"))
            return ActivityUpgradeStageStatus.AwaitingPublication;
        return step.Diagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error)
            ? ActivityUpgradeStageStatus.Blocked
            : ActivityUpgradeStageStatus.Ready;
    }

    private static string ReferenceKey(ActivityDefinitionReference reference) =>
        $"{reference.Kind}\u001f{reference.DraftId ?? reference.VersionId}";

    private async Task<IReadOnlyList<ActivityUpgradeOwnerSnapshot>> AuthorizeOwnersAsync(
        IReadOnlyList<ActivityUpgradeOwnerSnapshot> owners,
        ActivityUpgradePlanRequest request,
        CancellationToken cancellationToken)
    {
        var authorized = new List<ActivityUpgradeOwnerSnapshot>(owners.Count);
        foreach (var owner in owners)
        {
            if (!await AuthorizedAsync(owner.Owner, request, cancellationToken))
                continue;

            var paths = new List<IReadOnlyList<ActivityDefinitionReference>>();
            foreach (var path in owner.DependencyPaths)
            {
                var pathAuthorized = true;
                foreach (var reference in path)
                {
                    if (!await AuthorizedAsync(reference, request, cancellationToken))
                    {
                        pathAuthorized = false;
                        break;
                    }
                }

                if (pathAuthorized)
                    paths.Add(path);
            }

            if (paths.Count != 0)
                authorized.Add(owner with { DependencyPaths = paths.ToArray() });
        }

        return authorized;
    }

    private async ValueTask<bool> AuthorizedAsync(
        ActivityDefinitionReference reference,
        ActivityUpgradePlanRequest request,
        CancellationToken cancellationToken) =>
        StringComparer.Ordinal.Equals(authorization.TenantId, request.TenantId) &&
        (reference.TenantId is null || StringComparer.Ordinal.Equals(reference.TenantId, request.TenantId)) &&
        await authorization.CanReadAsync(reference, cancellationToken);

    private static bool MatchesRoot(ActivityUpgradeOwnerSnapshot owner, ActivityUpgradeRoot root) =>
        owner.DependencyPaths.SelectMany(path => path.Append(owner.Owner)).Any(reference =>
            StringComparer.Ordinal.Equals(reference.Kind, root.Kind) &&
            StringComparer.Ordinal.Equals(ReferenceIdentity(reference), root.Id));

    private static ActivityUpgradeOwnerSnapshot MergeOwnerPaths(
        IGrouping<string, ActivityUpgradeOwnerSnapshot> group)
    {
        var first = group.First();
        var paths = group
            .SelectMany(x => x.DependencyPaths)
            .GroupBy(PathKey, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderBy(x => x.Count)
            .ThenBy(PathKey, StringComparer.Ordinal)
            .ToArray();
        return first with
        {
            DirectReplacements = group
                .SelectMany(x => x.DirectReplacements)
                .Distinct()
                .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal)
                .ThenBy(x => x.FromVersionId, StringComparer.Ordinal)
                .ToArray(),
            DependencyPaths = paths,
            RequiresPublishedChildVersion = group.Any(x => x.RequiresPublishedChildVersion),
            RequiredChildStepId = group.Select(x => x.RequiredChildStepId).FirstOrDefault(x => x is not null)
        };
    }

    private static string PathKey(IReadOnlyList<ActivityDefinitionReference> path) =>
        string.Join('\u001e', path.Select(ReferenceKey));

    private static bool SameReplacements(
        IReadOnlyList<ActivityVersionReplacement> left,
        IReadOnlyList<ActivityVersionReplacement> right) =>
        left.OrderBy(x => x.FromVersionId, StringComparer.Ordinal)
            .SequenceEqual(right.OrderBy(x => x.FromVersionId, StringComparer.Ordinal));

    private static string? ReferenceIdentity(ActivityDefinitionReference reference) =>
        reference.DraftId ?? reference.VersionId;

    private static ActivityDiagnostic RootNotAffected(ActivityUpgradeRoot root) => new(
        "activity.upgrade.root-not-affected",
        ActivityDiagnosticSeverity.Error,
        "The selected root does not contain an authorized dependency on a requested source version.",
        new(root.Kind, root.Id));

    private static int KindOrder(string kind) => kind switch
    {
        "ActivityVersion" => 0,
        "ActivityDraft" => 1,
        "WorkflowVersion" => 2,
        "WorkflowDraft" => 3,
        "ActivityDefinition" => 4,
        "WorkflowDefinition" => 5,
        _ => 6
    };

    private sealed class SnapshotKeyComparer : IEqualityComparer<(string Kind, string Id)>
    {
        public static SnapshotKeyComparer Instance { get; } = new();
        public bool Equals((string Kind, string Id) x, (string Kind, string Id) y) =>
            StringComparer.Ordinal.Equals(x.Kind, y.Kind) && StringComparer.Ordinal.Equals(x.Id, y.Id);
        public int GetHashCode((string Kind, string Id) obj) => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(obj.Kind), StringComparer.Ordinal.GetHashCode(obj.Id));
    }
}

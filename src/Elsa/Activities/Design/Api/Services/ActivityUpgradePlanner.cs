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
    IActivityUpgradeDiffBuilder diffBuilder,
    IIdentityGenerator identityGenerator,
    ISystemClock clock) : IActivityUpgradePlanner
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public async ValueTask<ActivityUpgradePlan> PlanAsync(ActivityUpgradePlanRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var discovery = await discoverySource.DiscoverAsync(request, cancellationToken);
        var diagnostics = discovery.Diagnostics.ToList();
        var owners = discovery.Owners
            .OrderBy(x => x.DependencyPath.Count)
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
            steps.Add(new ActivityUpgradeStep(
                $"step-{identityGenerator.Generate()}",
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
                ActivityDiagnosticOrderer.Order(stepDiagnostics)));
        }

        diagnostics.AddRange(steps.SelectMany(x => x.Diagnostics));
        var snapshots = owners
            .SelectMany(ToSnapshots)
            .GroupBy(x => (x.Kind, x.Id), SnapshotKeyComparer.Instance)
            .Select(x => x.First())
            .OrderBy(x => KindOrder(x.Kind))
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
        var now = clock.UtcNow;
        var orderedDiagnostics = ActivityDiagnosticOrderer.Order(diagnostics);
        var plan = new ActivityUpgradePlan(
            $"upgrade-plan-{identityGenerator.Generate()}",
            now,
            now.Add(Lifetime),
            orderedDiagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error)
                ? ActivityUpgradePlanStatus.Blocked
                : ActivityUpgradePlanStatus.Ready,
            request.Replacements.OrderBy(x => x.FromVersionId, StringComparer.Ordinal).ToArray(),
            snapshots,
            steps,
            orderedDiagnostics,
            TenantId: request.TenantId);
        await planStore.SaveAsync(plan, cancellationToken);
        return plan;
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
    }

    private static IReadOnlyList<string> FindPrerequisites(
        ActivityUpgradeOwnerSnapshot owner,
        IReadOnlyList<ActivityUpgradeOwnerSnapshot> owners,
        IReadOnlyList<ActivityUpgradeStep> steps)
    {
        if (owner.DependencyPath.Count < 3)
            return [];
        var descendants = owner.DependencyPath.Skip(1).SkipLast(1)
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

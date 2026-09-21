using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations.Handlers;

/// <summary>Trusted <c>Migrate/1</c> handler. It only stages a root projection after exact-artifact, compatibility,
/// retained-reference, and actor-side quiescence proof all succeed.</summary>
public sealed class MigrateWorkflowAlterationHandler(
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IBookmarkStateStore bookmarkStore,
    IActivityExecutionInspectionStore inspectionStore,
    IWorkflowMigrationQuiescenceProbe quiescenceProbe,
    IRuntimeExecutionOwnershipContextAccessor? ownershipContextAccessor,
    WorkflowMigrationCompatibilityValidator compatibilityValidator,
    WorkflowMigrationPlanner planner,
    TimeProvider? timeProvider = null) : IWorkflowAlterationPreflightHandler
{
    private readonly IWorkflowExecutableStore _executableStore = executableStore ?? throw new ArgumentNullException(nameof(executableStore));
    private readonly IWorkflowExecutableSourceReferenceStore _sourceReferenceStore = sourceReferenceStore ?? throw new ArgumentNullException(nameof(sourceReferenceStore));
    private readonly IBookmarkStateStore _bookmarkStore = bookmarkStore ?? throw new ArgumentNullException(nameof(bookmarkStore));
    private readonly IActivityExecutionInspectionStore _inspectionStore = inspectionStore ?? throw new ArgumentNullException(nameof(inspectionStore));
    private readonly IWorkflowMigrationQuiescenceProbe _quiescenceProbe = quiescenceProbe ?? throw new ArgumentNullException(nameof(quiescenceProbe));
    private readonly IRuntimeExecutionOwnershipContextAccessor? _ownershipContextAccessor = ownershipContextAccessor;
    private readonly WorkflowMigrationCompatibilityValidator _compatibilityValidator = compatibilityValidator ?? throw new ArgumentNullException(nameof(compatibilityValidator));
    private readonly WorkflowMigrationPlanner _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(WorkflowAlterationPreflightContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Ordinal != 0)
            return Reject("MigrationMustBeFirst");
        if (!TryReadTarget(context.Envelope.Payload, out var requestedTarget))
            return Reject("InvalidMigratePayload");

        var workflow = context.ProjectedState.GetRequired<WorkflowExecutionState>();
        if (WorkflowAlterationCapturedConcurrencyValidator.ValidateWorkflow(context.Job, workflow) is { } workflowConflict)
            return WorkflowAlterationPreflightResult.Rejected(workflowConflict.Code, workflowConflict.Message);
        var activities = context.ProjectedState.TryGet<WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection>(out var activityProjection)
            ? activityProjection!.Activities
            : [];
        // Parked Elsa executions can retain a Running aggregate while their wait/root activity is suspended.
        // That suspension boundary is necessary but not sufficient: the quiescence probe below must still prove
        // that no activity or delivery lane remains active.
        if (workflow.Status != WorkflowExecutionStatus.Suspended &&
            activities.All(activity => activity.Status != ActivityExecutionStatus.Suspended))
            return Reject("WorkflowNotSuspended");

        var source = await _executableStore.FindAsync(workflow.PinnedExecutable.ArtifactId, cancellationToken);
        if (source is null || !Equals(source.Identity, workflow.PinnedExecutable))
            return Reject("PinnedExecutableUnavailable");
        var target = await _executableStore.FindAsync(requestedTarget.ArtifactId, cancellationToken);
        if (target is null || !Equals(target.Identity, requestedTarget))
            return Reject("TargetArtifactUnavailable");

        var tenantPartition = workflow.Partition?.Value ?? workflow.TenantId ?? WorkflowExecutionPartition.DefaultValue;
        var targetReference = await FindLiveTargetReferenceAsync(requestedTarget, tenantPartition, cancellationToken);
        if (targetReference is null)
            return Reject("TargetArtifactNotRetained");

        var bookmarks = await ListBookmarksAsync(workflow.WorkflowExecutionId, cancellationToken);
        var inspections = await ListInspectionsAsync(workflow.WorkflowExecutionId, activities, cancellationToken);
        var compatibility = _compatibilityValidator.Validate(workflow, source, target, activities, bookmarks, inspections);
        if (!compatibility.IsCompatible)
            return Reject(compatibility.Findings.First().Code);

        var quiescence = await _quiescenceProbe.ProbeAsync(workflow.WorkflowExecutionId, _ownershipContextAccessor?.Current?.ToFence(), cancellationToken);
        if (!quiescence.IsQuiescent)
            return Reject(quiescence.Findings.First().Code);

        _planner.Stage(workflow, target.Identity, WorkflowExecutableSourceProvenance.From(targetReference), context.Staging);
        context.ProjectedState.Set(workflow with { PinnedExecutable = target.Identity, PinnedSource = WorkflowExecutableSourceProvenance.From(targetReference) });
        return WorkflowAlterationPreflightResult.Accepted();
    }

    private async ValueTask<WorkflowExecutableSourceReference?> FindLiveTargetReferenceAsync(
        WorkflowExecutableIdentity target,
        string tenantPartition,
        CancellationToken cancellationToken)
    {
        string? continuation = null;
        do
        {
            var page = await _sourceReferenceStore.ListByArtifactPageAsync(
                new WorkflowExecutableSourceReferenceArtifactPageQuery(target.ArtifactId, continuationToken: continuation), cancellationToken);
            var reference = page.Items.FirstOrDefault(candidate =>
                candidate.IsLive(_timeProvider.GetUtcNow()) &&
                StringComparer.Ordinal.Equals(candidate.DefinitionId, target.DefinitionId) &&
                StringComparer.Ordinal.Equals(candidate.DefinitionVersionId, target.DefinitionVersionId) &&
                StringComparer.Ordinal.Equals(candidate.ArtifactVersion, target.ArtifactVersion) &&
                (candidate.TenantId is null || StringComparer.Ordinal.Equals(candidate.TenantId, tenantPartition)));
            if (reference is not null)
                return reference;
            continuation = page.NextContinuationToken;
        } while (continuation is not null);

        return null;
    }

    private async ValueTask<IReadOnlyCollection<BookmarkState>> ListBookmarksAsync(string workflowExecutionId, CancellationToken cancellationToken)
    {
        var result = new List<BookmarkState>();
        string? continuation = null;
        do
        {
            var page = await _bookmarkStore.ListPageAsync(new BookmarkStatePageQuery(workflowExecutionId, continuationToken: continuation), cancellationToken);
            result.AddRange(page.Items);
            continuation = page.NextContinuationToken;
        } while (continuation is not null);
        return result;
    }

    private async ValueTask<IReadOnlyCollection<ActivityExecutionInspectionProjection>> ListInspectionsAsync(
        string workflowExecutionId,
        IReadOnlyCollection<ActivityExecutionState> activities,
        CancellationToken cancellationToken)
    {
        var inspections = new List<ActivityExecutionInspectionProjection>(activities.Count);
        foreach (var activity in activities)
        {
            var inspection = await _inspectionStore.FindAsync(workflowExecutionId, activity.Execution.ActivityExecutionId, cancellationToken);
            if (inspection is not null)
                inspections.Add(inspection);
        }
        return inspections;
    }

    private static bool TryReadTarget(JsonElement payload, out WorkflowExecutableIdentity target)
    {
        target = null!;
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("targetArtifact", out var identity) || identity.ValueKind != JsonValueKind.Object ||
            payload.EnumerateObject().Any(property => property.Name != "targetArtifact") ||
            identity.EnumerateObject().Any(property => property.Name is not ("artifactId" or "definitionId" or "definitionVersionId" or "artifactVersion" or "artifactHash")))
            return false;

        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in identity.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
                return false;
            parts[property.Name] = property.Value.GetString()!.Trim();
        }
        if (parts.Count != 5 || new[] { "artifactId", "definitionId", "definitionVersionId", "artifactVersion", "artifactHash" }.Any(key => !parts.ContainsKey(key)))
            return false;
        target = new WorkflowExecutableIdentity(parts["artifactId"], parts["definitionId"], parts["definitionVersionId"], parts["artifactVersion"], parts["artifactHash"]);
        return true;
    }

    private static WorkflowAlterationPreflightResult Reject(string code) =>
        WorkflowAlterationPreflightResult.Rejected(code, "The requested migration is not valid for the current execution.");
}

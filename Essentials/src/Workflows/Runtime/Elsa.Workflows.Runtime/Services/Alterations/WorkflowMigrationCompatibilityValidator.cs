using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Strict, value-free compatibility gate for a suspended execution. It compares every artifact-bound identity that
/// is retained in the initial runtime model; callers add liveness and retained-reference proof before staging.
/// </summary>
public sealed class WorkflowMigrationCompatibilityValidator
{
    public WorkflowMigrationCompatibilityReport Validate(
        WorkflowExecutionState workflow,
        WorkflowExecutable source,
        WorkflowExecutable target,
        IReadOnlyCollection<ActivityExecutionState> activities,
        IReadOnlyCollection<BookmarkState> bookmarks,
        IReadOnlyCollection<ActivityExecutionInspectionProjection> inspections)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(bookmarks);
        ArgumentNullException.ThrowIfNull(inspections);

        var findings = new List<WorkflowMigrationCompatibilityFinding>();
        if (!Equals(source.Identity, workflow.PinnedExecutable))
            findings.Add(new("PinnedSourceArtifactMismatch"));
        if (workflow.PinnedSource is null)
            findings.Add(new("PinnedSourceProvenanceUnavailable"));
        if (!StringComparer.Ordinal.Equals(source.Identity.DefinitionId, target.Identity.DefinitionId))
            findings.Add(new("DefinitionIdentityMismatch"));

        foreach (var sourceNode in source.Nodes.OrderBy(node => node.ExecutableNodeId, StringComparer.Ordinal))
        {
            if (!target.NodesById.TryGetValue(sourceNode.ExecutableNodeId, out var targetNode))
            {
                findings.Add(new("RetainedNodeMissing", sourceNode.ExecutableNodeId));
                continue;
            }

            if (!SameNodeContract(sourceNode, targetNode))
                findings.Add(new("NodeConsumerOrContractMismatch", sourceNode.ExecutableNodeId));
        }

        foreach (var activity in activities.OrderBy(activity => activity.Execution.ActivityExecutionId, StringComparer.Ordinal))
        {
            if (!target.NodesById.TryGetValue(activity.Execution.ExecutableNodeId, out var targetNode))
            {
                findings.Add(new("RetainedActivityNodeMissing", activity.Execution.ActivityExecutionId));
                continue;
            }

            if (!StringComparer.Ordinal.Equals(activity.Execution.ActivityType, targetNode.ActivityType) ||
                !StringComparer.Ordinal.Equals(activity.Execution.ActivityTypeVersion, targetNode.ActivityTypeVersion))
                findings.Add(new("RetainedActivityTypeMismatch", activity.Execution.ActivityExecutionId));
            if (string.IsNullOrWhiteSpace(activity.ExecutionScopeId) == false &&
                activities.Count(candidate => StringComparer.Ordinal.Equals(candidate.ExecutionScopeId, activity.ExecutionScopeId)) != 1)
                findings.Add(new("ExecutionScopeTopologyMismatch", activity.ExecutionScopeId));
        }

        foreach (var bookmark in bookmarks.OrderBy(bookmark => bookmark.BookmarkId, StringComparer.Ordinal))
        {
            if (!target.ResumeTargets.TryGetValue(bookmark.ResumeTargetId, out var targetResume) ||
                !StringComparer.Ordinal.Equals(targetResume.ExecutableNodeId, bookmark.ExecutableNodeId))
                findings.Add(new("BookmarkResumeTargetMismatch", bookmark.BookmarkId));
        }

        foreach (var activity in activities)
        {
            var inspection = inspections.SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.ActivityExecutionId, activity.Execution.ActivityExecutionId));
            if (inspection is null ||
                !StringComparer.Ordinal.Equals(inspection.ExecutableNodeId, activity.Execution.ExecutableNodeId) ||
                !StringComparer.Ordinal.Equals(inspection.ActivityType, activity.Execution.ActivityType) ||
                !StringComparer.Ordinal.Equals(inspection.ActivityTypeVersion, activity.Execution.ActivityTypeVersion))
                findings.Add(new("InspectionProjectionMismatch", activity.Execution.ActivityExecutionId));
        }

        ValidateVariables(workflow, source, target, findings);
        ValidateRequirements(source, target, findings);
        ValidateDependencies(source, target, findings);

        return findings.Count == 0
            ? WorkflowMigrationCompatibilityReport.Compatible(source.Identity, target.Identity)
            : WorkflowMigrationCompatibilityReport.Incompatible(source.Identity, target.Identity, findings);
    }

    private static bool SameNodeContract(ExecutableNode source, ExecutableNode target) =>
        StringComparer.Ordinal.Equals(source.ActivityType, target.ActivityType) &&
        StringComparer.Ordinal.Equals(source.ActivityTypeVersion, target.ActivityTypeVersion) &&
        StringComparer.Ordinal.Equals(source.Descriptor.ConsumerKey, target.Descriptor.ConsumerKey) &&
        IsRequirementCompatible(source.Descriptor.SchemaVersion, target.Descriptor.SchemaVersion, target.Metadata) &&
        SameActivityContract(source.ActivityContract, target.ActivityContract) &&
        source.IntrinsicKind == target.IntrinsicKind &&
        Equals(source.IntrinsicVariable, target.IntrinsicVariable);

    private static bool SameActivityContract(
        Elsa.Activities.Runtime.Core.Models.ActivityContract? source,
        Elsa.Activities.Runtime.Core.Models.ActivityContract? target) =>
        source is null
            ? target is null
            : target is not null &&
              StringComparer.Ordinal.Equals(source.SchemaFingerprint, target.SchemaFingerprint);

    private static void ValidateVariables(
        WorkflowExecutionState workflow,
        WorkflowExecutable source,
        WorkflowExecutable target,
        ICollection<WorkflowMigrationCompatibilityFinding> findings)
    {
        foreach (var declaration in source.WorkflowVariables)
        {
            var candidate = target.WorkflowVariables.SingleOrDefault(item => StringComparer.Ordinal.Equals(item.VariableKey, declaration.VariableKey));
            if (candidate is null || !Equals(candidate.Type, declaration.Type) || !Equals(candidate.Policy, declaration.Policy))
                findings.Add(new("WorkflowVariableDeclarationMismatch", declaration.VariableKey));
        }

        if (workflow.RootVariableFrame is { } frame)
        {
            foreach (var key in frame.Values.Keys.Order(StringComparer.Ordinal))
            {
                if (target.WorkflowVariables.All(item => !StringComparer.Ordinal.Equals(item.VariableKey, key)))
                    findings.Add(new("RetainedWorkflowVariableMissing", key));
            }
        }
    }

    private static void ValidateRequirements(WorkflowExecutable source, WorkflowExecutable target, ICollection<WorkflowMigrationCompatibilityFinding> findings)
    {
        foreach (var requirement in source.RuntimeRequirements)
        {
            var candidate = target.RuntimeRequirements.SingleOrDefault(item => StringComparer.Ordinal.Equals(item.ConsumerKey, requirement.ConsumerKey));
            if (candidate is null || !IsRequirementCompatible(requirement.SchemaVersion, candidate.SchemaVersion, target.CompatibilityMetadata))
                findings.Add(new("RuntimeRequirementMismatch", requirement.ConsumerKey));
        }
        foreach (var requirement in source.StorageDriverRequirements)
        {
            if (target.StorageDriverRequirements.All(item => !StringComparer.Ordinal.Equals(item.DriverKey, requirement.DriverKey)))
                findings.Add(new("StorageDriverRequirementMismatch", requirement.DriverKey));
        }
    }

    private static void ValidateDependencies(WorkflowExecutable source, WorkflowExecutable target, ICollection<WorkflowMigrationCompatibilityFinding> findings)
    {
        foreach (var dependency in source.Dependencies)
        {
            var candidate = target.Dependencies.SingleOrDefault(item => StringComparer.Ordinal.Equals(item.ArtifactId, dependency.ArtifactId));
            if (candidate is null || !StringComparer.Ordinal.Equals(candidate.ArtifactHash, dependency.ArtifactHash) ||
                dependency.DispatchNodeIds.Except(candidate.DispatchNodeIds, StringComparer.Ordinal).Any())
                findings.Add(new("RetainedDependencyMismatch", dependency.ArtifactId));
        }
    }

    // A target can explicitly declare an older schema compatible only through an immutable publish-time metadata
    // marker. This is the narrow compatible-downgrade escape hatch; arbitrary string ordering is never inferred.
    private static bool IsRequirementCompatible(string sourceSchema, string targetSchema, IReadOnlyDictionary<string, string> metadata) =>
        StringComparer.Ordinal.Equals(sourceSchema, targetSchema) ||
        metadata.TryGetValue($"runtime.migration.compatible-from-schema:{sourceSchema}", out var marker) &&
        StringComparer.Ordinal.Equals(marker, "true");
}

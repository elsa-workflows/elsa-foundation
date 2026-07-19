using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record ActivityExecutionBookmarkIdentity(
    string BookmarkId,
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ResumeTargetId);

public sealed record ActivityExecutionBookmarkSummary(
    ActivityExecutionBookmarkIdentity Identity,
    string StimulusType,
    string StimulusHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt)
{
    public static ActivityExecutionBookmarkSummary From(BookmarkState bookmark) =>
        new(
            Identity: new(
                bookmark.BookmarkId,
                bookmark.WorkflowExecutionId,
                bookmark.ActivityExecutionId,
                bookmark.ResumeTargetId),
            StimulusType: bookmark.StimulusType,
            StimulusHash: bookmark.StimulusHash,
            CreatedAt: bookmark.CreatedAt,
            ExpiresAt: bookmark.ExpiresAt);
}

public sealed record ActivityExecutionIncidentSummary(
    string IncidentId,
    IncidentSeverity Severity,
    IncidentStatus Status,
    IncidentResolutionAction ResolutionAction,
    string FailureType,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    bool IsBlocking,
    IReadOnlyDictionary<string, string> Metadata,
    ActivityExecutionIncidentCausation? Causation = null)
{
    public static ActivityExecutionIncidentSummary From(IncidentState incident) =>
        new(
            IncidentId: incident.IncidentId,
            Severity: incident.Severity,
            Status: incident.Status,
            ResolutionAction: incident.ResolutionAction,
            FailureType: incident.FailureType,
            Message: incident.Message,
            CreatedAt: incident.CreatedAt,
            ResolvedAt: incident.ResolvedAt,
            IsBlocking: incident.IsBlocking,
            Metadata: incident.Metadata,
            Causation: ActivityExecutionIncidentCausation.From(incident.Metadata));
}

public sealed record ActivityExecutionIncidentCausation(
    string IncidentId,
    string ActivityExecutionId,
    string? ExecutableNodeId,
    string Kind)
{
    public static ActivityExecutionIncidentCausation? From(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(RuntimeMetadataKeys.CausalIncidentId, out var incidentId) ||
            !metadata.TryGetValue(RuntimeMetadataKeys.CausalActivityExecutionId, out var activityExecutionId) ||
            !metadata.TryGetValue(RuntimeMetadataKeys.CausationKind, out var kind) ||
            string.IsNullOrWhiteSpace(incidentId) ||
            string.IsNullOrWhiteSpace(activityExecutionId) ||
            string.IsNullOrWhiteSpace(kind))
            return null;
        metadata.TryGetValue(RuntimeMetadataKeys.CausalExecutableNodeId, out var executableNodeId);
        return new(incidentId, activityExecutionId, executableNodeId, kind);
    }
}

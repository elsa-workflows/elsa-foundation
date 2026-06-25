using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record ActivityExecutionBookmarkSummary(
    string BookmarkId,
    string ResumeTargetId,
    string StimulusType,
    string StimulusHash,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyDictionary<string, string> Metadata,
    JsonElement? Payload = null)
{
    public static ActivityExecutionBookmarkSummary From(BookmarkState bookmark, JsonElement? payload = null) =>
        new(
            BookmarkId: bookmark.BookmarkId,
            ResumeTargetId: bookmark.ResumeTargetId,
            StimulusType: bookmark.StimulusType,
            StimulusHash: bookmark.StimulusHash,
            CreatedAt: bookmark.CreatedAt,
            ExpiresAt: bookmark.ExpiresAt,
            Metadata: bookmark.Metadata,
            Payload: payload?.Clone());
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
    IReadOnlyDictionary<string, string> Metadata)
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
            Metadata: incident.Metadata);
}

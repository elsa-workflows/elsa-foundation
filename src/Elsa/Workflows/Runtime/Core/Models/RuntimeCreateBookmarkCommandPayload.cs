using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeCreateBookmarkCommandPayload
{
    public const string ActivitySuspendedReason = "ActivitySuspended";

    [JsonConstructor]
    public RuntimeCreateBookmarkCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string bookmarkId,
        string activityExecutionId,
        string executableNodeId,
        string resumeTargetId,
        string stimulusType,
        string stimulusHash,
        JsonElement? payload,
        DateTimeOffset? expiresAt,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot>? valueSnapshots = null)
    {
        if (pinnedExecutable is null)
            throw new RuntimeCreateBookmarkCommandPayloadValidationException("Pinned executable cannot be null.", nameof(pinnedExecutable));

        if (string.IsNullOrWhiteSpace(bookmarkId))
            throw new RuntimeCreateBookmarkCommandPayloadValidationException("Bookmark ID cannot be blank.", nameof(bookmarkId));

        if (string.IsNullOrWhiteSpace(activityExecutionId))
            throw new RuntimeCreateBookmarkCommandPayloadValidationException("Activity execution ID cannot be blank.", nameof(activityExecutionId));

        if (string.IsNullOrWhiteSpace(executableNodeId))
            throw new RuntimeCreateBookmarkCommandPayloadValidationException("Executable node ID cannot be blank.", nameof(executableNodeId));

        if (string.IsNullOrWhiteSpace(resumeTargetId))
            throw new RuntimeCreateBookmarkCommandPayloadValidationException("Resume target ID cannot be blank.", nameof(resumeTargetId));

        if (string.IsNullOrWhiteSpace(stimulusType))
            throw new RuntimeCreateBookmarkCommandPayloadValidationException("Stimulus type cannot be blank.", nameof(stimulusType));

        if (string.IsNullOrWhiteSpace(stimulusHash))
            throw new RuntimeCreateBookmarkCommandPayloadValidationException("Stimulus hash cannot be blank.", nameof(stimulusHash));

        if (string.IsNullOrWhiteSpace(reason))
            throw new RuntimeCreateBookmarkCommandPayloadValidationException("Reason cannot be blank.", nameof(reason));

        PinnedExecutable = pinnedExecutable;
        BookmarkId = bookmarkId;
        ActivityExecutionId = activityExecutionId;
        ExecutableNodeId = executableNodeId;
        ResumeTargetId = resumeTargetId;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        Payload = payload?.Clone();
        ExpiresAt = expiresAt;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        ValueSnapshots = valueSnapshots?.ToArray() ?? [];
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string BookmarkId { get; }
    public string ActivityExecutionId { get; }
    public string ExecutableNodeId { get; }
    public string ResumeTargetId { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public JsonElement? Payload { get; }
    public DateTimeOffset? ExpiresAt { get; }
    public string Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> ValueSnapshots { get; }
}

internal sealed class RuntimeCreateBookmarkCommandPayloadValidationException(string message, string? paramName)
    : ArgumentException(message, paramName);

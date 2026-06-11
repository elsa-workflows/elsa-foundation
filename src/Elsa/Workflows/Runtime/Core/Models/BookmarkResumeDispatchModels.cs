using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class BookmarkResumeDispatchRequest
{
    public BookmarkResumeDispatchRequest(
        string workflowExecutionId,
        string stimulusType,
        string stimulusHash,
        JsonElement? input = null,
        string? idempotencyKey = null,
        string requestedBy = "runtime.bookmark",
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);

        if (idempotencyKey is not null && string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Bookmark resume idempotency key cannot be blank when provided.", nameof(idempotencyKey));

        WorkflowExecutionId = workflowExecutionId;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        Input = input?.Clone();
        IdempotencyKey = idempotencyKey;
        RequestedBy = requestedBy;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string WorkflowExecutionId { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public JsonElement? Input { get; }
    public string? IdempotencyKey { get; }
    public string RequestedBy { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class BookmarkResumeDispatchResult
{
    public BookmarkResumeDispatchResult(
        BookmarkResumeDispatchStatus status,
        string workflowExecutionId,
        BookmarkState? bookmark = null,
        BookmarkResumeResolution? resolution = null,
        WorkflowExecutionCommandDispatchResult? commandDispatch = null,
        WorkflowExecutionAgentDescriptor? agent = null,
        string? reason = null,
        BookmarkStimulusLookupResult? lookup = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        if (status == BookmarkResumeDispatchStatus.Dispatched)
        {
            ArgumentNullException.ThrowIfNull(bookmark);
            ArgumentNullException.ThrowIfNull(resolution);
            ArgumentNullException.ThrowIfNull(commandDispatch);
            ArgumentNullException.ThrowIfNull(agent);

            if (reason is not null)
                throw new ArgumentException("Successful bookmark resume dispatch results cannot carry a reason.", nameof(reason));
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

            if (commandDispatch is not null && status is not BookmarkResumeDispatchStatus.Duplicate and not BookmarkResumeDispatchStatus.Rejected and not BookmarkResumeDispatchStatus.Deferred)
                throw new ArgumentException("Command dispatch results are only valid for agent dispatch outcomes.", nameof(commandDispatch));
        }

        WorkflowExecutionId = workflowExecutionId;
        Status = status;
        Bookmark = bookmark;
        Resolution = resolution;
        CommandDispatch = commandDispatch;
        Agent = agent;
        Reason = reason;
        Lookup = lookup;
    }

    public string WorkflowExecutionId { get; }
    public BookmarkResumeDispatchStatus Status { get; }
    public BookmarkState? Bookmark { get; }
    public BookmarkResumeResolution? Resolution { get; }
    public WorkflowExecutionCommandDispatchResult? CommandDispatch { get; }
    public WorkflowExecutionAgentDescriptor? Agent { get; }
    public string? Reason { get; }
    public BookmarkStimulusLookupResult? Lookup { get; }
}

public sealed class RuntimeResumeBookmarkCommandPayload
{
    public const string StimulusMatchedReason = "BookmarkStimulusMatched";

    public RuntimeResumeBookmarkCommandPayload(
        WorkflowExecutableIdentity pinnedExecutable,
        string bookmarkId,
        string activityExecutionId,
        string executableNodeId,
        string resumeTargetId,
        string stimulusType,
        string stimulusHash,
        JsonElement? input,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resumeTargetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        PinnedExecutable = pinnedExecutable;
        BookmarkId = bookmarkId;
        ActivityExecutionId = activityExecutionId;
        ExecutableNodeId = executableNodeId;
        ResumeTargetId = resumeTargetId;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        Input = input?.Clone();
        Reason = reason;
    }

    public WorkflowExecutableIdentity PinnedExecutable { get; }
    public string BookmarkId { get; }
    public string ActivityExecutionId { get; }
    public string ExecutableNodeId { get; }
    public string ResumeTargetId { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public JsonElement? Input { get; }
    public string Reason { get; }
}

public enum BookmarkResumeDispatchStatus
{
    Dispatched,
    Duplicate,
    Rejected,
    Deferred,
    NotFound,
    Ambiguous,
    WorkflowExecutionMissing,
    ExecutableMissing,
    ResumeResolutionFailed
}

using System.Text.Json;
namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The durable lifecycle of one logical background activity invocation.</summary>
public enum BackgroundActivityWorkStatus
{
    Pending = 0,
    Claimed = 1,
    ResultReady = 2,
    Accepted = 3,
    Cancelled = 4
}

/// <summary>The business outcome recorded by a worker. Host shutdown and lease loss are not outcomes.</summary>
public enum BackgroundActivityWorkResultKind
{
    Complete = 0,
    Fault = 1,
    Cancel = 2
}

/// <summary>Immutable, checkpoint-admitted description of the work owed for a logical activity attempt.</summary>
public sealed record BackgroundActivityWorkRequest
{
    public BackgroundActivityWorkRequest(
        string workId,
        string workflowExecutionId,
        string activityExecutionId,
        ActivityAttempt attempt,
        WorkflowExecutableIdentity executable,
        string executableNodeId,
        string contractFingerprint,
        ActivityInputSnapshot inputSnapshot,
        RuntimeSchedulerWorkItem coordinatorWorkItem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentException.ThrowIfNullOrWhiteSpace(attempt.AttemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(attempt.InvocationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempt.Ordinal);
        if (attempt.EndedAt is not null)
            throw new ArgumentException("A background work request must pin an active logical attempt.", nameof(attempt));
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractFingerprint);
        ArgumentNullException.ThrowIfNull(inputSnapshot);
        ArgumentNullException.ThrowIfNull(coordinatorWorkItem);

        if (!StringComparer.Ordinal.Equals(inputSnapshot.InvocationId, attempt.InvocationId))
            throw new ArgumentException("The pinned input snapshot must belong to the requested invocation.", nameof(inputSnapshot));
        if (!StringComparer.Ordinal.Equals(inputSnapshot.ContractFingerprint, contractFingerprint))
            throw new ArgumentException("The pinned input snapshot must carry the requested contract fingerprint.", nameof(inputSnapshot));
        if (!StringComparer.Ordinal.Equals(coordinatorWorkItem.WorkflowExecutionId, workflowExecutionId))
            throw new ArgumentException("The coordinator work item must belong to the requested workflow execution.", nameof(coordinatorWorkItem));

        WorkId = workId;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        Attempt = attempt;
        Executable = executable;
        ExecutableNodeId = executableNodeId;
        ContractFingerprint = contractFingerprint;
        InputSnapshot = inputSnapshot;
        CoordinatorWorkItem = coordinatorWorkItem;
    }

    public string WorkId { get; }
    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }
    public string InvocationId => Attempt.InvocationId;
    public string AttemptId => Attempt.AttemptId;
    public int AttemptOrdinal => Attempt.Ordinal;
    public ActivityAttempt Attempt { get; }
    public WorkflowExecutableIdentity Executable { get; }
    public string ExecutableNodeId { get; }
    public string ContractFingerprint { get; }
    public ActivityInputSnapshot InputSnapshot { get; }
    public RuntimeSchedulerWorkItem CoordinatorWorkItem { get; }
    public string CoordinatorWorkItemId => CoordinatorWorkItem.WorkItemId;
}

/// <summary>Lease identity and fence presented by a worker for renewal or first receipt recording.</summary>
public sealed record BackgroundActivityWorkClaim(
    string WorkId,
    string ClaimOwnerId,
    long ClaimFence,
    DateTimeOffset ClaimedAt,
    DateTimeOffset VisibleUntil,
    long Revision);

/// <summary>A serialized terminal business result. Completion type is derived from the pinned activity contract.</summary>
public sealed record BackgroundActivityWorkTerminalResult
{
    public BackgroundActivityWorkTerminalResult(
        BackgroundActivityWorkResultKind kind,
        string? resultJson = null,
        string? safeFailureCode = null,
        string? safeFailureMessage = null)
    {
        if (kind == BackgroundActivityWorkResultKind.Complete)
        {
            if (safeFailureCode is not null || safeFailureMessage is not null)
                throw new ArgumentException("A completed result cannot carry failure details.");
            if (resultJson is not null)
            {
                try { using var _ = JsonDocument.Parse(resultJson!); }
                catch (JsonException exception) { throw new ArgumentException("A result payload must contain valid JSON.", nameof(resultJson), exception); }
            }
        }
        else
        {
            if (resultJson is not null)
                throw new ArgumentException("Fault and Cancel outcomes cannot carry a completion result.");
            ArgumentException.ThrowIfNullOrWhiteSpace(safeFailureCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(safeFailureMessage);
        }

        Kind = kind;
        ResultJson = resultJson;
        SafeFailureCode = safeFailureCode;
        SafeFailureMessage = safeFailureMessage;
    }

    public BackgroundActivityWorkResultKind Kind { get; }
    public string? ResultJson { get; }
    public string? SafeFailureCode { get; }
    public string? SafeFailureMessage { get; }
}

/// <summary>Provider-neutral snapshot of one durable background work row.</summary>
public sealed record BackgroundActivityWorkItem(
    BackgroundActivityWorkRequest Request,
    BackgroundActivityWorkStatus Status,
    long ClaimFence,
    long Revision,
    DateTimeOffset RecordedAt,
    DateTimeOffset UpdatedAt,
    string? ClaimOwnerId = null,
    DateTimeOffset? ClaimedAt = null,
    DateTimeOffset? VisibleUntil = null,
    BackgroundActivityWorkTerminalResult? Result = null,
    string? ReceiptDigest = null,
    string? ResultClaimOwnerId = null,
    long? ResultClaimFence = null,
    DateTimeOffset? AcceptedAt = null,
    DateTimeOffset? CancelledAt = null);

/// <summary>Receipt identity computed from the canonical terminal payload, never supplied by a worker.</summary>
public static class BackgroundActivityWorkReceipt
{
    public static string ComputeDigest(BackgroundActivityWorkTerminalResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var canonical = JsonSerializer.Serialize(new
        {
            kind = result.Kind.ToString(),
            resultJson = result.ResultJson,
            safeFailureCode = result.SafeFailureCode,
            safeFailureMessage = result.SafeFailureMessage
        });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

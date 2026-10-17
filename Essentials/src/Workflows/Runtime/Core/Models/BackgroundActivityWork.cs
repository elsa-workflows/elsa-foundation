using System.Text.Json;
using System.Security.Cryptography;
using Elsa.Workflows.Runtime.Core.Constants;
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
public sealed record BackgroundActivityWorkClaim
{
    public BackgroundActivityWorkClaim(
        string workflowExecutionId,
        string workId,
        string claimOwnerId,
        long claimFence,
        DateTimeOffset claimedAt,
        DateTimeOffset visibleUntil,
        long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workId);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimOwnerId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(claimFence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        if (visibleUntil <= claimedAt)
            throw new ArgumentOutOfRangeException(nameof(visibleUntil), "A worker claim must have a positive visibility interval.");

        WorkflowExecutionId = workflowExecutionId;
        WorkId = workId;
        ClaimOwnerId = claimOwnerId;
        ClaimFence = claimFence;
        ClaimedAt = claimedAt;
        VisibleUntil = visibleUntil;
        Revision = revision;
    }

    public string WorkflowExecutionId { get; }
    public string WorkId { get; }
    public string ClaimOwnerId { get; }
    public long ClaimFence { get; }
    public DateTimeOffset ClaimedAt { get; }
    public DateTimeOffset VisibleUntil { get; }
    public long Revision { get; }
}

/// <summary>A serialized terminal business result. Completion type is derived from the pinned activity contract.</summary>
public sealed record BackgroundActivityWorkTerminalResult
{
    public BackgroundActivityWorkTerminalResult(
        BackgroundActivityWorkResultKind kind,
        string outcome = ActivityOutcomes.Done,
        string? resultJson = null,
        NormalizedActivityFault? fault = null,
        string? cancellationReason = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The background activity result kind is not defined.");

        if (kind == BackgroundActivityWorkResultKind.Complete)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
            if (fault is not null || cancellationReason is not null)
                throw new ArgumentException("A completed result cannot carry fault or cancellation details.");
            if (resultJson is not null)
            {
                try { using var _ = JsonDocument.Parse(resultJson); }
                catch (JsonException exception) { throw new ArgumentException("A result payload must contain valid JSON.", nameof(resultJson), exception); }
            }
        }
        else if (kind == BackgroundActivityWorkResultKind.Fault)
        {
            if (resultJson is not null || cancellationReason is not null)
                throw new ArgumentException("A fault cannot carry a completion result or cancellation reason.");
            ArgumentNullException.ThrowIfNull(fault);
        }
        else if (resultJson is not null || fault is not null)
            throw new ArgumentException("A cancellation cannot carry a completion result or fault.");
        else
            ArgumentException.ThrowIfNullOrWhiteSpace(cancellationReason);

        Kind = kind;
        Outcome = outcome;
        ResultJson = resultJson;
        Fault = fault;
        CancellationReason = cancellationReason;
    }

    public BackgroundActivityWorkResultKind Kind { get; }
    public string Outcome { get; }
    public string? ResultJson { get; }
    public NormalizedActivityFault? Fault { get; }
    public string? CancellationReason { get; }
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
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string ComputeDigest(BackgroundActivityWorkRequest request, BackgroundActivityWorkTerminalResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        JsonElement? resultJson = null;
        if (result.ResultJson is not null)
        {
            using var resultDocument = JsonDocument.Parse(result.ResultJson);
            resultJson = resultDocument.RootElement.Clone();
        }

        var canonicalPayload = JsonSerializer.SerializeToElement(new
        {
            request.WorkId,
            request.WorkflowExecutionId,
            request.ActivityExecutionId,
            request.InvocationId,
            request.AttemptId,
            request.AttemptOrdinal,
            request.Executable,
            request.ExecutableNodeId,
            request.ContractFingerprint,
            request.InputSnapshot,
            result.Kind,
            result.Outcome,
            ResultJson = resultJson,
            result.Fault,
            result.CancellationReason
        }, SerializerOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(canonicalPayload, writer);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                return;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                return;
            default:
                element.WriteTo(writer);
                return;
        }
    }
}

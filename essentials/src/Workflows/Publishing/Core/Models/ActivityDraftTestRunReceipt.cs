using System.Security.Cryptography;
using System.Text;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

public sealed record ActivityDraftTestRunReceipt(
    string TestRunId,
    string OperationScope,
    string IdempotencyKeyHash,
    string DraftId,
    long DraftRevision,
    string DefinitionId,
    string? TenantId,
    string? ResourceTenantId,
    string RequestFingerprint,
    string WorkflowExecutionId,
    ActivityDraftTestRunReceiptStatus Status,
    string? CommandDispatchStatus,
    ActivityDraftTestRunFailure? Failure,
    string? ArtifactId,
    string? SourceReferenceId,
    DateTimeOffset RequestedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset SourceReferenceExpiresAt,
    DateTimeOffset ReceiptExpiresAt,
    ActivityDraftTestRunCancellationStatus CancellationStatus,
    long Revision);

public sealed record ActivityDraftTestRunFailure(
    ActivityDraftTestRunFailureKind Kind,
    string Code,
    string Message,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

public enum ActivityDraftTestRunFailureKind
{
    Validation,
    RuntimeDispatch
}

public enum ActivityDraftTestRunReceiptStatus
{
    Preparing,
    ValidationRejected,
    Dispatching,
    DispatchAccepted,
    DispatchDeferred,
    DispatchRejected,
    DispatchAmbiguous
}

public enum ActivityDraftTestRunCancellationStatus
{
    Available,
    Requested,
    Cancelling,
    Terminal,
    Unavailable
}

public sealed record ActivityDraftTestRunCreateResult(
    bool Created,
    ActivityDraftTestRunReceipt Receipt);

public static class ActivityDraftTestRunIdentity
{
    public const string GlobalOperationScope = "global";

    public static string CreateOperationScope(string? tenantId) =>
        tenantId is null ? GlobalOperationScope : $"tenant:{Hash(Required(tenantId))}";

    public static string CreateTestRunId(string operationScope, string draftId, string idempotencyKey) =>
        $"activity-test-run-{Hash($"{Required(operationScope)}\u001f{Required(draftId)}\u001f{Required(idempotencyKey)}")}";

    public static string CreateWorkflowExecutionId(string operationScope, string draftId, string idempotencyKey) =>
        $"workflow-execution-{Hash($"activity-test-run\u001f{Required(operationScope)}\u001f{Required(draftId)}\u001f{Required(idempotencyKey)}")}";

    public static string HashIdempotencyKey(string idempotencyKey) => Hash(Required(idempotencyKey));

    public static void EnsureOperationScope(ActivityDraftTestRunReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var expected = CreateOperationScope(receipt.TenantId);
        if (!StringComparer.Ordinal.Equals(receipt.OperationScope, expected))
            throw new ArgumentException("The receipt operation scope does not match its tenant semantics.", nameof(receipt));
    }

    private static string Required(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

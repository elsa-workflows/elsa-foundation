using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Versioned deterministic identities for one parent activity dispatch.</summary>
public sealed class WorkflowDispatchIdentity
{
    public const string Version = "v1";
    private readonly string _digest;

    public WorkflowDispatchIdentity(string parentWorkflowExecutionId, string parentActivityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);

        var digest = ComputeDigest(parentWorkflowExecutionId, parentActivityExecutionId);
        _digest = digest;
        DispatchId = $"dispatch:{Version}:{digest}";
        ChildWorkflowExecutionId = $"wfexec:dispatch:{Version}:{digest}";
        StartIntentId = $"intent:dispatch-start:{Version}:{digest}";
        StartIdempotencyKey = $"dispatch-start:{Version}:{digest}";
        WaitBookmarkId = $"bookmark:dispatch-wait:{Version}:{digest}";
        WaitStimulusHash = $"stimulus:dispatch-wait:{Version}:{digest}";
        ParentResumeIntentId = $"intent:dispatch-resume:{Version}:{digest}";
        ParentResumeIdempotencyKey = $"dispatch-resume:{Version}:{digest}";
        ChildCancelIntentId = $"intent:dispatch-cancel-child:{Version}:{digest}";
        ChildCancelIdempotencyKey = $"dispatch-cancel-child:{Version}:{digest}";
        ChildCancelCommandId = $"command:dispatch-cancel-child:{Version}:{digest}";
        ChildCancelEnvelopeId = $"envelope:dispatch-cancel-child:{Version}:{digest}";
    }

    public string DispatchId { get; }
    public string ChildWorkflowExecutionId { get; }
    public string StartIntentId { get; }
    public string StartIdempotencyKey { get; }
    public string WaitBookmarkId { get; }
    public string WaitStimulusHash { get; }
    public string ParentResumeIntentId { get; }
    public string ParentResumeIdempotencyKey { get; }
    public string ChildCancelIntentId { get; }
    public string ChildCancelIdempotencyKey { get; }
    public string ChildCancelCommandId { get; }
    public string ChildCancelEnvelopeId { get; }

    /// <summary>Stable, generation-scoped safe incident identity for exhausted child-start delivery.</summary>
    public string DeliveryIncidentId(int generation)
    {
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation), "Workflow dispatch delivery generation cannot be negative.");
        return $"incident:dispatch-delivery:{Version}:{_digest}:g{generation}";
    }

    /// <summary>Stable outbox identity for the wait-parent failure responsibility of one delivery generation.</summary>
    public string WaitFailureResumeOutboxItemId(int generation)
    {
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation), "Workflow dispatch delivery generation cannot be negative.");
        return $"outbox:dispatch-failed-resume:{Version}:{_digest}:g{generation}";
    }

    /// <summary>Checks whether an outbox identity belongs to this dispatch's deterministic child-start intent.</summary>
    public bool MatchesStartOutboxItemId(string? outboxItemId)
    {
        if (string.IsNullOrWhiteSpace(outboxItemId))
            return false;

        var suffix = $":{StartIntentId}";
        if (!outboxItemId.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var commitId = outboxItemId[..^suffix.Length];
        return commitId.Length > 0 && commitId.All(IsSafeIdentityCharacter);
    }

    public string ParentResumeOutboxItemId(string commitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitId);
        return $"{commitId}:{ParentResumeIntentId}";
    }

    public string ChildCancelOutboxItemId(string commitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commitId);
        return $"{commitId}:{ChildCancelIntentId}";
    }

    private static string ComputeDigest(string parentWorkflowExecutionId, string parentActivityExecutionId)
    {
        using var stream = new MemoryStream();
        WriteValue(stream, "elsa.workflow-dispatch");
        WriteValue(stream, Version);
        WriteValue(stream, parentWorkflowExecutionId);
        WriteValue(stream, parentActivityExecutionId);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteValue(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    private static bool IsSafeIdentityCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is ':' or '-' or '_' or '.';
}

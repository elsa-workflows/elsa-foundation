using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Buffers.Binary;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Messaging;

/// <summary>Unregistered transient handoff only; never allocates persistent control, source or outcome evidence.</summary>
internal sealed class NativeMessageSendStagingBuffer : INativeMessageSendStager, INativeMessageSendStagingAccessor
{
    private readonly ConcurrentDictionary<(PersistenceScope Scope, string Execution, string Invocation), Entry> pending = new();
    public NativeMessageSendWaitState Stage(NativeMessageSendRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var content = Validate(request.Submission);
        var entry = new Entry(request, content.Fingerprint, Wait(request));
        var key = (request.Scope, request.WorkflowExecutionId, request.InvocationId);
        var retained = pending.GetOrAdd(key, entry);
        if (retained.Fingerprint != entry.Fingerprint || !SameContext(retained.Request, request))
            throw new InvalidOperationException("One native send invocation cannot stage conflicting content or context.");
        return retained.Wait;
    }
    public void Reset(PersistenceScope scope, string workflowExecutionId, string invocationId)
    { ValidateKey(scope, workflowExecutionId, invocationId); pending.TryRemove((scope, workflowExecutionId, invocationId), out _); }
    public NativeMessageSendRequest? Take(PersistenceScope scope, string workflowExecutionId, string invocationId)
    {
        ValidateKey(scope, workflowExecutionId, invocationId);
        return pending.TryRemove((scope, workflowExecutionId, invocationId), out var entry) ? entry.Request : null;
    }
    internal static MessageSubmissionIntent Validate(MessageSubmission submission) => MessageSubmissionRules.CreateIntent(
        submission.Name, submission.CorrelationKey, submission.Payload, submission.ProducerMessageId, submission.LifetimeSeconds);
    internal static NativeMessageSendWaitState Wait(NativeMessageSendRequest request) => new(
        Id("send", request), Id("receipt-wait", request), Id("receipt-stimulus", request));
    private static string Id(string role, NativeMessageSendRequest request)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (var value in new[] { "elsa.native-message-send/v1", request.Scope.Value, request.WorkflowExecutionId, request.InvocationId, role })
        {
            BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length);
            var bytes = new byte[checked(value.Length * 2)];
            for (var index = 0; index < value.Length; index++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2, 2), value[index]);
            hash.AppendData(bytes);
        }
        return "native-message-" + role + ":v1:" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static bool SameContext(NativeMessageSendRequest left, NativeMessageSendRequest right) =>
        left.Scope == right.Scope && left.WorkflowExecutionId == right.WorkflowExecutionId && left.InvocationId == right.InvocationId &&
        left.AttemptId == right.AttemptId && left.ExecutableNodeId == right.ExecutableNodeId && left.OperationKey == right.OperationKey &&
        left.TargetOperationKey == right.TargetOperationKey && left.OriginatedAt == right.OriginatedAt;
    private static void ValidateKey(PersistenceScope scope, string execution, string invocation)
    { ArgumentNullException.ThrowIfNull(scope); ArgumentException.ThrowIfNullOrWhiteSpace(execution); ArgumentException.ThrowIfNullOrWhiteSpace(invocation); }
    private sealed record Entry(NativeMessageSendRequest Request, string Fingerprint, NativeMessageSendWaitState Wait);
}

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Versioned deterministic identities for one parent activity dispatch.</summary>
public sealed class WorkflowDispatchIdentity
{
    public const string Version = "v1";

    public WorkflowDispatchIdentity(string parentWorkflowExecutionId, string parentActivityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);

        var digest = ComputeDigest(parentWorkflowExecutionId, parentActivityExecutionId);
        DispatchId = $"dispatch:{Version}:{digest}";
        ChildWorkflowExecutionId = $"wfexec:dispatch:{Version}:{digest}";
        StartIntentId = $"intent:dispatch-start:{Version}:{digest}";
        StartIdempotencyKey = $"dispatch-start:{Version}:{digest}";
    }

    public string DispatchId { get; }
    public string ChildWorkflowExecutionId { get; }
    public string StartIntentId { get; }
    public string StartIdempotencyKey { get; }

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
}

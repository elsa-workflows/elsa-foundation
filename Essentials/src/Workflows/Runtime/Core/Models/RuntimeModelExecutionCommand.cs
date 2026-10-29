using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A model input with an exact material pin and captured execution authority, never a protocol hint.</summary>
public sealed class RuntimeModelExecutionCommand
{
    public RuntimeModelExecutionCommand(WorkflowExecutableIdentity executable, string materialIncarnationId,
        string inputId, string inputKind, JsonElement payload, WorkflowExecutionAuthoritySnapshot authority, string? tenantId)
    {
        RuntimeModelMaterial.ValidatePin(executable);
        foreach (var value in new[] { materialIncarnationId, inputId, inputKind }) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentNullException.ThrowIfNull(authority);
        if (tenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException("A model input requires an object payload.", nameof(payload));
        Executable = executable; MaterialIncarnationId = materialIncarnationId; InputId = inputId; InputKind = inputKind;
        Payload = payload.Clone(); Authority = authority; TenantId = tenantId;
    }

    public WorkflowExecutableIdentity Executable { get; }
    public string MaterialIncarnationId { get; }
    public string InputId { get; }
    public string InputKind { get; }
    public JsonElement Payload { get; }
    public WorkflowExecutionAuthoritySnapshot Authority { get; }
    public string? TenantId { get; }

    /// <summary>Stable identity shared by mailbox admission and committed native-result producers.</summary>
    public static string CreateSchedulerWorkItemId(string inputId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputId);
        var bytes = new byte[checked(inputId.Length * sizeof(char))];
        for (var index = 0; index < inputId.Length; index++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * sizeof(char), sizeof(char)), inputId[index]);
        return "model-input:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}

/// <summary>Verified physical material and current scoped state supplied to one model decision.</summary>
public sealed record RuntimeModelExecutionContext(PersistenceScope Scope, RuntimeExecutionMaterial Material,
    RuntimeModelExecutionState? State, RuntimeModelExecutionCommand Command, RuntimeSchedulerWorkItem WorkItem);

/// <summary>One exact protocol/version's scoped driver contribution; duplicates are refused at startup.</summary>
public sealed class RuntimeExecutionProtocolRegistration
{
    public RuntimeExecutionProtocolRegistration(RuntimeExecutionProtocol protocol, Type driverType)
    {
        ArgumentNullException.ThrowIfNull(protocol); ArgumentNullException.ThrowIfNull(driverType);
        if (driverType.IsAbstract || driverType.IsInterface || driverType.ContainsGenericParameters ||
            !typeof(IRuntimeExecutionProtocolDriver).IsAssignableFrom(driverType))
            throw new ArgumentException("A protocol requires a concrete execution driver.", nameof(driverType));
        Protocol = protocol; DriverType = driverType;
    }
    public RuntimeExecutionProtocol Protocol { get; }
    public Type DriverType { get; }
    public IReadOnlyCollection<string> InputKinds { get; init; } = [];
}

using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Execution/cleanup classification; semantic outcomes remain in the model's typed result payload.</summary>
public enum RuntimeModelLifecycle
{
    Running = 0,
    WaitingIncident = 1,
    TerminalPendingSettlement = 2,
    TerminalSettled = 3
}

/// <summary>One committed model execution header and detached model-owned state, without a native execution header.</summary>
public sealed class RuntimeModelExecutionState
{
    public const int CurrentFormatVersion = 1;

    public RuntimeModelExecutionState(int formatVersion, PersistenceScope scope, string executionId, string? tenantId,
        RuntimeExecutionProtocol protocol, WorkflowExecutableIdentity executable, string materialIncarnationId,
        long revision, RuntimeModelLifecycle lifecycle, WorkflowExecutionAuthoritySnapshot authority,
        DateTimeOffset createdAt, DateTimeOffset updatedAt, JsonElement payload, JsonElement? terminalResult)
    {
        if (formatVersion != CurrentFormatVersion) throw new NotSupportedException($"Model state format '{formatVersion}' is unsupported.");
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        if (tenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(protocol);
        RuntimeModelMaterial.ValidatePin(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(materialIncarnationId);
        ArgumentNullException.ThrowIfNull(authority);
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(lifecycle)) throw new ArgumentOutOfRangeException(nameof(lifecycle));
        if (createdAt == default || createdAt == DateTimeOffset.MaxValue || updatedAt < createdAt || updatedAt == DateTimeOffset.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(updatedAt), "Model timestamps must be finite and ordered.");
        if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException("Model state requires an object payload.", nameof(payload));
        var terminal = lifecycle is RuntimeModelLifecycle.TerminalPendingSettlement or RuntimeModelLifecycle.TerminalSettled;
        if (terminal != terminalResult.HasValue || terminalResult is { ValueKind: not JsonValueKind.Object })
            throw new ArgumentException("Only a terminal lifecycle carries its model-owned result object.", nameof(terminalResult));
        FormatVersion = formatVersion; Scope = scope; ExecutionId = executionId; TenantId = tenantId;
        Protocol = protocol; Executable = executable; MaterialIncarnationId = materialIncarnationId;
        Revision = revision; Lifecycle = lifecycle; Authority = authority; CreatedAt = createdAt; UpdatedAt = updatedAt;
        Payload = payload.Clone(); TerminalResult = terminalResult?.Clone();
    }

    public int FormatVersion { get; }
    public PersistenceScope Scope { get; }
    public string ExecutionId { get; }
    public string? TenantId { get; }
    public RuntimeExecutionProtocol Protocol { get; }
    public WorkflowExecutableIdentity Executable { get; }
    public string MaterialIncarnationId { get; }
    public long Revision { get; }
    public RuntimeModelLifecycle Lifecycle { get; }
    public WorkflowExecutionAuthoritySnapshot Authority { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public JsonElement Payload { get; }
    public JsonElement? TerminalResult { get; }
}

/// <summary>Retained consumption proof for one exact input, not an actor cache or queue acknowledgement.</summary>
public sealed class RuntimeModelInputReceipt
{
    public RuntimeModelInputReceipt(PersistenceScope scope, string executionId, string inputId, string inputKind,
        string contentFingerprint, long acceptedRevision, DateTimeOffset acceptedAt, string? invocationId, string? attemptId, JsonElement result)
    {
        ArgumentNullException.ThrowIfNull(scope);
        foreach (var value in new[] { executionId, inputId, inputKind, contentFingerprint }) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (acceptedRevision <= 0) throw new ArgumentOutOfRangeException(nameof(acceptedRevision));
        if (acceptedAt == default || acceptedAt == DateTimeOffset.MaxValue) throw new ArgumentOutOfRangeException(nameof(acceptedAt));
        if ((invocationId is null) != (attemptId is null)) throw new ArgumentException("Invocation and attempt must be captured together.");
        if (invocationId is not null) { ArgumentException.ThrowIfNullOrWhiteSpace(invocationId); ArgumentException.ThrowIfNullOrWhiteSpace(attemptId); }
        if (result.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("An accepted input requires a defined retained result.", nameof(result));
        Scope = scope; ExecutionId = executionId; InputId = inputId; InputKind = inputKind; ContentFingerprint = contentFingerprint;
        AcceptedRevision = acceptedRevision; AcceptedAt = acceptedAt; InvocationId = invocationId; AttemptId = attemptId; Result = result.Clone();
    }

    public PersistenceScope Scope { get; }
    public string ExecutionId { get; }
    public string InputId { get; }
    public string InputKind { get; }
    public string ContentFingerprint { get; }
    public long AcceptedRevision { get; }
    public DateTimeOffset AcceptedAt { get; }
    public string? InvocationId { get; }
    public string? AttemptId { get; }
    public JsonElement Result { get; }
}

/// <summary>A fenced decision's model state and exact accepted-input proof; only a checkpoint may write it.</summary>
public sealed class RuntimeModelStateChange
{
    public RuntimeModelStateChange(long? expectedRevision, RuntimeModelExecutionState state, RuntimeModelInputReceipt input)
    {
        ArgumentNullException.ThrowIfNull(state); ArgumentNullException.ThrowIfNull(input);
        if (expectedRevision is <= 0 || expectedRevision == long.MaxValue) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (state.Revision != (expectedRevision ?? 0) + 1 || input.AcceptedRevision != state.Revision ||
            input.Scope != state.Scope || input.ExecutionId != state.ExecutionId)
            throw new ArgumentException("A model decision requires the next revision and matching scoped input receipt.");
        ExpectedRevision = expectedRevision; State = state; Input = input;
    }

    public long? ExpectedRevision { get; }
    public RuntimeModelExecutionState State { get; }
    public RuntimeModelInputReceipt Input { get; }
}

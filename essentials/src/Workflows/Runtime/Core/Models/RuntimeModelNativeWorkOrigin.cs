using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The immutable address of native work owed by one committed model invocation and physical attempt.</summary>
public sealed class RuntimeModelNativeWorkOrigin
{
    public RuntimeModelNativeWorkOrigin(int formatVersion, PersistenceScope scope, string executionId,
        RuntimeExecutionProtocol protocol, WorkflowExecutableIdentity executable, string materialIncarnationId,
        string bindingId, string invocationId, string attemptId, WorkflowExecutableIdentity workerExecutable,
        string workerExecutionId, string sourceOutboxItemId, string sourceIntentId, string resultIntentKind, string resultInputId)
        : this(formatVersion, scope, executionId, protocol, executable, materialIncarnationId, bindingId, invocationId,
            attemptId, workerExecutable, workerExecutionId, sourceOutboxItemId, sourceIntentId, resultIntentKind, resultInputId, null, null) { }

    [JsonConstructor]
    public RuntimeModelNativeWorkOrigin(int formatVersion, PersistenceScope scope, string executionId,
        RuntimeExecutionProtocol protocol, WorkflowExecutableIdentity executable, string materialIncarnationId,
        string bindingId, string invocationId, string attemptId, WorkflowExecutableIdentity workerExecutable,
        string workerExecutionId, string sourceOutboxItemId, string sourceIntentId, string resultIntentKind, string resultInputId,
        IReadOnlyDictionary<string, JsonElement>? inputs, string? inputContractFingerprint)
    {
        if (formatVersion is not (1 or 2)) throw new NotSupportedException("The native model-work origin format is unsupported.");
        if (formatVersion == 1 ? inputs is not null || inputContractFingerprint is not null :
            inputs is null || string.IsNullOrWhiteSpace(inputContractFingerprint) || inputs.Any(item => string.IsNullOrWhiteSpace(item.Key) || item.Value.ValueKind == JsonValueKind.Undefined))
            throw new ArgumentException("Input-capable model work requires its complete normalized input bag and contract fingerprint.");
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(protocol);
        RuntimeModelMaterial.ValidatePin(executable);
        RuntimeModelMaterial.ValidatePin(workerExecutable);
        RuntimePostCommitIntent.ValidateKind(resultIntentKind, nameof(resultIntentKind));
        foreach (var value in new[] { executionId, materialIncarnationId, bindingId, invocationId, attemptId,
                     workerExecutionId, sourceOutboxItemId, sourceIntentId, resultInputId })
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (executionId == workerExecutionId || sourceOutboxItemId.Length <= sourceIntentId.Length + 1 ||
            !sourceOutboxItemId.EndsWith(":" + sourceIntentId, StringComparison.Ordinal))
            throw new ArgumentException("Native model work requires its distinct worker and complete committed source address.");
        FormatVersion = formatVersion; Scope = scope; ExecutionId = executionId; Protocol = protocol;
        Executable = executable; MaterialIncarnationId = materialIncarnationId; BindingId = bindingId;
        InvocationId = invocationId; AttemptId = attemptId; WorkerExecutable = workerExecutable;
        WorkerExecutionId = workerExecutionId; SourceOutboxItemId = sourceOutboxItemId; SourceIntentId = sourceIntentId;
        ResultIntentKind = resultIntentKind;
        ResultInputId = resultInputId;
        Inputs = inputs is null ? null : new ReadOnlyDictionary<string, JsonElement>(inputs.OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal));
        InputContractFingerprint = inputContractFingerprint;
    }

    public int FormatVersion { get; }
    public PersistenceScope Scope { get; }
    public string ExecutionId { get; }
    public RuntimeExecutionProtocol Protocol { get; }
    public WorkflowExecutableIdentity Executable { get; }
    public string MaterialIncarnationId { get; }
    public string BindingId { get; }
    public string InvocationId { get; }
    public string AttemptId { get; }
    public WorkflowExecutableIdentity WorkerExecutable { get; }
    public string WorkerExecutionId { get; }
    public string SourceOutboxItemId { get; }
    public string SourceIntentId { get; }
    public string ResultIntentKind { get; }
    public string ResultInputId { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyDictionary<string, JsonElement>? Inputs { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? InputContractFingerprint { get; }
    /// <summary>The physically admitted model root's retained ingress authority, copied only from its verified parent state.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RuntimeIngressCommandOrigin? IngressOrigin { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WorkflowExecutionPartition? Partition { get; init; }

    public bool MatchesModel(RuntimeModelExecutionState state) => state.Scope == Scope && state.ExecutionId == ExecutionId &&
        (IngressOrigin is null ? Partition is null : IngressOrigin.Scope == Scope && Partition is not null) &&
        state.Protocol == Protocol && state.MaterialIncarnationId == MaterialIncarnationId &&
        WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.Executable, Executable) &&
        Partition == state.Partition &&
        (IngressOrigin is null ? state.IngressOrigin is null : IngressOrigin.IsEquivalentTo(state.IngressOrigin));

    public bool IsEquivalentTo(RuntimeModelNativeWorkOrigin? other) => other is not null &&
        FormatVersion == other.FormatVersion && Scope == other.Scope && ExecutionId == other.ExecutionId && Protocol == other.Protocol &&
        MaterialIncarnationId == other.MaterialIncarnationId && BindingId == other.BindingId && InvocationId == other.InvocationId &&
        AttemptId == other.AttemptId && WorkerExecutionId == other.WorkerExecutionId && SourceOutboxItemId == other.SourceOutboxItemId &&
        SourceIntentId == other.SourceIntentId && ResultIntentKind == other.ResultIntentKind && ResultInputId == other.ResultInputId &&
        WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(Executable, other.Executable) &&
        WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(WorkerExecutable, other.WorkerExecutable) &&
        InputContractFingerprint == other.InputContractFingerprint && InputsMatch(Inputs, other.Inputs) && Partition == other.Partition &&
        (IngressOrigin is null ? other.IngressOrigin is null : IngressOrigin.IsEquivalentTo(other.IngressOrigin));

    private static bool InputsMatch(IReadOnlyDictionary<string, JsonElement>? left, IReadOnlyDictionary<string, JsonElement>? right) =>
        left is null ? right is null : right is not null && left.Count == right.Count &&
            left.All(item => right.TryGetValue(item.Key, out var value) && JsonElement.DeepEquals(item.Value, value));
}

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>The immutable address of native work owed by one committed model invocation and physical attempt.</summary>
public sealed class RuntimeModelNativeWorkOrigin
{
    public RuntimeModelNativeWorkOrigin(int formatVersion, PersistenceScope scope, string executionId,
        RuntimeExecutionProtocol protocol, WorkflowExecutableIdentity executable, string materialIncarnationId,
        string bindingId, string invocationId, string attemptId, WorkflowExecutableIdentity workerExecutable,
        string workerExecutionId, string sourceOutboxItemId, string sourceIntentId, string resultIntentKind, string resultInputId)
    {
        if (formatVersion != 1) throw new NotSupportedException("The native model-work origin format is unsupported.");
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

    public bool MatchesModel(RuntimeModelExecutionState state) => state.Scope == Scope && state.ExecutionId == ExecutionId &&
        state.Protocol == Protocol && state.MaterialIncarnationId == MaterialIncarnationId &&
        WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.Executable, Executable);

    public bool IsEquivalentTo(RuntimeModelNativeWorkOrigin? other) => other is not null &&
        FormatVersion == other.FormatVersion && Scope == other.Scope && ExecutionId == other.ExecutionId && Protocol == other.Protocol &&
        MaterialIncarnationId == other.MaterialIncarnationId && BindingId == other.BindingId && InvocationId == other.InvocationId &&
        AttemptId == other.AttemptId && WorkerExecutionId == other.WorkerExecutionId && SourceOutboxItemId == other.SourceOutboxItemId &&
        SourceIntentId == other.SourceIntentId && ResultIntentKind == other.ResultIntentKind && ResultInputId == other.ResultInputId &&
        WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(Executable, other.Executable) &&
        WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(WorkerExecutable, other.WorkerExecutable);
}

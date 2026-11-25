using Kernel.Foundation.Contracts;

namespace Kernel.Model.Contracts;

/// <param name="Definition">The workflow the model runs: on a publishing host, the published artifact.</param>
/// <param name="DefinitionId">
/// The definition that workflow is a version of, which instance reads select by (the <c>definition</c> label), when it is
/// not the workflow itself.
/// </param>
public sealed record WorkflowStart(
    string Model,
    string Definition,
    string? Input = null,
    bool Ephemeral = false,
    History History = History.Full,
    string? DefinitionId = null);

/// <summary>The workflow model and its versioned state format.</summary>
public interface IExecutionModel
{
    string Key { get; }
    Type StateType { get; }
    int StateVersion { get; }
    object CreateState(WorkflowStart start);
    object UpgradeState(string state, int fromVersion);
    ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken);
    ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken);
    RestorePlan RestoreWaits(InstanceInfo instance, object state);
    long TerminalGrowth(InstanceInfo instance, object state);
}

/// <summary>Model-owned terminal metadata and deletion protection.</summary>
public interface IModelMetadata
{
    bool HandlesMetadata(Message message);
    ValueTask MetadataAsync(ITurn turn, ActorStatus status, long? endedAt, CancellationToken cancellationToken);
    void TerminateMetadata(object state);
    bool ProtectsDeletion(object state, long? endedAt, long now);
}

/// <summary>Validates a definition change that preserves model state and all existing waits.</summary>
public interface ISimpleModelMigration
{
    /// <returns>A value-free refusal code, or null when the checkpoint is compatible.</returns>
    string? ValidateMigration(InstanceInfo source, object state, string target);
}

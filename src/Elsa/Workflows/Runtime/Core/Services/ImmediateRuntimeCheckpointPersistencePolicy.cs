using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class ImmediateRuntimeCheckpointPersistencePolicy : IRuntimeCheckpointPersistencePolicy
{
    private static readonly RuntimeCheckpointPersistenceDecision Decision = new(RuntimeCheckpointPersistenceMode.Immediate);

    public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Decision);
}

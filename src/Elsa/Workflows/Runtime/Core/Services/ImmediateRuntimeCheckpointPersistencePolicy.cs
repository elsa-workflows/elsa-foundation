using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class ImmediateRuntimeCheckpointPersistencePolicy : IRuntimeCheckpointPersistencePolicy
{
    public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
}

using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeCheckpointPersistencePolicy
{
    ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default);
}

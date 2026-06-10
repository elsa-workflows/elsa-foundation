using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeCheckpointWriter
{
    ValueTask WriteAsync(RuntimeCheckpoint checkpoint, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default);
}

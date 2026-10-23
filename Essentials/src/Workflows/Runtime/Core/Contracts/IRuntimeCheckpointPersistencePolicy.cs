using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Decides whether a checkpoint commits at once or folds into the drain's next commit. A replacement contract owned by
/// the persistence modes: one policy per runtime composition, the one <c>AddPersistenceModes</c> registers.
/// </summary>
public interface IRuntimeCheckpointPersistencePolicy
{
    ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default);
}

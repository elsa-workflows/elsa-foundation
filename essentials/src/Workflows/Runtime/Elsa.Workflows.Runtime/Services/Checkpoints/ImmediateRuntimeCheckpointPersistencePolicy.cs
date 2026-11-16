using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Checkpoints;

/// <summary>
/// Commits every checkpoint at once. The runtime registers it as a placeholder for a composition without the persistence
/// modes, which refuses to drain; <c>AddPersistenceModes</c> replaces it with the folding policy.
/// </summary>
public sealed class ImmediateRuntimeCheckpointPersistencePolicy : IRuntimeCheckpointPersistencePolicy
{
    private static readonly RuntimeCheckpointPersistenceDecision Decision = new(RuntimeCheckpointPersistenceMode.Immediate);

    public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Decision);
}

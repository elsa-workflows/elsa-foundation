using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Rebuilds the enriched commit and final generic persistence decision from canonical prepared input.</summary>
public interface IRuntimeCheckpointPreparationReplayer
{
    /// <summary>Verifies canonical authority and rebuilds the deterministic enriched checkpoint and final decision.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="reservation"/> is null.</exception>
    /// <exception cref="ArgumentException">The token or canonical envelope is malformed.</exception>
    /// <exception cref="InvalidOperationException">The canonical digest or durable authority does not match.</exception>
    ValueTask<RuntimeCheckpointPreparedCommit> RehydrateAsync(
        RuntimeCheckpointPreparedReservation reservation,
        CancellationToken cancellationToken = default);
}

using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Contributes deterministic state derived immediately before checkpoint persistence and fingerprinting.</summary>
public interface IRuntimeCheckpointCommitEnricher
{
    /// <summary>Orders deterministic enrichment phases; existing contributors default to the base phase.</summary>
    int Order => 0;

    ValueTask<RuntimeCheckpointCommit> EnrichAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default);
}

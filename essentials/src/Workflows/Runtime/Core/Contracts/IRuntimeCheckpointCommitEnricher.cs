using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Contributes deterministic state and validates the final envelope before checkpoint persistence and fingerprinting.</summary>
public interface IRuntimeCheckpointCommitEnricher
{
    /// <summary>Orders deterministic enrichment phases; existing contributors default to the base phase.</summary>
    int Order => 0;

    /// <summary>Validates the final enriched envelope before persistence decisions, fingerprinting or writes.
    /// Runs after every enrichment, so later contributors cannot introduce data past an earlier acceptance check.</summary>
    ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    ValueTask<RuntimeCheckpointCommit> EnrichAsync(
        RuntimeCheckpointCommit commit,
        CancellationToken cancellationToken = default);
}

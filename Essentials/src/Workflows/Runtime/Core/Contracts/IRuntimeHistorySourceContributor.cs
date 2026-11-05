using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Optional write-side source handoff. Only an admitted immutable policy may activate the owner factory.</summary>
public interface IRuntimeHistorySourceContributor
{
    ValueTask<RuntimeCheckpointCommit> AddFactsAsync(RuntimeCheckpointCommit commit,
        Func<RuntimeHistoryCapturePolicy, IEnumerable<RuntimeHistorySourceFact>> facts, CancellationToken cancellationToken = default);
}

using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Optional write-side source handoff. Only an admitted immutable policy may activate the owner factory.</summary>
public interface IRuntimeHistorySourceExtension
{
    /// <exception cref="RuntimeSchedulerWorkUnavailableException">The admitted native policy cannot be established; its original cause is retained and work stays owed.</exception>
    ValueTask<RuntimeCheckpointCommit> AddFactsAsync(RuntimeCheckpointCommit commit,
        Func<RuntimeHistoryCapturePolicy, IEnumerable<RuntimeHistorySourceFact>> facts, CancellationToken cancellationToken = default);
}

using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Explicit handler opt-in to a verified durable source; context is never inferred from an intent alone.</summary>
public interface IRuntimePostCommitDurableSourceIntentHandler : IRuntimePostCommitIntentHandler
{
    /// <summary>Explicitly qualifies this selected owner against the actual observation writer/reader pair; legacy owners refuse.</summary>
    bool CanRecordHistory(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader) => false;

    /// <summary>Handles one source verified by the processor; revalidate domain authority before recipient effects.</summary>
    /// <exception cref="RuntimePostCommitSourceBoundaryException">Required source/provider support is unavailable or contradictory; responsibility stays owed.</exception>
    /// <exception cref="OperationCanceledException">Requested cancellation propagates unchanged.</exception>
    ValueTask HandleAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default);

    ValueTask IRuntimePostCommitIntentHandler.HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken) =>
        ValueTask.FromException(new RuntimePostCommitSourceBoundaryException(null, intent.Kind,
            new InvalidOperationException("This handler requires a verified durable source context."), false));
}

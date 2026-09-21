using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimePostCommitIntentDispatcher
{
    ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default);
}

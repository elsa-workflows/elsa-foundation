using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimePostCommitOutboxProcessor
{
    ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
        RuntimePostCommitOutboxProcessRequest request,
        CancellationToken cancellationToken = default);
}

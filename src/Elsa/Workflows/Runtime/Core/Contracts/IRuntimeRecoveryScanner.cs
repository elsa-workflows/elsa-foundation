using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeRecoveryScanner
{
    ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(RuntimeRecoveryScanRequest request, CancellationToken cancellationToken = default);
}

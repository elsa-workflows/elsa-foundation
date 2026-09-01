using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Provider-neutral capability required to page recovery liveness in stable due order.
/// </summary>
/// <remarks>
/// Implementations must apply the recovery selector and order key before taking the page. A generic ID-ordered
/// <see cref="IExecutionLivenessStateStore.ListAllPageAsync"/> cannot be adapted safely after the fact because a
/// later ID may be eligible before the first page's last row.
/// </remarks>
public interface IRuntimeRecoveryLivenessPageSource
{
    ValueTask<RuntimeStorePage<ExecutionLivenessState>> ListRecoveryPageAsync(
        RuntimeRecoveryScanRequest request,
        RuntimeStorePageRequest query,
        CancellationToken cancellationToken = default);
}

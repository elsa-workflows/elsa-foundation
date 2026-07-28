using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryRuntimeRecoveryScanner : IRuntimeRecoveryScanner
{
    private readonly IExecutionLivenessStateStore _operationalStateStore;

    public InMemoryRuntimeRecoveryScanner(IExecutionLivenessStateStore operationalStateStore)
    {
        ArgumentNullException.ThrowIfNull(operationalStateStore);
        _operationalStateStore = operationalStateStore;
    }

    public async ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(
        RuntimeRecoveryScanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var states = await RuntimeOperationalStorePagingExtensions.ListAllAsync(_operationalStateStore, cancellationToken);
        return RuntimeRecoveryCandidateSelector.Select(states, request);
    }
}

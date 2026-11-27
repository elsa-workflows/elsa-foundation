using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.KernelHost;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelHistoryCapabilitySource(KernelRuns runs) : IApiCapabilitySource
{
    public async ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        await runs.SupportsSequencedJournalAsync(cancellationToken)
            ? [new(RuntimeApiCapabilities.CapabilityId, 1,
                [new("workflow-history-page", "runtime/workflows/instances/{workflowExecutionId}/history", templated: true)],
                $"{WorkflowsRuntimeKernelApiFeature.FeatureName}.History")]
            : [];
}

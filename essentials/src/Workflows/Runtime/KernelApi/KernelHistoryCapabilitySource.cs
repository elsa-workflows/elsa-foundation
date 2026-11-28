using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Models;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.KernelHost;

namespace Elsa.Workflows.Runtime.KernelApi;

internal sealed class KernelHistoryCapabilitySource(KernelRuns runs) : IApiCapabilitySource
{
    public async ValueTask<IReadOnlyCollection<ApiCapabilityDeclaration>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        if (!await runs.SupportsSequencedJournalAsync(cancellationToken)) return [];
        var link = new ApiCapabilityLink("workflow-history-page", "runtime/workflows/instances/{workflowExecutionId}/history", templated: true);
        var live = new ApiCapabilityLink("workflow-history-live", KernelHistoryStream.Route, templated: true);
        return [new(RuntimeApiCapabilities.CapabilityId, 1, await runs.SupportsPlaybackAsync(cancellationToken)
            ? [link, live, new("workflow-history-occurrences", "runtime/workflows/instances/{workflowExecutionId}/history/occurrences", templated: true)]
            : [link, live], $"{WorkflowsRuntimeKernelRetainedApiFeature.RouteOwner}.History")];
    }
}

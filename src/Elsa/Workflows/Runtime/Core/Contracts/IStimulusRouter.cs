using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Routes an external stimulus to workflows (W7). This is the service Elsa 4 was missing that closes the
/// largest parity gap with Elsa 3: a single stimulus, with no explicit execution id, can start new workflow
/// instances (E3-1) and/or fan in to every waiting instance across executions (E3-5).
/// </summary>
public interface IStimulusRouter
{
    ValueTask<StimulusRoutingResult> RouteAsync(StimulusDispatchRequest request, CancellationToken cancellationToken = default);
}

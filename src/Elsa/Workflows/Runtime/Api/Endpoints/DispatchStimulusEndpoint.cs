using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Constants;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Api.Endpoints;

/// <summary>
/// <c>POST runtime/workflows/stimuli</c> — routes an external stimulus to workflows (W7). Secured by
/// construction through <see cref="ElsaRequestHandlerEndpoint{TRequest,TResponse}"/> and
/// <c>ConfigurePermissions()</c> (W4); it never calls <c>AllowAnonymous</c>. This is the integration surface
/// that turns the trigger index and cross-execution bookmark index into a reachable feature: a caller posts a
/// stimulus and the engine starts matching workflows and/or fans in to waiting instances.
/// </summary>
internal sealed class DispatchStimulusEndpoint : ElsaRequestHandlerEndpoint<DispatchStimulus, DispatchStimulusResponse>
{
    public DispatchStimulusEndpoint(IRequestSender requestSender, ILogger<DispatchStimulusEndpoint> logger)
        : base(requestSender, logger)
    {
    }

    public override void Configure()
    {
        Post(RouteConstants.GetRoute("stimuli"));
        ConfigurePermissions(PermissionNames.WorkflowRuntimeExecute);
    }
}

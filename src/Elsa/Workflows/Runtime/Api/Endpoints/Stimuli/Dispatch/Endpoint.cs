using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using NativeEndpoints;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Stimuli.Dispatch;

[Post("runtime/workflows/stimuli")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeExecute)]
[RuntimeProblems("handling runtime request", NotFoundArms = true)]
public sealed class Endpoint(IStimulusDispatchService stimuli) : ApiEndpoint<DispatchStimulus, DispatchStimulusResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DispatchStimulus";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload;
    }

    public override Task<DispatchStimulusResponse> HandleAsync(DispatchStimulus request, CancellationToken cancellationToken) =>
        stimuli.DispatchAsync(request, cancellationToken);
}

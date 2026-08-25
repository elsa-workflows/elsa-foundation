using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Stimuli.Dispatch;

[Post("runtime/workflows/stimuli")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeExecute)]
[RuntimeProblems("handling runtime request", NotFoundArms = true)]
public sealed class Endpoint(IRequestSender sender) : ApiEndpoint<DispatchStimulus, DispatchStimulusResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DispatchStimulus";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload;
    }

    public override Task<DispatchStimulusResponse> HandleAsync(DispatchStimulus request, CancellationToken cancellationToken) =>
        sender.Send(request, cancellationToken);
}

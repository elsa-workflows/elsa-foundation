using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;
using System.Globalization;

namespace Elsa.Workflows.Runtime.Api.Endpoints.Stimuli.Dispatch;

[Post("runtime/workflows/stimuli")]
[RequirePermission(WorkflowRuntimePermissions.WorkflowRuntimeExecute)]
[RuntimeProblems("handling runtime request", NotFoundArms = true)]
public sealed class Endpoint(IStimulusDispatchService stimuli) : ApiEndpointWithResult<DispatchStimulus, DispatchStimulusResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "DispatchStimulus";
        options.Accepts = ["application/json"];
        options.BodyMode = EndpointBodyMode.RequiredWithContentTypeAndPayload;
    }

    public override async Task<EndpointResult<DispatchStimulusResponse>> HandleAsync(DispatchStimulus request, CancellationToken cancellationToken)
    {
        var response = await stimuli.DispatchAsync(request, cancellationToken);

        // Admission refused every start and nothing was resumed (#2548): nothing was written, so this is backpressure,
        // answered as the Execute endpoint answers a shed start.
        if (response.ShedStartCount > 0 && response.StartedCount == 0 && response.ResumedCount == 0)
        {
            HttpContext.Response.Headers.RetryAfter = Math.Max(1, response.RetryAfterSeconds ?? 1).ToString(CultureInfo.InvariantCulture);
            return EndpointResult.Status(StatusCodes.Status429TooManyRequests, response);
        }

        return EndpointResult.Status(StatusCodes.Status200OK, response);
    }
}

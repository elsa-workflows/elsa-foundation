using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring.ResolveActivityInputOptions;

/// <summary>The status code is chosen by the resolution result, so this endpoint writes its own response.</summary>
[Post("activities/{activityVersionId}/inputs/{inputName}/options")]
[RequirePermission(WorkflowDesignPermissions.Read)]
public sealed class Endpoint(ActivityInputOptionsAuthoringService authoringService)
    : WritingApiEndpoint<ActivityInputOptionsRequest, ActivityInputOptionsResponse>
{
    public override void Configure(ApiEndpointOptions options)
    {
        options.Operation = "AuthoringResolveActivityInputOptions";
        options.Accepts = ["application/json"];
    }

    public override async Task HandleAsync(ActivityInputOptionsRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var result = await authoringService.ResolveAsync(
            request.ActivityVersionId, request.InputName, request.NodeId, request.WorkflowState, cancellationToken);
        await WriteAsync(
            result.StatusCode == StatusCodes.Status200OK
                ? new ActivityInputOptionsResponse(result.Options!)
                : new ActivityInputOptionsResponse(Error: result.Error, Code: result.Code),
            result.StatusCode);
    }
}

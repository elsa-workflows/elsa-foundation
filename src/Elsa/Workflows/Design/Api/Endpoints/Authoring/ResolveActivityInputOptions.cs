using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Services;

namespace Elsa.Workflows.Design.Api.Endpoints.Authoring;

internal sealed class ResolveActivityInputOptions(ActivityInputOptionsAuthoringService authoringService)
    : ElsaEndpoint<ActivityInputOptionsRequest, ActivityInputOptionsResponse>
{
    public override void Configure()
    {
        Post(RouteConstants.ActivityInputOptions);
        ConfigurePermissions(PermissionNames.WorkflowDesignRead);
    }

    public override async Task HandleAsync(ActivityInputOptionsRequest request, CancellationToken cancellationToken)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        var result = await authoringService.ResolveAsync(
            request.ActivityVersionId,
            request.InputName,
            request.NodeId,
            request.WorkflowState,
            cancellationToken);

        if (result.StatusCode == 200)
        {
            await Send.OkAsync(new(result.Options!), cancellationToken);
            return;
        }

        await Send.ResponseAsync(
            new ActivityInputOptionsResponse(Error: result.Error!, Code: result.Code),
            result.StatusCode,
            cancellationToken);
    }
}

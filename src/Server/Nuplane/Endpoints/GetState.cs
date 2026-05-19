using Elsa.Serialization.Core;
using Elsa.Server.Constants;
using Elsa.Server.Nuplane.Endpoints.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Nuplane.Admin;
using System.Net;

namespace Elsa.Server.Nuplane.Endpoints;

internal sealed class GetState(INuplaneAdminOperations operations, ILogger<GetState> logger)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("nuplane/admin/state");
        //ConfigurePermissions("actions:nuplane:state");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var state = await operations.GetStateAsync(cancellationToken);
            await SendResponseAsync(new OperationalStateResponse(state), HttpStatusCode.OK, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get state. Error: {err}", ex.Message);
            await Send.ErrorsAsync((int)HttpStatusCode.InternalServerError, cancellationToken);
        }
    }

    private async Task SendResponseAsync(OperationalStateResponse response, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        var serializerOptions = JsonSerializerOptionsCache.Instance;
        HttpContext.Response.StatusCode = (int)statusCode;

        if (statusCode != HttpStatusCode.ServiceUnavailable)
            await HttpContext.Response.WriteAsJsonAsync(response, serializerOptions, cancellationToken);
    }
}

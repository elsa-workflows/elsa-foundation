using Elsa.Serialization.Core;
using Elsa.Server.Constants;
using Elsa.Server.Nuplane.Endpoints.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Nuplane.Admin;
using System.Net;

namespace Elsa.Server.Nuplane.Endpoints;

internal sealed class ListPackages(INuplaneAdminOperations operations, ILogger<ListPackages> logger)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("nuplane/admin/packages");
        //ConfigurePermissions("actions:nuplane:packages");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await operations.GetPackagesAsync(cancellationToken);
            await SendResponseAsync(new PackageCatalogResponse(snapshot), HttpStatusCode.OK, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list packages. Error: {err}", ex.Message);
            await Send.ErrorsAsync((int)HttpStatusCode.InternalServerError, cancellationToken);
        }
    }

    private async Task SendResponseAsync(PackageCatalogResponse response, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        var serializerOptions = JsonSerializerOptionsCache.Instance;
        HttpContext.Response.StatusCode = (int)statusCode;

        if (statusCode != HttpStatusCode.ServiceUnavailable)
            await HttpContext.Response.WriteAsJsonAsync(response, serializerOptions, cancellationToken);
    }
}

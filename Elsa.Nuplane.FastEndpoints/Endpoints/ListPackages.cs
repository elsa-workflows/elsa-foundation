using Elsa.FastEndpoints.Abstractions;
using Elsa.Nuplane.FastEndpoints.Models;
using Elsa.Serialization.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Nuplane.Admin;
using System.Net;

namespace Elsa.Nuplane.FastEndpoints.Endpoints
{
    internal sealed class ListPackages(INuplaneAdminOperations operations, IPayloadSerializer serializer, ILogger<ListPackages> logger) 
        : ElsaEndpointWithoutRequest
    {
        public override void Configure()
        {
            Post("nuplane/admin/packages");
            ConfigurePermissions("actions:nuplane:packages");
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
            var serializerOptions = serializer.GetOptions();
            HttpContext.Response.StatusCode = (int)statusCode;

            if (statusCode != HttpStatusCode.ServiceUnavailable)
                await HttpContext.Response.WriteAsJsonAsync(response, serializerOptions, cancellationToken);
        }
    }
}

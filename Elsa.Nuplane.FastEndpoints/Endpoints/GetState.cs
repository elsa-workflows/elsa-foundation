using Elsa.FastEndpoints.Abstractions;
using Elsa.Nuplane.FastEndpoints.Models;
using Elsa.Serialization.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Nuplane.Admin;
using System.Net;

namespace Elsa.Nuplane.FastEndpoints.Endpoints
{
    internal sealed class GetState(INuplaneAdminOperations operations, IPayloadSerializer serializer, ILogger<ListPackages> logger) 
        : ElsaEndpointWithoutRequest
    {
        public override void Configure()
        {
            Post("nuplane/admin/state");
            ConfigurePermissions("actions:nuplane:state");
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
            var serializerOptions = serializer.GetOptions();
            HttpContext.Response.StatusCode = (int)statusCode;

            if (statusCode != HttpStatusCode.ServiceUnavailable)
                await HttpContext.Response.WriteAsJsonAsync(response, serializerOptions, cancellationToken);
        }
    }
}

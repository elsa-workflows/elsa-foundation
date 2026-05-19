using Elsa.Serialization.Core;
using Elsa.Server.Constants;
using Elsa.Server.Nuplane.Endpoints.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Nuplane.Admin;
using Nuplane.Reconciliation;
using System.Net;

namespace Elsa.Server.Nuplane.Endpoints;

internal sealed class Reconcile(INuplaneAdminOperations nuplaneAdminOperations, ILogger<Reconcile> logger)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("nuplane/admin/reconcile");
        //ConfigurePermissions("actions:nuplane:reconcile");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await nuplaneAdminOperations.TriggerReconcileAsync(cancellationToken);
            var responseModel = new ReconcileResponse(outcome);
            var statusCode = outcome.OutcomeCode switch
            {
                ManualReconcileOutcomeCode.Completed => HttpStatusCode.OK,
                ManualReconcileOutcomeCode.Accepted => HttpStatusCode.Accepted,
                ManualReconcileOutcomeCode.Rejected => HttpStatusCode.Conflict,
                ManualReconcileOutcomeCode.Unavailable => HttpStatusCode.ServiceUnavailable,
                _ => HttpStatusCode.InternalServerError
            };
            await SendResponseAsync(responseModel, statusCode, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reconcile. Error: {err}", ex.Message);
            await Send.ErrorsAsync((int)HttpStatusCode.InternalServerError, cancellationToken);
        }
    }

    private async Task SendResponseAsync(ReconcileResponse response, HttpStatusCode statusCode, CancellationToken cancellationToken)
    {
        var serializerOptions = JsonSerializerOptionsCache.Instance;
        HttpContext.Response.StatusCode = (int)statusCode;

        if (statusCode != HttpStatusCode.ServiceUnavailable)
            await HttpContext.Response.WriteAsJsonAsync(response, serializerOptions, cancellationToken);
    }
}

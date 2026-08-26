using Elsa.Api.Endpoints;
using Elsa.Attention.Core;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Attention.Api;

/// <summary>Maps the attention aggregation surface using ordinary ASP.NET Core endpoints.</summary>
public static class AttentionApi
{
    private const string OwnerId = "Elsa.Attention.Api";

    public static void MapAttentionApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The published document tags this surface with the host application name, resolved at
        // composition time exactly as the hand-written mapper did.
        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName;
        var api = endpoints.MapModuleEndpoints(
            OwnerId,
            AttentionJsonContext.Default,
            jsonContentType: "application/json",
            tag: string.IsNullOrWhiteSpace(applicationName) ? null : applicationName);

        // The published error contract is a plain-text 400 carrying the query exception's message,
        // and the repeatable contributor filter is undocumented on purpose, so the operation stays
        // on the group's raw seam with its own reads and writes.
        api.MapUnboundOperation(
                "GET", AttentionRoutes.Items, "GetAttentionItems",
                typeof(AttentionAggregationResult), StatusCodes.Status200OK, null, HandleGetAttentionItemsAsync)
            .RequirePermission(AttentionPermissions.Read);
    }

    private static async Task HandleGetAttentionItemsAsync(HttpContext context)
    {
        var query = context.Request.Query["contributorId"]
            .Select(value => value ?? string.Empty)
            .ToArray();
        var principal = context.User;
        var tenantId = principal.FindFirst(IdentityClaimTypes.TenantId)?.Value;
        var service = context.RequestServices.GetRequiredService<IAttentionAggregationService>();

        try
        {
            var result = await service.AggregateAsync(
                new AttentionQuery(
                    new AttentionQueryContext(principal, tenantId),
                    query.Length == 0 ? null : query),
                context.RequestAborted);
            await Results.Json(result, AttentionJsonContext.Default.AttentionAggregationResult, contentType: "application/json").ExecuteAsync(context);
        }
        catch (AttentionQueryException exception)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(exception.Message, context.RequestAborted);
        }
    }
}

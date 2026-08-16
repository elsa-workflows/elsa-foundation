using Elsa.Api.AspNetCore;
using Elsa.Attention.Core;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Attention.Api;

/// <summary>Maps the attention aggregation surface using ordinary ASP.NET Core endpoints.</summary>
public static class AttentionApi
{
    private const string OwnerId = "Elsa.Attention.Api";

    public static void MapAttentionApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RequestDelegate handler = HandleGetAttentionItemsAsync;
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        endpoints.MapGet(AttentionRoutes.Items, handler)
            .WithName("ElsaAttentionApiEndpointsGetAttentionItems")
            .WithHostApplicationOpenApiTag(endpoints.ServiceProvider)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAnyPermission(PermissionKey.Wildcard, AttentionPermissions.Read)
            .WithMetadata(
                descriptionMethod,
                new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(AttentionAggregationResult), ["application/json"]),
                new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(string), ["text/plain"]),
                new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []),
                new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), []));
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
            await Results.Json(result, AttentionJsonContext.Default.AttentionAggregationResult).ExecuteAsync(context);
        }
        catch (AttentionQueryException exception)
        {
            await Results.Text(exception.Message, "text/plain", statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(context);
        }
    }
}

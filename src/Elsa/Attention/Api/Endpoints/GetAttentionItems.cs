using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Attention.Core;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Http;

namespace Elsa.Attention.Api.Endpoints;

internal sealed class GetAttentionItems(IAttentionAggregationService aggregationService)
    : ElsaEndpointWithoutRequest<AttentionAggregationResult>
{
    public override void Configure()
    {
        Get(AttentionRoutes.Items);
        ConfigurePermissions();
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var contributorIds = HttpContext.Request.Query["contributorId"]
            .Select(value => value ?? string.Empty)
            .ToArray();
        var context = new AttentionQueryContext(
            HttpContext.User,
            HttpContext.User.FindFirst(IdentityClaimTypes.TenantId)?.Value);

        try
        {
            var response = await aggregationService.AggregateAsync(
                new AttentionQuery(context, contributorIds.Length == 0 ? null : contributorIds),
                cancellationToken);
            await Send.OkAsync(response, cancellationToken);
        }
        catch (AttentionQueryException exception)
        {
            await Send.StringAsync(
                exception.Message,
                StatusCodes.Status400BadRequest,
                cancellation: cancellationToken);
        }
    }
}

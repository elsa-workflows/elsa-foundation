using Elsa.Api.AspNetCore;
using Elsa.Expressions.Api.Authorization;
using Elsa.Expressions.Api.Constants;
using Elsa.Expressions.Api.Models;
using Elsa.Expressions.Api.Requests;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Expressions.Api;

/// <summary>Maps the expression descriptor surfaces using ordinary ASP.NET Core endpoints.</summary>
public static class ExpressionsApi
{
    private const string OwnerId = "Elsa.Expressions.Api";

    public static void MapExpressionsApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var descriptors = new RequestDelegate(HandleExpressionDescriptorsAsync);
        var variables = new RequestDelegate(HandleVariableTypeDescriptorsAsync);
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        endpoints.MapGet(RouteConstants.Descriptors, descriptors)
            .WithName("ElsaExpressionsApiEndpointsListExpressionDescriptors")
            .WithHostApplicationOpenApiTag(endpoints.ServiceProvider)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(ExpressionsPermissions.Read)
            .WithMetadata(
                descriptionMethod,
                Response(StatusCodes.Status200OK, typeof(ExpressionDescriptorsResponse)),
                Unauthorized(),
                Forbidden());

        endpoints.MapGet(RouteConstants.VariableTypes, variables)
            .WithName("ElsaExpressionsApiEndpointsListVariableTypeDescriptors")
            .WithHostApplicationOpenApiTag(endpoints.ServiceProvider)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(ExpressionsPermissions.Read)
            .WithMetadata(
                descriptionMethod,
                Response(StatusCodes.Status200OK, typeof(VariableTypeDescriptorsResponse)),
                Unauthorized(),
                Forbidden());
    }

    private static async Task HandleExpressionDescriptorsAsync(HttpContext context)
    {
        var sender = context.RequestServices.GetRequiredService<IRequestSender>();
        var response = await sender.Send(new ListExpressionDescriptors(), context.RequestAborted);
        await Results.Json(response, ExpressionsJsonContext.Default.ExpressionDescriptorsResponse, contentType: "application/json").ExecuteAsync(context);
    }

    private static async Task HandleVariableTypeDescriptorsAsync(HttpContext context)
    {
        var sender = context.RequestServices.GetRequiredService<IRequestSender>();
        var response = await sender.Send(new ListVariableTypeDescriptors(), context.RequestAborted);
        await Results.Json(response, ExpressionsJsonContext.Default.VariableTypeDescriptorsResponse, contentType: "application/json").ExecuteAsync(context);
    }

    private static ProducesResponseTypeMetadata Response(int statusCode, Type bodyType) =>
        new(statusCode, bodyType, ["application/json"]);

    private static ProducesResponseTypeMetadata Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, typeof(void), []);

    private static ProducesResponseTypeMetadata Forbidden() =>
        new(StatusCodes.Status403Forbidden, typeof(void), []);
}

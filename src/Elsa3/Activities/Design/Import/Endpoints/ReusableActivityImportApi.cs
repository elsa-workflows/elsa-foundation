using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elsa3.Activities.Design.Import.Endpoints;

/// <summary>Maps the Elsa 3 reusable-activity import surface using ordinary ASP.NET Core endpoints.</summary>
public static class ReusableActivityImportApi
{
    private const string OwnerId = "Elsa3.Activities.Design.Import";

    public static void MapReusableActivityImportApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName
                               ?? typeof(ReusableActivityImportApi).Assembly.GetName().Name!;
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");
        var unauthorized = new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []);
        var forbidden = new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), []);

        endpoints.MapPost("migration/elsa3/reusable-activities/collections", HandleUploadAsync)
            .WithName("Elsa3ActivitiesDesignImportEndpointsUploadReusableActivityCollectionEndpoint")
            .WithTags(applicationName)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(Elsa3ImportPermissions.Manage)
            .WithMetadata(
                descriptionMethod,
                Response(StatusCodes.Status200OK, typeof(ReusableActivityImportUploadResult)),
                unauthorized,
                forbidden);

        endpoints.MapGet("migration/elsa3/reusable-activities/collections/{collectionHandle}/analysis", HandleAnalyzeAsync)
            .WithName("Elsa3ActivitiesDesignImportEndpointsAnalyzeReusableActivityCollectionEndpoint")
            .WithTags(applicationName)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(Elsa3ImportPermissions.Read)
            .WithMetadata(descriptionMethod, Response(StatusCodes.Status200OK, typeof(ReusableActivityImportAnalysisPage)), unauthorized, forbidden);

        endpoints.MapPost("migration/elsa3/reusable-activities/collections/{collectionHandle}/selection", HandleSelectionAsync)
            .WithName("Elsa3ActivitiesDesignImportEndpointsExpandReusableActivityImportSelectionEndpoint")
            .WithTags(applicationName)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(Elsa3ImportPermissions.Read)
            .WithMetadata(descriptionMethod, Accepts(typeof(ReusableActivityImportSelectionRequest)), Response(StatusCodes.Status200OK, typeof(ReusableActivityImportSelectionReadiness)), unauthorized, forbidden);

        endpoints.MapPost("migration/elsa3/reusable-activities/collections/{collectionHandle}/apply", HandleApplyAsync)
            .WithName("Elsa3ActivitiesDesignImportEndpointsApplyReusableActivityImportEndpoint")
            .WithTags(applicationName)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(Elsa3ImportPermissions.Manage)
            .WithMetadata(descriptionMethod, Accepts(typeof(ReusableActivityImportApplyHttpRequest)), Response(StatusCodes.Status200OK, typeof(ReusableActivityImportReceipt)), unauthorized, forbidden);

        endpoints.MapGet("migration/elsa3/reusable-activities/imports/{idempotencyKey}", HandleStatusAsync)
            .WithName("Elsa3ActivitiesDesignImportEndpointsGetReusableActivityImportStatusEndpoint")
            .WithTags(applicationName)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(Elsa3ImportPermissions.Read)
            .WithMetadata(descriptionMethod, Response(StatusCodes.Status200OK, typeof(ReusableActivityImportReceipt)), unauthorized, forbidden);
    }

    private static async Task HandleUploadAsync(HttpContext context)
    {
        try
        {
            var service = context.RequestServices.GetRequiredService<IReusableActivityImportOperationService>();
            var result = await service.UploadAsync(
                context.Request.Body,
                context.Request.ContentLength,
                ReusableActivityImportHttp.Scope(context.User),
                context.RequestAborted);
            context.Response.Headers.Location = $"/migration/elsa3/reusable-activities/collections/{Uri.EscapeDataString(result.CollectionHandle)}/analysis";
            await Results.Json(
                result,
                ReusableActivityImportJsonContext.Default.ReusableActivityImportUploadResult,
                contentType: "application/json; charset=utf-8",
                statusCode: StatusCodes.Status201Created).ExecuteAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReusableActivityImportHttp.WriteProblemAsync(context, exception, context.RequestAborted);
        }
    }

    private static async Task HandleAnalyzeAsync(HttpContext context)
    {
        try
        {
            var service = context.RequestServices.GetRequiredService<IReusableActivityImportOperationService>();
            var options = context.RequestServices.GetRequiredService<IOptions<ReusableActivityImportOptions>>();
            var result = await service.AnalyzeAsync(
                Route(context, "collectionHandle"),
                QueryInt(context, "offset") ?? 0,
                QueryInt(context, "limit") ?? options.Value.DefaultPageSize,
                ReusableActivityImportHttp.Scope(context.User),
                context.RequestAborted);
            await Results.Json(
                result,
                ReusableActivityImportJsonContext.Default.ReusableActivityImportAnalysisPage,
                contentType: "application/json; charset=utf-8").ExecuteAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReusableActivityImportHttp.WriteProblemAsync(context, exception, context.RequestAborted);
        }
    }

    private static async Task HandleSelectionAsync(HttpContext context)
    {
        var request = await ReadJsonAsync(context, ReusableActivityImportJsonContext.Default.ReusableActivityImportSelectionRequest);
        if (request is null)
            return;

        try
        {
            var service = context.RequestServices.GetRequiredService<IReusableActivityImportOperationService>();
            var result = await service.ExpandSelectionAsync(
                Route(context, "collectionHandle"), request.PlanId, request.SelectedSourceVersionIds,
                ReusableActivityImportHttp.Scope(context.User), context.RequestAborted);
            await Results.Json(
                result,
                ReusableActivityImportJsonContext.Default.ReusableActivityImportSelectionReadiness,
                contentType: "application/json; charset=utf-8").ExecuteAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReusableActivityImportHttp.WriteProblemAsync(context, exception, context.RequestAborted);
        }
    }

    private static async Task HandleApplyAsync(HttpContext context)
    {
        var request = await ReadJsonAsync(context, ReusableActivityImportJsonContext.Default.ReusableActivityImportApplyHttpRequest);
        if (request is null)
            return;

        try
        {
            var service = context.RequestServices.GetRequiredService<IReusableActivityImportOperationService>();
            var result = await service.ApplyAsync(
                Route(context, "collectionHandle"), request.PlanId, request.SelectedSourceVersionIds,
                request.IdempotencyKey, ReusableActivityImportHttp.Scope(context.User), context.RequestAborted);
            context.Response.Headers.Location = $"/migration/elsa3/reusable-activities/imports/{Uri.EscapeDataString(request.IdempotencyKey)}";
            var status = result.Status == ReusableActivityImportReceiptStatus.Applied
                ? StatusCodes.Status201Created
                : StatusCodes.Status200OK;
            await Results.Json(
                result,
                ReusableActivityImportJsonContext.Default.ReusableActivityImportReceipt,
                contentType: "application/json; charset=utf-8",
                statusCode: status).ExecuteAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReusableActivityImportHttp.WriteProblemAsync(context, exception, context.RequestAborted);
        }
    }

    private static async Task HandleStatusAsync(HttpContext context)
    {
        try
        {
            var service = context.RequestServices.GetRequiredService<IReusableActivityImportOperationService>();
            var result = await service.GetStatusAsync(
                Route(context, "idempotencyKey"), ReusableActivityImportHttp.Scope(context.User), context.RequestAborted);
            await Results.Json(
                result,
                ReusableActivityImportJsonContext.Default.ReusableActivityImportReceipt,
                contentType: "application/json; charset=utf-8").ExecuteAsync(context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await ReusableActivityImportHttp.WriteProblemAsync(context, exception, context.RequestAborted);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context, JsonTypeInfo<T> jsonTypeInfo)
        where T : class
    {
        try
        {
            var value = await context.Request.ReadFromJsonAsync(jsonTypeInfo, context.RequestAborted);
            if (value is not null)
                return value;
        }
        catch (JsonException exception)
        {
            await ReusableActivityImportHttp.WriteProblemAsync(context, exception, context.RequestAborted);
            return null;
        }

        await Results.BadRequest().ExecuteAsync(context);
        return null;
    }

    private static string Route(HttpContext context, string key) =>
        context.Request.RouteValues.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static int? QueryInt(HttpContext context, string key) =>
        context.Request.Query.TryGetValue(key, out var values) && int.TryParse(values.FirstOrDefault(), out var value) ? value : null;

    private static AcceptsMetadata Accepts(Type type) => new(["application/json"], type, false);

    private static ProducesResponseTypeMetadata Response(int statusCode, Type type) =>
        new(statusCode, type, ["application/json"]);
}

public sealed record ReusableActivityImportSelectionRequest(string PlanId, IReadOnlyCollection<string> SelectedSourceVersionIds);

public sealed record ReusableActivityImportApplyHttpRequest(string PlanId, IReadOnlyCollection<string> SelectedSourceVersionIds, string IdempotencyKey);

internal static class Elsa3ImportPermissions
{
    public const string Read = "elsa3-import.read";
    public const string Manage = "elsa3-import.manage";
}

public static class ReusableActivityImportHttp
{
    private const string ElsaTenantClaim = "elsa.identity.tenant_id";
    private const string ConventionalTenantClaim = "tenant_id";

    public static ReusableActivityImportAccessScope Scope(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst("sub")?.Value
                     ?? user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId))
            throw new ReusableActivityImportNotFoundException("The authorized Elsa 3 import caller has no stable user identity.");
        var tenantId = user.FindFirst(ElsaTenantClaim)?.Value ?? user.FindFirst(ConventionalTenantClaim)?.Value;
        return new(tenantId, userId);
    }

    public static Task WriteProblemAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title) = exception switch
        {
            ReusableActivityImportNotFoundException => (404, "elsa3.import.not-found", "Elsa 3 import resource not found"),
            ReusableActivityImportExpiredException => (410, "elsa3.import.collection-expired", "Elsa 3 import collection expired"),
            ReusableActivityImportIdempotencyConflictException => (409, "elsa3.import.idempotency-conflict", "Idempotency key conflict"),
            ReusableActivityImportCollisionException => (409, "elsa3.import.identity-collision", "Import identity collision"),
            ReusableActivityImportValidationException => (422, "elsa3.import.validation-failed", "Elsa 3 import validation failed"),
            ReusableActivityImportPayloadException => (400, "elsa3.import.payload-invalid", "Elsa 3 import payload invalid"),
            ArgumentException => (400, "elsa3.import.request-invalid", "Elsa 3 import request invalid"),
            _ => (500, "elsa3.import.unexpected", "Elsa 3 import failed")
        };
        var problem = new ReusableActivityImportProblem(
            status,
            $"https://elsa.dev/problems/{code}",
            title,
            status == 500 ? "The Elsa 3 import could not be completed." : exception.Message,
            context.Request.Path,
            code,
            exception is ReusableActivityImportValidationException validation ? validation.Diagnostics : null);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, ReusableActivityImportJsonContext.Default.ReusableActivityImportProblem),
            cancellationToken);
    }
}

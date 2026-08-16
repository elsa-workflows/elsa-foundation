using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Exceptions;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Workflows.Design.Core.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints;

/// <summary>Maps the BPMN interchange surface using ordinary ASP.NET Core endpoints.</summary>
public static class BpmnInterchangeApi
{
    private const string OwnerId = "Elsa.Activities.Bpmn.Interchange";

    public static void MapBpmnInterchangeApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName
                               ?? typeof(BpmnInterchangeApi).Assembly.GetName().Name!;
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        endpoints.MapPost("interchange/bpmn/analyze", HandleAnalyzeAsync)
            .WithName("ElsaActivitiesBpmnInterchangeEndpointsAnalyzeBpmnDocumentEndpoint")
            .WithTags(applicationName)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(BpmnInterchangePermissions.Read)
            .WithMetadata(descriptionMethod, Accepts(typeof(AnalyzeBpmnDocumentRequest)), Response(StatusCodes.Status200OK, typeof(BpmnImportAnalysis)), Unauthorized(), Forbidden());

        endpoints.MapPost("interchange/bpmn/import", HandleImportAsync)
            .WithName("ElsaActivitiesBpmnInterchangeEndpointsImportBpmnDocumentEndpoint")
            .WithTags(applicationName)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(BpmnInterchangePermissions.Manage)
            .WithMetadata(descriptionMethod, Accepts(typeof(ImportBpmnDocumentRequest)), Response(StatusCodes.Status200OK, typeof(BpmnImportResult)), Unauthorized(), Forbidden());

        endpoints.MapPost("interchange/bpmn/export", HandleExportAsync)
            .WithName("ElsaActivitiesBpmnInterchangeEndpointsExportBpmnDocumentEndpoint")
            .WithTags(applicationName)
            .WithOwner(OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(BpmnInterchangePermissions.Read)
            .WithMetadata(descriptionMethod, Accepts(typeof(ExportBpmnDocumentRequest)), Response(StatusCodes.Status200OK, typeof(ExportBpmnDocumentResult)), Unauthorized(), Forbidden());
    }

    private static async Task HandleAnalyzeAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<AnalyzeBpmnDocumentRequest>(context);
        if (request is null)
            return;

        try
        {
            var importer = context.RequestServices.GetRequiredService<IBpmnDocumentImporter>();
            await Results.Json(importer.Analyze(request.Xml, new BpmnImportOptions { ProcessId = request.ProcessId })).ExecuteAsync(context);
        }
        catch (BpmnInterchangeException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
    }

    private static async Task HandleImportAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ImportBpmnDocumentRequest>(context);
        if (request is null)
            return;

        try
        {
            var importer = context.RequestServices.GetRequiredService<IBpmnDocumentImporter>();
            await Results.Json(importer.Import(request.Xml, new BpmnImportOptions
            {
                ProcessId = request.ProcessId,
                NodeIdPrefix = request.NodeIdPrefix
            })).ExecuteAsync(context);
        }
        catch (BpmnInterchangeException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
    }

    private static async Task HandleExportAsync(HttpContext context)
    {
        var request = await ReadJsonAsync<ExportBpmnDocumentRequest>(context);
        if (request is null)
            return;

        try
        {
            var exporter = context.RequestServices.GetRequiredService<IBpmnDocumentExporter>();
            await Results.Json(new ExportBpmnDocumentResult(exporter.Export(request.ProcessNode, new BpmnExportOptions
            {
                ProcessId = request.ProcessId
            }))).ExecuteAsync(context);
        }
        catch (BpmnInterchangeException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context)
        where T : class
    {
        try
        {
            var request = await context.Request.ReadFromJsonAsync<T>(context.RequestAborted);
            if (request is not null)
                return request;
        }
        catch (JsonException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
            return null;
        }

        await WriteLegacyErrorAsync(context, "A request body is required.", StatusCodes.Status400BadRequest);
        return null;
    }

    private static Task WriteLegacyErrorAsync(HttpContext context, string message, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            errors = new Dictionary<string, string[]> { ["generalErrors"] = [message] },
            message = "One or more errors occurred!",
            statusCode
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), context.RequestAborted);
    }

    private static ProducesResponseTypeMetadata Response(int statusCode, Type type) =>
        new(statusCode, type, ["application/json"]);

    private static AcceptsMetadata Accepts(Type type) =>
        new(["application/json"], type, false);

    private static ProducesResponseTypeMetadata Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, typeof(void), []);

    private static ProducesResponseTypeMetadata Forbidden() =>
        new(StatusCodes.Status403Forbidden, typeof(void), []);
}

public sealed record AnalyzeBpmnDocumentRequest(string Xml, string? ProcessId = null);

public sealed record ImportBpmnDocumentRequest(string Xml, string? ProcessId = null, string? NodeIdPrefix = null);

public sealed record ExportBpmnDocumentRequest(ActivityNode ProcessNode, string? ProcessId = null);

public sealed record ExportBpmnDocumentResult(string Xml);

internal static class BpmnInterchangePermissions
{
    public const string Read = "bpmn-interchange.read";
    public const string Manage = "bpmn-interchange.manage";
}

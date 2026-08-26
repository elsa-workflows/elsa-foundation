using Elsa.Api.Endpoints;
using Elsa.Workflows.Design.Core.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Activities.Bpmn.Interchange.Endpoints;

/// <summary>Maps the BPMN interchange surface using ordinary ASP.NET Core endpoints.</summary>
public static class BpmnInterchangeApi
{
    internal const string OwnerId = "Elsa.Activities.Bpmn.Interchange";

    public static void MapBpmnInterchangeApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The published documents tag this surface with the host application name, resolved at
        // composition time exactly as the hand-written mapper did.
        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName
                              ?? typeof(BpmnInterchangeApi).Assembly.GetName().Name!;
        var api = endpoints.MapModuleEndpoints(
            OwnerId,
            BpmnInterchangeJsonContext.Default,
            jsonContentType: "application/json; charset=utf-8",
            tag: applicationName);

        api.MapEndpointsFrom(typeof(BpmnInterchangeApi).Assembly);
    }
}

public sealed record AnalyzeBpmnDocumentRequest(string Xml, string? ProcessId = null);

public sealed record ImportBpmnDocumentRequest(string Xml, string? ProcessId = null, string? NodeIdPrefix = null);

public sealed record ExportBpmnDocumentRequest(ActivityNode ProcessNode, string? ProcessId = null);

public sealed record ExportBpmnDocumentResult(string Xml);

internal sealed record BpmnInterchangeError(
    Dictionary<string, string[]> Errors,
    string Message,
    int StatusCode);

internal static class BpmnInterchangePermissions
{
    public const string Read = "bpmn-interchange.read";
    public const string Manage = "bpmn-interchange.manage";
}

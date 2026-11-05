using Elsa.Activities.Bpmn.Interchange.Models;
using NativeEndpoints;
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
        var api = endpoints.MapEndpointGroup(
            OwnerId,
            BpmnInterchangeJsonContext.Default,
            jsonContentType: "application/json; charset=utf-8",
            tag: applicationName);

        // The optional authored publisher shares this package, but its endpoint is mapped only
        // by its own feature. Ordinary interchange must not expose an uncomposed write action.
        api.MapEndpoint<Documents.Analyze.Endpoint>();
        api.MapEndpoint<Documents.Import.Endpoint>();
        api.MapEndpoint<Documents.Export.Endpoint>();
    }
}

/// <param name="ConfirmedBindings">Element ids whose retained task binding the author confirmed; see <see cref="BpmnImportOptions.ConfirmedBindings"/>.</param>
public sealed record AnalyzeBpmnDocumentRequest(string Xml, string? ProcessId = null, IReadOnlyList<string>? ConfirmedBindings = null);

/// <param name="ConfirmedBindings">Element ids whose retained task binding the author confirmed; see <see cref="BpmnImportOptions.ConfirmedBindings"/>.</param>
public sealed record ImportBpmnDocumentRequest(string Xml, string? ProcessId = null, string? NodeIdPrefix = null, IReadOnlyList<string>? ConfirmedBindings = null);

public sealed record ExportBpmnDocumentRequest(ActivityNode ProcessNode, string? ProcessId = null);

/// <param name="Omissions">Every task binding the document does not carry, each with why.</param>
public sealed record ExportBpmnDocumentResult(string Xml, IReadOnlyList<BpmnExportOmission>? Omissions = null);

internal sealed record BpmnInterchangeError(
    Dictionary<string, string[]> Errors,
    string Message,
    int StatusCode);

internal static class BpmnInterchangePermissions
{
    public const string Read = "bpmn-interchange:read";
    public const string Manage = "bpmn-interchange:manage";
}

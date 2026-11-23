using Elsa.Api.AspNetCore;
using Elsa.Studio.Api.Contracts;

namespace Elsa.Studio.Hosting;

internal static class ActiveShellStudioApiEndpoint
{
    private static readonly System.Text.Json.JsonSerializerOptions WireJson = CreateWireJson();

    private static System.Text.Json.JsonSerializerOptions CreateWireJson()
    {
        var options = Elsa.Serialization.Core.JsonBaseline.CreateCopy();
        Elsa.Serialization.Core.StrictDateTimeConverters.Configure(options, apiMode: true);
        options.MakeReadOnly();
        return options;
    }

    public static IEndpointRouteBuilder MapActiveShellStudioApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/_elsa/studio/modules", (IStudioModuleManifestProvider manifests, StudioHostingSettings settings, HttpContext context) =>
            ReadAsync(manifests, settings, context, false)).WithElsaJsonResponseHeaders().WithOrder(-1000);
        endpoints.MapGet("/_elsa/studio/module-registry", (IStudioModuleManifestProvider manifests, StudioHostingSettings settings, HttpContext context) =>
            ReadAsync(manifests, settings, context, true)).WithElsaJsonResponseHeaders().WithOrder(-1000);
        return endpoints;
    }

    private static async Task<IResult> ReadAsync(IStudioModuleManifestProvider manifests, StudioHostingSettings settings, HttpContext context, bool moduleRegistry)
    {
        try
        {
            return moduleRegistry
                ? Results.Json(await manifests.GetModuleRegistry(context.RequestAborted), options: WireJson)
                : Results.Json(await manifests.GetModules(context.RequestAborted), options: WireJson);
        }
        catch (StudioHostingException error)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ActiveShellStudioApiEndpoint))
                .LogWarning(error, "Studio shell {ShellName} is unavailable for module discovery.", settings.ShellName);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}

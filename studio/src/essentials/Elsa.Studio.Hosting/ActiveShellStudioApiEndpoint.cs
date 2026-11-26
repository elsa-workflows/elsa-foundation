using Elsa.Studio.Api.Contracts;

namespace Elsa.Studio.Hosting;

internal static class ActiveShellStudioApiEndpoint
{
    public static IEndpointRouteBuilder MapActiveShellStudioApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/_elsa/studio/modules", (IStudioModuleManifestProvider manifests, StudioHostingSettings settings, HttpContext context) =>
            ReadAsync(manifests, settings, context, false)).WithOrder(-1000);
        endpoints.MapGet("/_elsa/studio/module-registry", (IStudioModuleManifestProvider manifests, StudioHostingSettings settings, HttpContext context) =>
            ReadAsync(manifests, settings, context, true)).WithOrder(-1000);
        return endpoints;
    }

    private static async Task<IResult> ReadAsync(IStudioModuleManifestProvider manifests, StudioHostingSettings settings, HttpContext context, bool moduleRegistry)
    {
        try
        {
            return moduleRegistry
                ? Results.Ok(await manifests.GetModuleRegistry(context.RequestAborted))
                : Results.Ok(await manifests.GetModules(context.RequestAborted));
        }
        catch (StudioHostingException error)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ActiveShellStudioApiEndpoint))
                .LogWarning(error, "Studio shell {ShellName} is unavailable for module discovery.", settings.ShellName);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}

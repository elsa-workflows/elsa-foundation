using CShells;
using CShells.Lifecycle;
using Elsa.Studio.Api.Contracts;

namespace Elsa.Studio.Web;

internal static class ActiveShellStudioApiEndpoint
{
    // Both routes are registered with a strongly negative order so they are matched ahead of the shell's
    // own catch-all/path-routed endpoints (mapped by MapShells) and the SPA fallback. Shell endpoints
    // default to order 0; -1000 gives these Studio API routes ample priority headroom without having to
    // track the exact order the shell assigns, ensuring "/_elsa/studio/*" is never swallowed by the
    // fallback-to-index handler.
    private const int RoutePriorityOrder = -1000;

    public static IEndpointRouteBuilder MapActiveShellStudioApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/_elsa/studio/modules", async (IShellRegistry shellRegistry, HttpContext httpContext) =>
        {
            var provider = await GetActiveShellServiceProviderAsync(shellRegistry, httpContext.RequestAborted);
            var response = await provider
                .GetRequiredService<IStudioModuleManifestProvider>()
                .GetModules(httpContext.RequestAborted);

            return Results.Ok(response);
        }).WithOrder(RoutePriorityOrder);

        endpoints.MapGet("/_elsa/studio/module-registry", async (IShellRegistry shellRegistry, HttpContext httpContext) =>
        {
            var provider = await GetActiveShellServiceProviderAsync(shellRegistry, httpContext.RequestAborted);
            var response = await provider
                .GetRequiredService<IStudioModuleManifestProvider>()
                .GetModuleRegistry(httpContext.RequestAborted);

            return Results.Ok(response);
        }).WithOrder(RoutePriorityOrder);

        return endpoints;
    }

    private static async Task<IServiceProvider> GetActiveShellServiceProviderAsync(IShellRegistry shellRegistry, CancellationToken cancellationToken)
    {
        var shell = shellRegistry.GetActive(ShellConstants.DefaultShellName)
            ?? await shellRegistry.GetOrActivateAsync(ShellConstants.DefaultShellName, cancellationToken);

        return shell.ServiceProvider;
    }
}

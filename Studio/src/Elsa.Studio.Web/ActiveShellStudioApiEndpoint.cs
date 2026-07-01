using CShells;
using CShells.Lifecycle;
using Elsa.Studio.Api.Contracts;

namespace Elsa.Studio.Web;

internal static class ActiveShellStudioApiEndpoint
{
    public static IEndpointRouteBuilder MapActiveShellStudioApi(this IEndpointRouteBuilder endpoints)
    {
        // Lightweight, unauthenticated reachability probe for the Studio SPA's "Backend API" health tile.
        // Does not touch the shell registry so it answers even while shells are (re)activating.
        endpoints.MapGet("/_elsa/health", () => Results.Ok(new { status = "healthy" })).WithOrder(-1000);

        endpoints.MapGet("/_elsa/studio/modules", async (IShellRegistry shellRegistry, HttpContext httpContext) =>
        {
            var provider = await GetActiveShellServiceProviderAsync(shellRegistry, httpContext.RequestAborted);
            var response = await provider
                .GetRequiredService<IStudioModuleManifestProvider>()
                .GetModules(httpContext.RequestAborted);

            return Results.Ok(response);
        }).WithOrder(-1000);

        endpoints.MapGet("/_elsa/studio/module-registry", async (IShellRegistry shellRegistry, HttpContext httpContext) =>
        {
            var provider = await GetActiveShellServiceProviderAsync(shellRegistry, httpContext.RequestAborted);
            var response = await provider
                .GetRequiredService<IStudioModuleManifestProvider>()
                .GetModuleRegistry(httpContext.RequestAborted);

            return Results.Ok(response);
        }).WithOrder(-1000);

        return endpoints;
    }

    private static async Task<IServiceProvider> GetActiveShellServiceProviderAsync(IShellRegistry shellRegistry, CancellationToken cancellationToken)
    {
        var shell = shellRegistry.GetActive(ShellConstants.DefaultShellName)
            ?? await shellRegistry.GetOrActivateAsync(ShellConstants.DefaultShellName, cancellationToken);

        return shell.ServiceProvider;
    }
}

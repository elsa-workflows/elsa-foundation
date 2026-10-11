using CShells;
using CShells.Lifecycle;

namespace Elsa.Studio.Web;

/// <summary>
/// Activates the default Studio shell before endpoint routing runs, so the endpoints a shell feature maps (theme
/// management, a module's bridge, …) are matched by routing — and therefore gated by the
/// authorization middleware — on the very first request as well as every later one.
///
/// <para>Without this, CShells activates a cold shell inside its own middleware, which runs <i>after</i> authorization,
/// and then re-matches the request against the freshly registered shell endpoints. Routing had matched the host's
/// <c>/_elsa/{**path}</c> fallback (which carries no policy), so authorization had already passed the request through:
/// the first request to a gated shell endpoint after a cold start would execute without its policy being evaluated.
/// Activating the shell first means routing sees the real endpoint and authorization enforces its policy.</para>
///
/// <para>Activation is best-effort: if it fails for any non-fatal reason other than the caller aborting the request,
/// the failure is logged and the request continues, so a broken shell cannot turn static files or the SPA into 500s.
/// CShells' own middleware answers shell requests downstream as before.</para>
/// </summary>
internal static class StudioShellActivation
{
    public static IApplicationBuilder UseStudioDefaultShellActivation(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await context.RequestServices.GetRequiredService<IShellRegistry>().GetOrActivateDefaultShellAsync(context.RequestAborted);
            }
            catch (Exception ex) when (ex is ShellBlueprintNotFoundException or ShellBlueprintUnavailableException)
            {
                // No usable default shell blueprint: CShells' own middleware answers this case (404/503) downstream.
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            // Out of memory is not best-effort material; let it escape.
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(StudioShellActivation))
                    .LogWarning(ex, "Activating the default Studio shell failed; continuing the request without it.");
            }

            await next(context);
        });

    /// <summary>The active default shell, activating it first when it is cold.</summary>
    public static async Task<IShell> GetOrActivateDefaultShellAsync(this IShellRegistry shells, CancellationToken cancellationToken) =>
        shells.GetActive(ShellConstants.DefaultShellName)
        ?? await shells.GetOrActivateAsync(ShellConstants.DefaultShellName, cancellationToken);
}

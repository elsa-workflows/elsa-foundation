using CShells.Lifecycle;

namespace Elsa.Studio.Hosting;

internal static class StudioShellActivation
{
    public static IApplicationBuilder UseStudioShellActivation(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var settings = context.RequestServices.GetRequiredService<StudioHostingSettings>();
        try
        {
            await context.RequestServices.GetRequiredService<IShellRegistry>().GetOrActivateAsync(settings.ShellName, context.RequestAborted);
        }
        catch (Exception error) when (error is ShellBlueprintNotFoundException or ShellBlueprintUnavailableException) { }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StudioShellActivation))
                .LogWarning(error, "Activating Studio shell {ShellName} failed; continuing the request without it.", settings.ShellName);
        }
        await next(context);
    });

    public static async Task<IShellScope> AcquireScopeAsync(IShellRegistry registry, string shellName, CancellationToken cancellationToken)
    {
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var shell = await registry.GetOrActivateAsync(shellName, cancellationToken);
                try { return shell.BeginScope(); }
                catch (InvalidOperationException error) when (attempt == 0 && (error is ObjectDisposedException || shell.State == ShellLifecycleState.Disposed)) { }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new StudioHostingException($"Could not acquire a request scope for Studio shell '{shellName}'.", error);
        }
    }
}

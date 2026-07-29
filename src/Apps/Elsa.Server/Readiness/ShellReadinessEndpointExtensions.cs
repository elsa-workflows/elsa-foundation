using CShells.Lifecycle;
using Microsoft.Extensions.Options;

namespace Elsa.Server.Readiness;

public static class ShellReadinessEndpointExtensions
{
    public static IEndpointRouteBuilder MapShellReadiness(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        endpoints.MapGet("/health/ready", (IShellRegistry registry, ShellReadinessState state, IOptions<ShellReadinessOptions> options) =>
        {
            var shellName = options.Value.DefaultShellName;
            var active = registry.GetActive(shellName);
            if (active?.State == ShellLifecycleState.Active)
            {
                return Results.Json(new
                {
                    status = "ready",
                    shell = shellName,
                    generation = active.Descriptor.Generation,
                    durationMs = state.Snapshot.Generation == active.Descriptor.Generation
                        ? state.Snapshot.Duration?.TotalMilliseconds
                        : null
                });
            }

            var snapshot = state.Snapshot;
            return Results.Json(new
            {
                status = snapshot.Status.ToString().ToLowerInvariant(),
                shell = shellName,
                code = snapshot.Code
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return endpoints;
    }
}

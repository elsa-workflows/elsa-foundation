using Elsa.Api.AspNetCore;
using CShells.Lifecycle;
using Microsoft.Extensions.Options;

namespace Elsa.Workbench.Readiness;

public static class ShellReadinessEndpointExtensions
{
    public static IEndpointRouteBuilder MapShellReadiness(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .WithHostOwner("Elsa.Workbench")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .AllowPublic("health", "Reports whether the Workbench host process is live.");
        endpoints.MapGet("/health/ready", (IShellRegistry registry, ShellReadinessState state, IOptions<ShellReadinessOptions> options) =>
        {
            var shellName = options.Value.DefaultShellName;
            var snapshot = state.Snapshot;
            var active = registry.GetActive(shellName);
            if (active?.State == ShellLifecycleState.Active
                && snapshot.Status is ShellReadinessStatus.Ready or ShellReadinessStatus.Disabled)
            {
                var generation = active.Descriptor.Generation;
                var response = new ShellReadinessReadyResponse(
                    "ready",
                    shellName,
                    generation,
                    snapshot.Status == ShellReadinessStatus.Ready && snapshot.Generation == generation
                        ? snapshot.Duration?.TotalMilliseconds
                        : null);
                return Results.Json(response);
            }

            return Results.Json(new
            {
                status = snapshot.Status.ToString().ToLowerInvariant(),
                shell = shellName,
                code = snapshot.Code
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
            .WithHostOwner("Elsa.Workbench")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .AllowPublic("health", "Reports whether the Workbench default shell is ready.");

        return endpoints;
    }
}

/// <summary>
/// The successful <c>/health/ready</c> payload.
/// </summary>
/// <remarks>
/// <para>
/// A successful response always identifies the shell generation that is active when the response is built.
/// During the initial warmup, HTTP 200 is withheld until the terminal readiness snapshot is published, so a
/// successful warmup response for that generation includes <see cref="DurationMs"/>.
/// </para>
/// <para>
/// <see cref="DurationMs"/> is intentionally nullable for a generation promoted by reload or external
/// activation, because those paths do not publish a warmup duration. Such a generation is still ready when
/// the active shell is serving and the readiness state is terminal.
/// </para>
/// </remarks>
public sealed record ShellReadinessReadyResponse(
    string Status,
    string Shell,
    int Generation,
    double? DurationMs);

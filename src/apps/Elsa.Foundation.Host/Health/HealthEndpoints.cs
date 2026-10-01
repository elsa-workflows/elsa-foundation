using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Host.Shells;

namespace Elsa.Foundation.Host.Health;

/// <summary>
/// Host-level health probes. These report HOST-infrastructure state only:
/// <list type="bullet">
///   <item><c>/health/live</c> — the process is up and the root pipeline serves. Liveness.</item>
///   <item><c>/health/ready</c> — the configured shell(s) have activated (reached
///   <see cref="ShellLifecycleState.Active"/>). With eager activation on this flips shortly after boot; with
///   lazy activation it flips on each shell's first request. Readiness.</item>
/// </list>
/// Readiness is read live from <see cref="IShellRegistry.GetActive(string)"/> — it reflects the shell's real
/// state, so it is correct whether the shell was activated eagerly or lazily, and it drops back to not-ready
/// for the window a shell is mid-reload. A shell that is not active says why in its <c>reason</c>
/// (<see cref="ShellNotActiveReason"/>), from what eager activation's failed attempts left in
/// <see cref="ShellActivationTracker"/>; the probe itself activates nothing, the eager activation keeps retrying.
/// <para>
/// This deliberately does NOT evaluate feature-level health (a feature's own dependencies — a database, a
/// broker). That lives inside the shell where those dependencies are registered; see the notes in README.
/// </para>
/// </summary>
public static class HealthEndpoints
{
    private const string ConfiguredShellsSection = "CShells:Shells";

    public static IEndpointRouteBuilder MapHostHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .WithHostOwner("Elsa.Foundation.Host")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .AllowPublic("health", "Reports whether the Foundation Host process is live.");

        endpoints.MapGet("/health/ready", (IShellRegistry registry, IConfiguration configuration, ShellActivationTracker tracker) =>
        {
            var shellNames = configuration.GetSection(ConfiguredShellsSection).GetChildren()
                .Select(child => child.Key)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();

            var shells = shellNames.Select(name =>
            {
                var active = registry.GetActive(name);
                return new
                {
                    name,
                    state = active is null ? "inactive" : active.State.ToString(),
                    generation = active?.Descriptor.Generation,
                    active = active?.State == ShellLifecycleState.Active,
                    reason = active?.State == ShellLifecycleState.Active ? null : ShellNotActiveReason.For(active, tracker.FailureOf(name))
                };
            }).ToArray();

            var ready = shells.Length > 0 && shells.All(shell => shell.active);
            return Results.Json(
                new { status = ready ? "ready" : "not-ready", shells },
                statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        })
            .WithHostOwner("Elsa.Foundation.Host")
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .AllowPublic("health", "Reports whether all configured Foundation Host shells are ready.");

        return endpoints;
    }
}

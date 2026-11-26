using CShells.Lifecycle;
using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Elsa.Foundation.Hosting;

/// <summary>
/// Host-level health probes. These report HOST-infrastructure state only:
/// <list type="bullet">
///   <item><c>/health/live</c> — the process is up and the root pipeline serves. Liveness.</item>
///   <item><c>/health/ready</c> — the configured shell(s) have settled as current. With eager activation on this flips shortly after boot; with
///   lazy activation it flips on each shell's first request. Readiness.</item>
/// </list>
/// Readiness is read live from <see cref="ISettledShellRegistry.GetSettledActive(string)"/> on the host's registry.
/// It fails closed without that capability, and drops back to not-ready
/// for the window a shell is mid-reload. A shell that is not active says why in its <c>reason</c>
/// (<see cref="ShellNotActiveReason"/>), from what eager activation's failed attempts left in
/// <see cref="ShellActivationTracker"/>; the probe itself activates nothing, the eager activation keeps retrying.
/// <para>
/// This deliberately does NOT evaluate feature-level health (a feature's own dependencies — a database, a
/// broker). That lives inside the shell where those dependencies are registered; see the notes in README.
/// </para>
/// </summary>
public static class FoundationHealthEndpoints
{
    private const string ConfiguredShellsSection = "CShells:Shells";

    /// <summary>Maps <c>/health/live</c> and <c>/health/ready</c>, owned by <paramref name="hostOwner"/>.</summary>
    /// <remarks>Exclude both paths from shell routing (<c>WithWebRouting</c>) so a probe never resolves a shell.</remarks>
    public static IEndpointRouteBuilder MapFoundationHealth(this IEndpointRouteBuilder endpoints, string hostOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostOwner);

        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .WithHostOwner(hostOwner)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .AllowPublic("health", $"Reports whether the {hostOwner} process is live.");

        endpoints.MapGet("/health/ready", (IShellRegistry registry, IConfiguration configuration, ShellActivationTracker tracker) =>
        {
            var shellNames = configuration.GetSection(ConfiguredShellsSection).GetChildren()
                .Select(child => child.Key)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();

            var shells = shellNames.Select(name =>
            {
                var active = (registry as ISettledShellRegistry)?.GetSettledActive(name);
                var failure = tracker.FailureOf(name);
                return new
                {
                    name,
                    state = active is null ? "inactive" : active.State.ToString(),
                    generation = active?.Descriptor.Generation,
                    active = active is not null,
                    reason = active is not null ? null : ShellNotActiveReason.For(failure is null ? registry.GetActive(name) : null, failure)
                };
            }).ToArray();

            var ready = shells.Length > 0 && shells.All(shell => shell.active);
            return Results.Json(
                new { status = ready ? "ready" : "not-ready", shells },
                statusCode: ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        })
            .WithHostOwner(hostOwner)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .AllowPublic("health", $"Reports whether all configured {hostOwner} shells are ready.");

        return endpoints;
    }
}

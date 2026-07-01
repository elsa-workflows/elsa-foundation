using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.ConsoleStream;

/// <remarks>
/// The console-log-streaming host (capture, SignalR hub, HTTP endpoints) is hosted on the application
/// root rather than in this shell feature — see <c>Program.cs</c> and
/// <see cref="ConsoleStreamStudioServiceCollectionExtensions.AddConsoleStreamStudioHostIfEnabled"/>. As a
/// consequence the feature must be enabled in <c>shells.json</c> at startup for the hub to be hosted;
/// enabling it only at runtime contributes the UI module but does not host the backend hub.
/// </remarks>
[ManifestRuntimeKind(ElsaRuntimeKinds.Studio)]
[ManifestFeatureCategory("Studio")]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Console")]
[ShellFeature(
    name: "ConsoleStream",
    DisplayName = "Console stream",
    Description = "Contributes a live console panel to Elsa Studio that streams console logs from the server backend."
)]
public sealed class ConsoleStreamStudioFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Only the UI module manifest contribution is shell-scoped. The console-log-streaming host
        // (capture, SignalR hub, HTTP endpoints) is hosted on the application root instead, so that
        // long-lived hub connections do not capture this shell's service provider — which is disposed
        // when the shell is recycled at runtime (e.g. on a Nuplane package change).
        services.AddConsoleStreamStudio();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        // Intentionally empty: the console-log-streaming hub is mapped on the application root
        // (see Program.cs / MapConsoleStreamStudioIfEnabled), not within the shell's route scope.
    }
}

using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Studio.ConsoleStream.Extensions;
using Elsa.Studio.ConsoleStream.Options;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.ConsoleStream;

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
    public ConsoleStreamStudioOptions Options { get; set; } = new();

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddConsoleStreamStudio(Options);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapConsoleStreamStudio();
    }
}

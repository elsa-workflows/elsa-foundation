using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Studio.ConsoleStream.Extensions;
using Elsa.Studio.ConsoleStream.Options;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.ConsoleStream;

[ShellFeature(
    name: "ConsoleStream",
    DisplayName = "Console stream",
    Description = "Captures backend stdout/stderr and contributes a live console panel to Elsa Studio."
)]
public sealed class ConsoleStreamStudioFeature : IWebShellFeature
{
    public ConsoleStreamStudioOptions Options { get; set; } = new();

    public void ConfigureServices(IServiceCollection services)
    {
        ConsoleStreamHookInstaller.InstallOnce();
        services.AddConsoleStreamStudio(Options);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapConsoleStreamStudio();
    }
}

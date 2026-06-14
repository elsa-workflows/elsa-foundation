using CShells.AspNetCore.Features;
using CShells.Features;
using Elsa.Features.Abstractions.Extensions;

namespace Elsa.Server;

[ShellFeature(
    name: "CapabilitiesApi",
    DisplayName = "Capabilities API",
    Description = "Exposes active shell backend capabilities for Studio module discovery."
)]
public sealed class CapabilitiesApiFeature : IWebShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddElsaCapabilities();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, IHostEnvironment? environment)
    {
        endpoints.MapElsaCapabilitiesApi();
    }
}

using CShells.Features;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Primitives.Hosting;

[ShellFeature(
    name: "Primitives"
)]
public sealed class PrimitivesFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ISystemClock, SystemClock>();
    }
}

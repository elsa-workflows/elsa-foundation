using CShells.Features;
using Elsa.Mapping.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Mapping;

[ShellFeature(
    name: "Mapping",
    Description = "Provides a default implementation of IObjectMapper"
)]
public class MappingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IObjectMapper, ObjectMapper>();
    }
}

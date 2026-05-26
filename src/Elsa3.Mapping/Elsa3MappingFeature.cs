using CShells.Features;
using Elsa.Mapping.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Mapping;

[ShellFeature(
    name: "Elsa3Mapping",
    Description = "Provides mappings for Elsa3 workflow models"
)]
public class Elsa3MappingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMappingsFrom(GetType().Assembly);
    }
}

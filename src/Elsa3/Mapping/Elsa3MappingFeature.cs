using CShells.Features;
using Elsa3.Mapping.Contracts;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Mapping;

[ShellFeature(
    name: "Elsa3Mapping",
    Description = "Provides converters for Elsa3 workflow models"
)]
public class Elsa3MappingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<Elsa3ArgumentDefinitionToInputOutput>();
        services.AddScoped<Elsa3ActivityToState>();
        services.AddScoped<Elsa3WorkflowDefinitionToState>();
        services.AddScoped<Elsa3WorkflowDefinitionToWorkflowDefinitionVersion>();
        services.AddScoped<IElsa3WorkflowDefinitionImporter, Elsa3WorkflowDefinitionImporter>();
    }
}

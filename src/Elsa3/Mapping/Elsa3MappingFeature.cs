using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa3.Mapping.Contracts;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Mapping;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Elsa3")]
[ManifestFeatureCategory("Mapping")]
[ShellFeature(
    name: "Elsa3Mapping",
    DisplayName = "Elsa 3 Mapping",
    Description = "Provides converters for Elsa3 workflow models"
)]
public class Elsa3MappingFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<Elsa3ArgumentDefinitionToInputOutput>();
        services.AddScoped<Elsa3MemoryReferenceGraph>();
        services.AddScoped<Elsa3ExpressionRewriter>();
        services.AddScoped<Elsa3ValueFlowLowerer>();
        services.AddScoped<Elsa3ActivityToState>();
        services.AddScoped<Elsa3WorkflowDefinitionToState>();
        services.AddScoped<Elsa3WorkflowDefinitionToWorkflowDefinitionVersion>();
        services.AddScoped<IElsa3WorkflowDefinitionImporter, Elsa3WorkflowDefinitionImporter>();
    }
}

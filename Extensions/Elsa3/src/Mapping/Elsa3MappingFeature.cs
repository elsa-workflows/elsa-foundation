using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.ActivityMap.Rewrites;
using Elsa3.Mapping.Contracts;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Resolution;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Activities.Design.Import.Contracts;
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
    public virtual void ConfigureServices(IServiceCollection services)
    {
        // The built-in map is a static table: one per application (§2.5.1). Resolution caches per conversion scope.
        services.AddSingleton<IElsa3ActivityMap, Elsa3BuiltInActivityMap>();
        services.AddScoped<Elsa3ReplacedActivityTypeIndex>();
        services.AddScoped<Elsa3ActivityResolver>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3SwitchValueSynthesisRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3VariableReferenceRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3TerminalPlacementRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3ParallelBranchNamesRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3LoopVariableReferencesRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3HttpTriggerResponseModeRewrite>();
        services.AddScoped<Elsa3ActivityNormalizer>();
        services.AddScoped<Elsa3DefinitionConverter>();
        services.AddScoped<IElsa3DefinitionConverter>(provider => provider.GetRequiredService<Elsa3DefinitionConverter>());
        services.AddScoped<IElsa3NodeConverter>(provider => provider.GetRequiredService<Elsa3DefinitionConverter>());
        services.AddScoped<Elsa3ArgumentDefinitionToInputOutput>();
        services.AddScoped<Elsa3MemoryReferenceGraph>();
        services.AddScoped<Elsa3ExpressionRewriter>();
        services.AddScoped<Elsa3ValueFlowLowerer>();
        services.AddScoped<Elsa3ActivityToState>();
        services.AddScoped<Elsa3WorkflowDefinitionToState>();
        services.AddScoped<Elsa3WorkflowDefinitionToWorkflowDefinitionVersion>();
        services.AddScoped<IReusableActivityImportMaterializer, Elsa3ReusableActivityImportMaterializer>();
        services.AddScoped<IElsa3WorkflowDefinitionImporter, Elsa3WorkflowDefinitionImporter>();
    }
}

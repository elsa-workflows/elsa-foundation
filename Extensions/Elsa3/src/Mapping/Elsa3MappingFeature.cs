using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.ActivityMap.Rewrites;
using Elsa3.Mapping.Contracts;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Internal;
using Elsa3.Mapping.Resolution;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Activities.Design.Import.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        // IElsa3ActivityMap, IElsa3DefinitionConverter and IElsa3NodeConverter are replacement contracts (§2.6.2): the
        // defaults are registered with TryAdd, so a host's own registration wins, and composing more than one fails
        // shell activation.
        Elsa3MappingCompositionCheck.Register(services);

        // The built-in map is a static table: one per application (§2.5.1). Resolution caches per conversion scope.
        services.TryAddSingleton<IElsa3ActivityMap, Elsa3BuiltInActivityMap>();
        services.AddScoped<Elsa3ReplacedActivityTypeIndex>();
        services.AddScoped<Elsa3ActivityResolver>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3SwitchValueSynthesisRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3VariableReferenceRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3TerminalPlacementRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3ParallelBranchNamesRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3LoopVariableReferencesRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3HttpTriggerResponseModeRewrite>();
        services.AddSingleton<IElsa3NodeRewrite, Elsa3ReusableOutputWriteRewrite>();
        services.AddScoped<Elsa3ActivityNormalizer>();
        services.AddScoped<Elsa3DefinitionConverter>();
        services.TryAddScoped<IElsa3DefinitionConverter>(provider => provider.GetRequiredService<Elsa3DefinitionConverter>());
        // The node converter follows whichever definition converter is composed, so the two cannot disagree. A host
        // whose definition converter does not also convert nodes registers its own node converter too.
        services.TryAddScoped<IElsa3NodeConverter>(provider =>
            provider.GetRequiredService<IElsa3DefinitionConverter>() as IElsa3NodeConverter
            ?? throw new InvalidOperationException(
                $"The composed {nameof(IElsa3DefinitionConverter)} does not implement {nameof(IElsa3NodeConverter)}. " +
                $"Register an {nameof(IElsa3NodeConverter)} alongside it, so node and definition conversion follow one policy."));
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

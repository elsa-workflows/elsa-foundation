using CShells.Features;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Graph.Design;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ShellFeature(
    name: "ActivitiesGraphDesign",
    DisplayName = "Activities Graph (Design)",
    Description = "Authored activity-graph validation, dependency discovery, and compilation.",
    DependsOn = new object[] { "ActivitiesDesignApi", "WorkflowsPublishing" })]
public sealed class GraphActivitiesDesignFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Spec 266: the provider reports unordered graph-variable captures with the workflow design validations'
        // analyzer, which this feature's dependency chain composes; a missing analyzer is a composition error.
        services.AddSingleton(sp => new GraphActivityProvider(
            sp.GetRequiredService<IActivityStructureService>(),
            sp.GetRequiredService<ActivityContractAuthoringValidator>(),
            sp.GetService<IServiceScopeFactory>(),
            sp.GetRequiredService<IUnorderedVariableWriteAnalyzer>()));
        services.AddSingleton<IActivityProvider>(sp => sp.GetRequiredService<GraphActivityProvider>());
        services.AddSingleton<IActivityTemplateProviderCompiler>(sp => sp.GetRequiredService<GraphActivityProvider>());
        services.AddSingleton<IActivityTemplateDependencyDiscoverer>(sp => sp.GetRequiredService<GraphActivityProvider>());
        services.AddSingleton<IActivityDataSelectionDocumentProjector>(sp => sp.GetRequiredService<GraphActivityProvider>());
        services.AddSingleton<IActivityProviderReferenceRewriter, GraphActivityProviderReferenceRewriter>();
        services.AddScoped<IDraftValidator, CompleteOutsideGraphDraftValidator>();
    }
}

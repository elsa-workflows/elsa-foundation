using CShells.Features;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Graph.Design.Services;
using Elsa.Platform.PackageManifest.Generator.Hints;
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
    DependsOn = new object[] { "ActivitiesDesignApi", "WorkflowsPublishingApi" })]
public sealed class GraphActivitiesDesignFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<GraphActivityProvider>();
        services.AddSingleton<IActivityProvider>(sp => sp.GetRequiredService<GraphActivityProvider>());
        services.AddSingleton<IActivityTemplateProviderCompiler>(sp => sp.GetRequiredService<GraphActivityProvider>());
        services.AddSingleton<IActivityTemplateDependencyDiscoverer>(sp => sp.GetRequiredService<GraphActivityProvider>());
        services.AddSingleton<IActivityProviderReferenceRewriter, GraphActivityProviderReferenceRewriter>();
    }
}

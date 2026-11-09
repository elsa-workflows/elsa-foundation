using CShells.Features;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Bpmn.Design;

/// <summary>Authored BPMN projection/lowering and exact published-model pinning without an interpreter dependency.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ShellFeature(name: "ActivitiesBpmnDesign", DisplayName = "Activities BPMN (Design)",
    Description = "BPMN authored structures, executable lowering and published-model references.")]
public class ActivitiesBpmnDesignFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityStructureHandler, BpmnStructureHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExecutableCompilationSource, BpmnEmbeddingPinSource>());
    }
}

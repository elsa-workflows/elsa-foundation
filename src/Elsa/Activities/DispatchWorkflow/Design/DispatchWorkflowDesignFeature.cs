using CShells.Features;
using Elsa.Activities.DispatchWorkflow.Design.Services;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.DispatchWorkflow.Design;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Activities")]
[ManifestFeatureCategory("Design")]
[ShellFeature(
    name: "ActivitiesDispatchWorkflowDesign",
    DisplayName = "Activities Dispatch Workflow (Design)",
    Description = "Provides live Published workflow options and exact publication pins for DispatchWorkflow.",
    DependsOn = new object[] { "WorkflowsDesignApi", "WorkflowsPublishingApi" })]
public class DispatchWorkflowDesignFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityInputOptionsProvider, WorkflowDefinitionOptionsProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExecutableNodeMetadataSource, DispatchPinSource>());
    }
}

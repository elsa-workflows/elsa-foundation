using CShells.Features;
using Elsa.Activities.Bpmn.Core;
using Elsa.Jobs.Bpmn.Services;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Jobs.Bpmn;

/// <summary>Connects Jobs native workers to their actual BPMN model owner, admission and retained root input.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Jobs")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "JobsBpmn", DisplayName = "Jobs BPMN integration",
    Description = "Associates Jobs service workers with genuine BPMN owners and exact result-input permissions.",
    DependsOn = new object[] { "JobsWorkflowsRuntime", "ActivitiesBpmnHosting" })]
public class JobsBpmnFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IJobWorkflowAssociationSource, JobsBpmnAssociationSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IBpmnNativeWorkResultValidator, JobsBpmnResultValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IBpmnNativeWorkResultDeliveryContributor, JobsBpmnResultDeliveryContributor>());
    }
}

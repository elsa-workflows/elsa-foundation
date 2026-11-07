using CShells.Features;
using Elsa.Jobs.Workflows.Design.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Jobs.Workflows.Design;

/// <summary>Freezes the logical Jobs selections used by a published native workflow.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Jobs")]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ShellFeature(
    name: "JobsWorkflowsDesign",
    DisplayName = "Jobs Workflows (Design)",
    Description = "Pins literal handler, schema and policy selections when Jobs activities are published.",
    DependsOn = new object[] { "WorkflowsPublishing" })]
public class JobsWorkflowsDesignFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExecutableCompilationSource, JobsHandlerPinSource>());
    }
}

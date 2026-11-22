using CShells.Features;
using Elsa.Rules.Workflows.Design.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Rules.Workflows.Design;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Rules")]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ShellFeature(
    name: "RulesWorkflowsDesign",
    DisplayName = "Rules Workflows (Design)",
    Description = "Compiles and pins literal finite Rules programs when workflows are published.",
    DependsOn = new object[] { "RulesRuntime", "WorkflowsPublishing" })]
public class RulesWorkflowsDesignFeature : IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExecutableCompilationSource, RulesPublishedProgramPinSource>());
    }
}

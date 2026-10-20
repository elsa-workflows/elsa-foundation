using CShells.Features;
using Elsa.Etl.Workflows.Design.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Publishing.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Etl.Workflows.Design;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("ETL")]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Design")]
[ShellFeature(
    name: "EtlWorkflowsDesign",
    DisplayName = "ETL Workflows (Design)",
    Description = "Pins exact published ETL artifacts into native RunEtl workflow nodes.",
    DependsOn = new object[] { "EtlDesign", "WorkflowsPublishing" })]
public sealed class EtlWorkflowsDesignFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExecutableCompilationSource, EtlPublishedArtifactPinSource>());
    }
}

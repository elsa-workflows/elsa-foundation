using CShells.Features;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Activities.Design.Reconciliation.Clr.Options;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Samples.Nuplane.Renewals.Activities;

/// <summary>Catalogs the structural activities from their actual package-loaded assembly folders.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ShellFeature(
    name: "FoundationDemoDesignerActivities",
    DisplayName = "Demo designer activities",
    Description = "Reconciles Sequence and Flowchart from their Nuplane-installed assemblies for this local Studio demo.",
    DependsOn = new object[] { "ActivitiesSequence", "ActivitiesFlowchart", "ClrActivityReconciliation", "ActivitiesDesignReconciliation" })]
public sealed class FoundationDemoDesignerActivitiesFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
        var assemblies = new[]
        {
            typeof(Elsa.Activities.Sequence.Activities.Sequence).Assembly,
            typeof(Elsa.Activities.Flowchart.Activities.Flowchart).Assembly
        };
        foreach (var assembly in assemblies)
        {
            var folder = Path.GetDirectoryName(assembly.Location)
                ?? throw new InvalidOperationException("The demo activity assembly has no package folder.");
            var source = new ClrReconciliationOptions
            {
                FolderPath = folder,
                SourceId = "foundation-demo-designer:" + assembly.GetName().Name
            };
            services.AddScoped<IActivityReconciliationSource>(provider => new ClrActivityReconciliationSource(
                provider.GetRequiredService<IClrAssemblyScanner>(), Options.Create(source)));
        }
    }
}

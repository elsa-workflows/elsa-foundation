using CShells.Features;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Renewals.Activities;

/// <summary>Contributes the <see cref="RegisterRenewal"/> activity to the workflow designer's catalog and runtime.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ManifestFeatureCategory("Activities")]
[ShellFeature(
    name: "RenewalsActivities",
    DisplayName = "Renewals activities",
    Description = "The \"Register renewal\" workflow activity, which writes a renewal through the Renewals module.",
    DependsOn = new object[] { RenewalsActivitiesRelease.RequiredFeature })]
public sealed class RenewalsActivitiesFeature : IShellFeature
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IActivityReconciliationSource, RenewalsActivityReconciliationSource>();
}

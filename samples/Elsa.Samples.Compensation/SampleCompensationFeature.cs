using CShells.Features;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Samples.Compensation.Ledger;
using Elsa.Samples.Compensation.Reconciliation;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Compensation;

/// <summary>
/// The compensation sample: the <c>Reserve</c> and <c>Release</c> activities and the file-backed participant they call.
/// Its workflows (<c>workflows/</c>) need the compensation scope, so it depends on <c>ActivitiesCompensation</c>.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ManifestFeatureCategory("Activities")]
[ShellFeature(
    name: Name,
    DependsOn = new object[] { "ActivitiesCompensation" },
    DisplayName = "Sample Compensation",
    Description = "Sample reservation participant and activities for compensation scopes."
)]
public sealed class SampleCompensationFeature : IShellFeature
{
    public const string Name = "SampleCompensation";
    public const string DefaultDirectory = "compensation-sample";

    [ManifestSetting(
        DisplayName = "Ledger directory",
        Description = "Where the participant keeps one JSON file per resource. A relative path is resolved against the host's working directory.",
        Category = "General",
        DefaultValue = DefaultDirectory)]
    public string Directory { get; set; } = DefaultDirectory;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IActivityReconciliationSource, SampleCompensationActivityCatalog>();
        services.AddSingleton(new ReservationLedger(string.IsNullOrWhiteSpace(Directory) ? DefaultDirectory : Directory));
    }
}

using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Samples.Nuplane.Activities.Activities;
using Elsa.Samples.Nuplane.Activities.Reconciliation;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Samples.Nuplane.Activities;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Samples")]
[ManifestFeatureCategory("Activities")]
[ShellFeature(
    name: "SampleNuplaneActivities",
    DisplayName = "Sample Nuplane Activities",
    Description = "Sample package-loaded activity feature for Nuplane demonstrations."
)]
public sealed class SampleNuplaneActivitiesFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Message template",
        Description = "Console message template. Use {recipient} for the activity input value.",
        Category = "General",
        DefaultValue = "Hello {recipient} from a Nuplane-loaded activity.")]
    public string MessageTemplate { get; set; } = "Hello {recipient} from a Nuplane-loaded activity.";

    [ManifestSetting(
        DisplayName = "Include timestamp", 
        Description = "Prefixes the console message with the current local time.", 
        Category = "General",
        DefaultValue = "false")]
    public bool IncludeTimestamp { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IActivityReconciliationSource, SampleNuplaneActivityReconciliationSource>();
        services.AddSingleton(new SampleNuplaneActivityOptions(MessageTemplate, IncludeTimestamp));
    }
}

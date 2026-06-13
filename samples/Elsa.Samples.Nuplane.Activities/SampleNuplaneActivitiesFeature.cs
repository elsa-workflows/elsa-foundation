using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Samples.Nuplane.Activities.Constructors;
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
    [ManifestSetting(DisplayName = "Message template", Description = "Console message template. Use {recipient} for the activity input value.", Category = "Output", DefaultValue = "Hello {recipient} from a Nuplane-loaded activity.", RestartRequired = true)]
    public string MessageTemplate { get; set; } = "Hello {recipient} from a Nuplane-loaded activity.";

    [ManifestSetting(DisplayName = "Include timestamp", Description = "Prefixes the console message with the current local time.", Category = "Output", DefaultValue = "false", RestartRequired = true)]
    public bool IncludeTimestamp { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IActivityReconciliationSource, SampleNuplaneActivityReconciliationSource>();
        services.AddSingleton<IActivityConstructor>(new SampleNuplaneActivityConstructor(MessageTemplate, IncludeTimestamp));
    }
}

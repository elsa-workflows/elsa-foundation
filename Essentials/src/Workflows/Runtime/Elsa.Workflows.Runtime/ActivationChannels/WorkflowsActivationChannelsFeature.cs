using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.ActivationChannels;

/// <summary>Optional shell feature that declares the activation channels accepted by the workflow runtime.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(
    name: "WorkflowsActivationChannels",
    DisplayName = "Workflow Activation Channels",
    Description = "Declares shell-wide activation channel names and whether undeclared channel names are accepted.")]
public class WorkflowsActivationChannelsFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Allow ad hoc channels",
        Description = "Allow workflow activations to use channel names that are not listed below. Defaults to true for compatibility with existing runtime compositions.",
        Category = "Runtime",
        DefaultValue = "true")]
    public bool AllowAdHocChannels { get; set; } = true;

    [ManifestSetting(
        DisplayName = "Channels",
        Description = "Shell-wide channel declarations. Each entry has a name, display name, and optional description. Names must match activation slot names exactly using ordinal, case-sensitive comparison; values are not normalized.",
        Category = "Runtime")]
    public List<ActivationChannelDefinition> Channels { get; set; } = [];

    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddActivationChannelOptions();
        services.Configure<ActivationChannelOptions>(options =>
        {
            options.AllowAdHocChannels = AllowAdHocChannels;
            options.Channels = Channels;
        });
    }
}

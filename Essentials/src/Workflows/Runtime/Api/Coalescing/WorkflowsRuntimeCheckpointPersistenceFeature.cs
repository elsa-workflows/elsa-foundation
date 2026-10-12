using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Api.Coalescing;

/// <summary>
/// Selects the shell's default persistence mode after all runtime persistence providers have registered their
/// stores. Durable commits every checkpoint; Balanced folds checkpoints between commits and accepts that the
/// activities since the last commit run again after a crash.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeCheckpointPersistence",
    DisplayName = "Workflows Runtime Checkpoint Persistence",
    Description = "Selects the default persistence mode (Durable or Balanced) after the shell's runtime provider is configured.",
    DependsOn = new object[] { "WorkflowsRuntimeApi" })]
public sealed class WorkflowsRuntimeCheckpointPersistenceFeature : IShellFeature, IPostConfigureShellServices
{
    [ManifestSetting(
        DisplayName = "Persistence mode",
        Description = "Durable commits every checkpoint; Balanced folds checkpoints and commits at suspension, fault, completion, the end of each execution pass and after a bounded amount of work.",
        Category = "Runtime",
        DefaultValue = "Durable")]
    public PersistenceMode Mode { get; set; } = PersistenceMode.Durable;

    [ManifestSetting(
        DisplayName = "Maximum segment checkpoints",
        Description = "Maximum checkpoints Balanced folds before an intermediate commit. Must be greater than zero.",
        Category = "Runtime",
        DefaultValue = "50")]
    public int MaxSegmentCheckpoints { get; set; } = 50;

    [ManifestSetting(
        DisplayName = "Coalesce durable-value reads",
        Description = "Reuse equivalent durable-value pages within an eligible Balanced session. Disable to issue each logical page read to the durable store.",
        Category = "Runtime",
        DefaultValue = "true")]
    public bool CoalesceDurableValueReads { get; set; } = true;

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
    }

    public void PostConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!Enum.IsDefined(Mode))
        {
            throw new InvalidOperationException(
                $"Feature 'WorkflowsRuntimeCheckpointPersistence' received unsupported persistence mode '{(int)Mode}'.");
        }

        if (Mode == PersistenceMode.Durable)
            return;

        if (MaxSegmentCheckpoints <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxSegmentCheckpoints)} must be greater than zero when the persistence mode is Balanced.");
        }

        services.AddPersistenceModes(options =>
        {
            options.Mode = Mode;
            options.MaxSegmentCheckpoints = MaxSegmentCheckpoints;
            options.CoalesceDurableValueReads = CoalesceDurableValueReads;
        });
    }
}

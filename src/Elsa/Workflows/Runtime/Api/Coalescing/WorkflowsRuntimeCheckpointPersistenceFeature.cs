using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Api.Coalescing;

/// <summary>
/// Selects the shell's checkpoint persistence policy after all runtime persistence providers have registered their
/// stores. Immediate mode preserves the narrowest replay window; coalesced mode trades a bounded replay window for
/// fewer durable writes within non-suspending drain segments.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeCheckpointPersistence",
    DisplayName = "Workflows Runtime Checkpoint Persistence",
    Description = "Selects immediate or bounded coalesced checkpoint persistence after the shell's runtime provider is configured.",
    DependsOn = new object[] { "WorkflowsRuntimeApi" })]
public sealed class WorkflowsRuntimeCheckpointPersistenceFeature : IShellFeature, IPostConfigureShellServices
{
    [ManifestSetting(
        DisplayName = "Checkpoint persistence mode",
        Description = "Immediate persists each checkpoint; Coalesced folds replayable checkpoints until a durable boundary.",
        Category = "Runtime",
        DefaultValue = "Immediate")]
    public CheckpointPersistenceMode Mode { get; set; } = CheckpointPersistenceMode.Immediate;

    [ManifestSetting(
        DisplayName = "Maximum segment checkpoints",
        Description = "Maximum replayable checkpoints buffered by Coalesced mode before an intermediate flush. Must be greater than zero.",
        Category = "Runtime",
        DefaultValue = "50")]
    public int MaxSegmentCheckpoints { get; set; } = 50;

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
                $"Feature 'WorkflowsRuntimeCheckpointPersistence' received unsupported checkpoint persistence mode '{(int)Mode}'.");
        }

        if (Mode == CheckpointPersistenceMode.Immediate)
            return;

        if (MaxSegmentCheckpoints <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxSegmentCheckpoints)} must be greater than zero when checkpoint persistence mode is Coalesced.");
        }

        services.AddCoalescingRuntimeCheckpointPersistence(options =>
            options.MaxSegmentCheckpoints = MaxSegmentCheckpoints);
    }
}

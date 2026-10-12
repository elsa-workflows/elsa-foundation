using CShells.Features;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Api.Coalescing;

/// <summary>
/// The shell's persistence-mode settings. Durable commits once per activity; Balanced folds checkpoints between commits
/// and accepts that the activities since the last commit run again after a crash; Ephemeral commits when a run
/// suspends or faults, before an activity that is not safe to repeat, at the end of a pass that does not complete the
/// run and at completion, and can skip the completion record of a run that was never committed. Every mode commits
/// the claim before an activity that is not safe to repeat. The services that run each mode come with the runtime API
/// itself, so a shell that does not list this feature still runs Balanced with these defaults; list it to choose a
/// different default or cap.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Workflows")]
[ManifestFeatureCategory("Runtime")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(
    name: "WorkflowsRuntimeCheckpointPersistence",
    DisplayName = "Workflows Runtime Checkpoint Persistence",
    Description = "Sets the default persistence mode (Durable, Balanced or Ephemeral), how much work Balanced folds into one commit, and whether Ephemeral records completions.",
    DependsOn = new object[] { "WorkflowsRuntimeApi" })]
public class WorkflowsRuntimeCheckpointPersistenceFeature : IShellFeature
{
    [ManifestSetting(
        DisplayName = "Persistence mode",
        Description = "The mode a run uses when its workflow does not choose one. Durable commits once per activity; Balanced folds checkpoints and commits at suspension, fault, completion, the end of each execution pass and after a bounded amount of work; Ephemeral commits at suspension and fault, at the end of a pass that does not complete the run, and at completion. Every mode commits before an activity that is not safe to repeat.",
        Category = "Runtime",
        DefaultValue = "Balanced")]
    public PersistenceMode Mode { get; set; } = PersistenceMode.Balanced;

    [ManifestSetting(
        DisplayName = "Maximum activities per commit",
        Description = "The most activities Balanced folds into one commit before an intermediate commit. Must be greater than zero.",
        Category = "Runtime",
        DefaultValue = "50")]
    public int MaxActivitiesPerCommit { get; set; } = 50;

    [ManifestSetting(
        DisplayName = "Record completion",
        Description = "Under Ephemeral, whether a run that completes without ever having been committed writes a completion record. Off leaves no trace of such a run, which only a run of activities that are all safe to repeat can be; a run that already has a record always gets its completion committed.",
        Category = "Runtime",
        DefaultValue = "true")]
    public bool RecordCompletion { get; set; } = true;

    [ManifestSetting(
        DisplayName = "Coalesce durable-value reads",
        Description = "Reuse equivalent durable-value pages within an eligible drain session, in any mode. Disable to issue each logical page read to the durable store.",
        Category = "Runtime",
        DefaultValue = "true")]
    public bool CoalesceDurableValueReads { get; set; } = true;

    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Only the options are registered here, and registering them refuses a bad setting while the shell is composed.
        // A workflow can choose Balanced on a Durable host, so the cap is checked whatever the default mode is. The
        // runtime API feature wraps the stores after every feature has registered its own, and keeps these options
        // because they are already present.
        services.AddPersistenceModeOptions(new PersistenceModeOptions
        {
            Mode = Mode,
            MaxActivitiesPerCommit = MaxActivitiesPerCommit,
            RecordCompletion = RecordCompletion,
            CoalesceDurableValueReads = CoalesceDurableValueReads
        });
    }
}

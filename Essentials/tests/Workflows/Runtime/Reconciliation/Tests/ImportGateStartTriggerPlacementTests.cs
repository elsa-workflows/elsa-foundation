using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// Spec 238, User Story 4 scenario 7: an imported artifact whose start trigger sits where no run could start at it is
/// refused, through the same trigger preflight a publish uses, and is never activated. The composite here has no
/// structure, so no start-entry strategy can answer for it.
/// </summary>
public sealed class ImportGateStartTriggerPlacementTests : IDisposable
{
    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-placement",
        Guid.NewGuid().ToString("N"));

    public ImportGateStartTriggerPlacementTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task A_misplaced_start_trigger_with_carried_bindings_is_rejected_before_persistence()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(MisplacedTriggerRoot(), "definition-misplaced");
        ArtifactClosureFixture.Mount(
            harness.Services,
            _mount,
            "misplaced.json",
            ArtifactClosureFixture.ClosureWithCarriedBindings(executable, "node-trigger", ArtifactClosureFixture.TriggerStimulusHash("node-trigger")));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactRejectionKind.TriggerSurfaceMismatch, entry.RejectionKind);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-misplaced"));
    }

    [Fact]
    public async Task A_misplaced_start_trigger_without_carried_bindings_is_never_activated()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(MisplacedTriggerRoot(), "definition-misplaced-unasserted");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "misplaced.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.NotEqual(WorkflowArtifactImportOutcome.Imported, entry.Outcome);
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-misplaced-unasserted"));
        Assert.Empty(await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-trigger")));
    }

    /// <summary>A structure-less probe composite whose only child is a start trigger.</summary>
    private static ExecutableNode MisplacedTriggerRoot()
    {
        var trigger = ArtifactClosureFixture.AsStartTrigger(ArtifactClosureFixture.ProbeNode("node-trigger"));
        var parent = ArtifactClosureFixture.ProbeNode("node-root");
        return new ExecutableNode(
            executableNodeId: parent.ExecutableNodeId,
            authoredActivityId: parent.AuthoredActivityId,
            activityType: parent.ActivityType,
            activityTypeVersion: parent.ActivityTypeVersion,
            descriptor: parent.Descriptor,
            inputBindings: parent.InputBindings,
            metadata: parent.Metadata,
            childSlots: [new ExecutableChildSlot("Body", [trigger])],
            activityContract: parent.ActivityContract);
    }
}

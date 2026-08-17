using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// US4's latest-wins rules, end to end on a real engine: a newer artifact supersedes the serving one, an older one
/// never moves activation backward, an unorderable version is refused at the door, and one logical version claimed
/// with two different payloads is named as the broken source it is.
/// </summary>
/// <remarks>
/// <para>
/// Every version pair here also differs in <em>content</em>, because it has to: an artifact id is the hash of the
/// payload, and <c>ArtifactVersion</c> is not a hash input. Two "versions" of an identical workflow are literally
/// the same artifact, which is the idempotent no-op path, not a supersession. Changing the node id is the smallest
/// honest way to say "this is a different build".
/// </para>
/// <para>
/// The mount is rewritten between passes rather than accumulating files, so each pass asks exactly one question of
/// the engine — the same way a redeploy replaces a ConfigMap rather than appending to it.
/// </para>
/// </remarks>
public sealed class ArtifactVersionSupersessionTests : IDisposable
{
    private const string DefinitionId = "definition-invoice";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-supersession",
        Guid.NewGuid().ToString("N"));

    public ArtifactVersionSupersessionTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task A_newer_version_supersedes_the_active_one_and_retires_its_reference_as_activation_replaced()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var v1 = TriggerExecutable("node-v1", "1.0.0");
        var v2 = TriggerExecutable("node-v2", "1.1.0");

        Mount(harness, v1);
        var firstEntry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, firstEntry.Outcome);

        Mount(harness, v2);
        var secondEntry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Imported, secondEntry.Outcome);
        Assert.Equal(v2.Identity.ArtifactId, secondEntry.ArtifactId);

        // The slot moved to v2.
        var slot = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.Equal(secondEntry.ActivationId, slot!.ActiveActivationId);

        // v1's reference is retired with the pinned reason (P10), and the pinned literal is what the coordinator
        // actually writes — asserted as a string so a rename of the constant cannot hide a wire change.
        var retired = await ArtifactImportHarness.FindReferenceAsync(harness, firstEntry.ActivationId!);
        Assert.NotNull(retired);
        Assert.NotNull(retired!.DeletedAt);
        Assert.Equal("activation-replaced", retired.DeletedReason);
        Assert.Equal(WorkflowActivationCoordinator.ReplacedRetireReason, retired.DeletedReason);

        // v2's reference is live and carries the new version — this IS the state latest-wins reads next pass.
        var live = await ArtifactImportHarness.FindReferenceAsync(harness, secondEntry.ActivationId!);
        Assert.NotNull(live);
        Assert.Null(live!.DeletedAt);
        Assert.Equal("1.1.0", live.ArtifactVersion);

        // And the projection followed: v1's stimulus no longer serves, v2's does.
        Assert.Empty(await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-v1")));
        var serving = Assert.Single(await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-v2")));
        Assert.True(serving.IsActive);
        Assert.Equal(secondEntry.ActivationId, serving.ActivationId);
    }

    [Fact]
    public async Task A_v1_candidate_arriving_after_v2_is_skipped_and_activation_does_not_move_backward()
    {
        // The rollback-by-accident case: a stale mount, an out-of-order rollout, a replayed ConfigMap. Activation
        // must stay on the newer artifact.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var v1 = TriggerExecutable("node-v1", "1.0.0");
        var v2 = TriggerExecutable("node-v2", "2.0.0");

        Mount(harness, v2);
        var newer = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, newer.Outcome);
        var revisionAfterV2 = (await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId))!.Revision;

        Mount(harness, v1);
        var older = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Skipped, older.Outcome);
        Assert.Equal(WorkflowArtifactSkipReason.OlderVersion, older.SkipReason);
        Assert.Contains("2.0.0", older.Diagnostic);
        Assert.Contains("never moves backward", older.Diagnostic);

        // Nothing moved: same activation, same revision, v2's reference still live.
        var slot = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.Equal(newer.ActivationId, slot!.ActiveActivationId);
        Assert.Equal(revisionAfterV2, slot.Revision);
        Assert.Null((await ArtifactImportHarness.FindReferenceAsync(harness, newer.ActivationId!))!.DeletedAt);

        // v1 never became serving, and never got a reference of its own.
        Assert.Empty(await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-v1")));
        Assert.Single(await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-v2")));
        Assert.Single(await ArtifactImportHarness.ListAllReferencesAsync(harness));
    }

    [Fact]
    public async Task A_prerelease_does_not_supersede_its_own_release()
    {
        // Proves the comparator is SemVer precedence and not a string compare: ordinally "1.1.0-rc.1" sorts ABOVE
        // "1.1.0", so a naive implementation would activate the release candidate over the release.
        await using var harness = ArtifactImportHarness.Build(_mount);
        Mount(harness, TriggerExecutable("node-release", "1.1.0"));
        var release = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, release.Outcome);

        Mount(harness, TriggerExecutable("node-rc", "1.1.0-rc.1"));
        var candidate = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Skipped, candidate.Outcome);
        Assert.Equal(WorkflowArtifactSkipReason.OlderVersion, candidate.SkipReason);
        Assert.Equal(release.ActivationId, (await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId))!.ActiveActivationId);
    }

    [Fact]
    public async Task An_artifact_version_that_is_not_a_semantic_version_is_rejected()
    {
        // Latest-wins needs orderability. An unorderable version could not be compared against whatever is serving,
        // so it is unimportable rather than admitted and activated blind.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = TriggerExecutable("node-root", "v1-latest");
        Mount(harness, executable);

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.UnorderableVersion, entry.RejectionKind);
        Assert.Contains("'v1-latest'", entry.Diagnostic);
        Assert.Contains("not a semantic version", entry.Diagnostic);

        // Refused before any write, like every other gate.
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId));
    }

    [Fact]
    public async Task An_unorderable_version_anywhere_in_a_closure_rejects_the_whole_unit()
    {
        // Closure-unit isolation (P6): the root is perfectly orderable, but its child is not.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var child = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-child"), "definition-child", "not-semver");
        var parent = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-parent"),
            "definition-parent",
            "1.0.0",
            ArtifactClosureFixture.DependencyOn(child, "node-parent"));
        ArtifactClosureFixture.Mount(harness.Services, _mount, "closure.json", ArtifactClosureFixture.Closure(parent, child));

        var result = await ArtifactImportHarness.ReconcileAsync(harness);

        Assert.Equal(2, result.RejectedCount);
        Assert.All(result.Entries, entry => Assert.Equal(WorkflowArtifactRejectionKind.UnorderableVersion, entry.RejectionKind));
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, parent.Identity.ArtifactId));
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, child.Identity.ArtifactId));
    }

    [Fact]
    public async Task The_same_logical_version_claimed_with_different_content_is_rejected_as_a_broken_source()
    {
        // Same (DefinitionId, ArtifactVersion), two different content-addressed payloads. Not a supersession and
        // not a no-op — a source-side bug (content edited without bumping the version).
        await using var harness = ArtifactImportHarness.Build(_mount);
        var first = TriggerExecutable("node-original", "1.0.0");
        var second = TriggerExecutable("node-edited", "1.0.0");

        Mount(harness, first);
        var imported = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, imported.Outcome);

        Mount(harness, second);
        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.VersionHashMismatch, entry.RejectionKind);
        Assert.Contains(first.Identity.ArtifactId, entry.Diagnostic);
        Assert.Contains(second.Identity.ArtifactId, entry.Diagnostic);
        Assert.Contains("The source is broken", entry.Diagnostic);

        // The serving activation is untouched — a broken source never displaces working state.
        var slot = await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId);
        Assert.Equal(imported.ActivationId, slot!.ActiveActivationId);
        Assert.Single(await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-original")));
    }

    [Fact]
    public async Task Build_metadata_does_not_make_a_second_claim_on_a_version_legitimate()
    {
        // SemVer ignores build metadata for precedence, so 1.0.0+build.1 and 1.0.0+build.2 are the SAME logical
        // version. Two different payloads under them is the same broken source, dressed up.
        await using var harness = ArtifactImportHarness.Build(_mount);
        Mount(harness, TriggerExecutable("node-build-1", "1.0.0+build.1"));
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries).Outcome);

        Mount(harness, TriggerExecutable("node-build-2", "1.0.0+build.2"));
        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.VersionHashMismatch, entry.RejectionKind);
    }

    [Fact]
    public async Task One_closure_claiming_one_logical_version_twice_with_different_content_is_rejected()
    {
        // The envelope-internal form of the same breakage: the planner's duplicate check keys on artifact id, which
        // these two deliberately do not share.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var first = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-a"), DefinitionId, "3.0.0");
        var second = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-b"), DefinitionId, "3.0.0");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "closure.json", ArtifactClosureFixture.Closure(first, second));

        var result = await ArtifactImportHarness.ReconcileAsync(harness);

        Assert.Equal(2, result.RejectedCount);
        Assert.All(result.Entries, entry => Assert.Equal(WorkflowArtifactRejectionKind.VersionHashMismatch, entry.RejectionKind));
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, first.Identity.ArtifactId));
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, second.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, DefinitionId));
    }

    /// <summary>A trigger-bearing executable, so every assertion can also see the projection follow the slot.</summary>
    private static WorkflowExecutable TriggerExecutable(string nodeId, string artifactVersion) =>
        ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.AsStartTrigger(ArtifactClosureFixture.ProbeNode(nodeId)),
            DefinitionId,
            artifactVersion);

    /// <summary>Replaces the mount's contents, the way a redeploy replaces a mounted config rather than appending.</summary>
    private void Mount(WorkflowExecutionHarness harness, WorkflowExecutable executable)
    {
        foreach (var stale in Directory.GetFiles(_mount, "*.json"))
            File.Delete(stale);

        ArtifactClosureFixture.Mount(harness.Services, _mount, "invoice.json", ArtifactClosureFixture.Closure(executable));
    }
}

using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// US2 scenario 3 and pin P6: the isolation unit is the <b>closure</b>, and isolation across the mounted set is
/// <b>per unit</b>.
/// </summary>
/// <remarks>
/// Two claims, and they pull in opposite directions, which is why both need proving. Outward: one unsatisfiable
/// unit must not take the batch down — a single bad file in a mount folder cannot be allowed to stop a deploy.
/// Inward: a unit is all-or-nothing — if any member fails a gate, <em>no</em> member is written, so there is never
/// a half-imported closure whose parent is live and whose child is absent.
/// </remarks>
public sealed class MixedBatchClosureIsolationTests : IDisposable
{
    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-batch",
        Guid.NewGuid().ToString("N"));

    public MixedBatchClosureIsolationTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task Satisfiable_units_activate_while_unsatisfiable_ones_are_rejected_individually()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);

        var good = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-good"), "definition-good");
        var unsatisfiable = ArtifactClosureFixture.ExecutableRequiring(
            ArtifactClosureFixture.ProbeNode("node-bad"),
            "definition-bad",
            storageDriverRequirements: [new RuntimeStorageDriverRequirement("acme.cold-storage")]);

        // Mounted with ordinally-sorted names so the rejected unit is read *between* two good ones: a pass that
        // aborted on the first rejection would leave the third unimported and this test would catch it.
        ArtifactClosureFixture.Mount(harness.Services, _mount, "a-good.json", ArtifactClosureFixture.Closure(good));
        ArtifactClosureFixture.Mount(harness.Services, _mount, "b-bad.json", ArtifactClosureFixture.Closure(unsatisfiable));
        var alsoGood = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-third"), "definition-third");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "c-good.json", ArtifactClosureFixture.Closure(alsoGood));

        var result = await ArtifactImportHarness.ReconcileAsync(harness);

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(1, result.RejectedCount);

        var rejected = Assert.Single(result.Rejections);
        Assert.Equal(unsatisfiable.Identity.ArtifactId, rejected.ArtifactId);
        Assert.Equal(WorkflowArtifactRejectionKind.UnmetRequirement, rejected.RejectionKind);

        Assert.True(await ArtifactImportHarness.IsInStoreAsync(harness, good.Identity.ArtifactId));
        Assert.True(await ArtifactImportHarness.IsInStoreAsync(harness, alsoGood.Identity.ArtifactId));
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, unsatisfiable.Identity.ArtifactId));

        Assert.NotNull(await ArtifactImportHarness.FindSlotAsync(harness, "definition-good"));
        Assert.NotNull(await ArtifactImportHarness.FindSlotAsync(harness, "definition-third"));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-bad"));
    }

    [Fact]
    public async Task A_unit_whose_dependency_fails_a_gate_writes_nothing_at_all()
    {
        // P6's sharpest edge. The *parent* is perfectly satisfiable; only its child needs an activity package this
        // runtime lacks. Persisting the parent anyway would leave a content-addressed row nothing references, and
        // activating it would produce a workflow that dispatches into nothing.
        await using var harness = ArtifactImportHarness.Build(_mount);

        var child = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.UnregisteredClrNode("node-child", "Acme.Warehouse.PickAndPack"),
            "definition-child");
        var parent = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-parent"),
            "definition-parent",
            "1.0.0",
            ArtifactClosureFixture.DependencyOn(child, "node-parent"));
        ArtifactClosureFixture.Mount(harness.Services, _mount, "closure.json", ArtifactClosureFixture.Closure(parent, child));

        var result = await ArtifactImportHarness.ReconcileAsync(harness);

        // Every member of the unit is accounted for as rejected, not just the one that failed: none of them were
        // imported, and an operator reading the pass result has to see that.
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(2, result.RejectedCount);
        Assert.All(result.Rejections, entry =>
        {
            Assert.Equal(WorkflowArtifactRejectionKind.UnmetRequirement, entry.RejectionKind);
            Assert.Contains("Acme.Warehouse.PickAndPack", entry.Diagnostic);
        });
        Assert.Equal(
            new[] { child.Identity.ArtifactId, parent.Identity.ArtifactId }.Order(StringComparer.Ordinal).ToArray(),
            result.Rejections.Select(entry => entry.ArtifactId).Order(StringComparer.Ordinal).ToArray());

        // The store is untouched for *every* member.
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, child.Identity.ArtifactId));
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, parent.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-child"));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-parent"));
    }

    [Fact]
    public async Task A_failed_unit_does_not_stop_a_sibling_closure_unit_from_importing()
    {
        // The outward half of the same pin, with a multi-member failure rather than a single artifact: the whole
        // parent+child unit is refused and the unrelated unit beside it still goes live.
        await using var harness = ArtifactImportHarness.Build(_mount);

        var child = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.UnregisteredClrNode("node-child", "Acme.Warehouse.PickAndPack"),
            "definition-child");
        var parent = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-parent"),
            "definition-parent",
            "1.0.0",
            ArtifactClosureFixture.DependencyOn(child, "node-parent"));
        var unrelated = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-solo"), "definition-solo");

        ArtifactClosureFixture.Mount(harness.Services, _mount, "a-broken.json", ArtifactClosureFixture.Closure(parent, child));
        ArtifactClosureFixture.Mount(harness.Services, _mount, "b-solo.json", ArtifactClosureFixture.Closure(unrelated));

        var result = await ArtifactImportHarness.ReconcileAsync(harness);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(2, result.RejectedCount);
        Assert.True(await ArtifactImportHarness.IsInStoreAsync(harness, unrelated.Identity.ArtifactId));
        Assert.NotNull(await ArtifactImportHarness.FindSlotAsync(harness, "definition-solo"));
    }
}

using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// SC-B-004: reconciling an unchanged mounted set N times leaves exactly one active version per definition, with no
/// duplicate records and no corruption.
/// </summary>
/// <remarks>
/// <para>
/// <b>The load-bearing assertion is the cross-pass one.</b> Every per-pass check here would also pass on an
/// importer that minted a fresh artifact or activation identity on every run — it would simply report N imports of
/// N "different" artifacts, each internally consistent. Only comparing pass <em>k</em>'s ids against pass 1's
/// catches that, which is exactly the spec's "artifact id not pinned" edge case: identity must come from content
/// and configuration, never from the clock or a counter.
/// </para>
/// <para>
/// N is 5 rather than 2 because the failure mode this guards against is accumulation, and an off-by-one in a
/// replace-vs-append is invisible at 2.
/// </para>
/// </remarks>
public sealed class RepeatedReconcileStabilityTests : IDisposable
{
    private const int Passes = 5;

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-repeat",
        Guid.NewGuid().ToString("N"));

    public RepeatedReconcileStabilityTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task Repeated_passes_over_an_unchanged_mount_keep_one_active_version_and_identical_identities()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);

        var child = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-child"), "definition-child");
        var parent = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.AsStartTrigger(ArtifactClosureFixture.ProbeNode("node-parent")),
            "definition-parent",
            "1.0.0",
            ArtifactClosureFixture.DependencyOn(child, "node-parent"));
        ArtifactClosureFixture.Mount(harness.Services, _mount, "closure.json", ArtifactClosureFixture.Closure(parent, child));

        var passes = new List<WorkflowArtifactReconciliationResult>();
        var revisionsAfterEachPass = new List<long>();
        for (var index = 0; index < Passes; index++)
        {
            passes.Add(await ArtifactImportHarness.ReconcileAsync(harness));
            revisionsAfterEachPass.Add((await ArtifactImportHarness.FindSlotAsync(harness, "definition-parent"))!.Revision);
        }

        // The authority moved exactly once, on the first pass, and never again.
        Assert.All(revisionsAfterEachPass, revision => Assert.Equal(revisionsAfterEachPass[0], revision));

        // Pass 1 imports; every later pass recognizes the same artifacts as already current and writes nothing.
        Assert.Equal(2, passes[0].ImportedCount);
        foreach (var pass in passes.Skip(1))
        {
            Assert.Equal(0, pass.ImportedCount);
            Assert.Equal(2, pass.AlreadyCurrentCount);
            Assert.Equal(0, pass.SkippedCount);
            Assert.Equal(0, pass.RejectedCount);
        }

        // Byte-identical identities across every pass — the assertion a per-pass check cannot make.
        var baselineArtifactIds = passes[0].Entries.Select(entry => entry.ArtifactId).ToArray();
        var baselineActivationIds = passes[0].Entries.Select(entry => entry.ActivationId).ToArray();
        Assert.Equal([child.Identity.ArtifactId, parent.Identity.ArtifactId], baselineArtifactIds);
        foreach (var pass in passes)
        {
            Assert.Equal(baselineArtifactIds, pass.Entries.Select(entry => entry.ArtifactId).ToArray(), StringComparer.Ordinal);
            Assert.Equal(baselineActivationIds, pass.Entries.Select(entry => entry.ActivationId).ToArray()!, StringComparer.Ordinal);
        }

        // Exactly one active version per definition, and it is the one the first pass settled on.
        foreach (var (definitionId, activationId) in new[]
                 {
                     ("definition-child", baselineActivationIds[0]),
                     ("definition-parent", baselineActivationIds[1]),
                 })
        {
            var slot = await ArtifactImportHarness.FindSlotAsync(harness, definitionId);
            Assert.NotNull(slot);
            Assert.Equal(activationId, slot!.ActiveActivationId);
        }

        // No duplicate records: one source reference per definition, both live, and one serving trigger binding.
        var references = await ArtifactImportHarness.ListAllReferencesAsync(harness);
        Assert.Equal(2, references.Count);
        Assert.All(references, reference => Assert.Null(reference.DeletedAt));
        Assert.Equal(
            baselineActivationIds.Order(StringComparer.Ordinal).ToArray(),
            references.Select(reference => reference.ActivationId).Order(StringComparer.Ordinal).ToArray()!,
            StringComparer.Ordinal);

        var serving = Assert.Single(
            await ArtifactImportHarness.ListServingBindingsAsync(harness, ArtifactClosureFixture.TriggerStimulusHash("node-parent")));
        Assert.True(serving.IsActive);
        Assert.Equal(baselineActivationIds[1], serving.ActivationId);

        // And no corruption: the stored payloads still hash to the ids the envelope declared.
        var store = harness.Services.GetRequiredService<IWorkflowExecutableStore>();
        foreach (var expected in new[] { child, parent })
        {
            var stored = await store.FindAsync(expected.Identity.ArtifactId);
            Assert.NotNull(stored);
            Assert.Equal(expected.Identity.ArtifactHash, stored!.Identity.ArtifactHash);
            Assert.Equal(expected.Identity.ArtifactVersion, stored.Identity.ArtifactVersion);
        }

        var page = await store.ListPageAsync(new RuntimeStorePageRequest(limit: 100));
        Assert.Equal(2, page.Items.Count);
    }
}

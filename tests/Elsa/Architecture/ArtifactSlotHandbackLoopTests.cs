using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// T125 — the operator loop the docs actually tell operators to use, asserted end to end: a mount claims a
/// definition, an explicit publish takes it, reconciliation never takes it back, and <b>unpublishing hands the
/// slot to the mount again</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists when every step is already covered.</b> The claim, the takeover, the skip and
/// <c>Deactivation_clears_ownership_so_the_slot_becomes_claimable_again</c> each have their own test, and all
/// four can pass while the loop is broken at a seam between them: an unpublish that retires the journal but
/// leaves the slot's <see cref="WorkflowActivationSource"/> stamped; a reconciler that stops offering a
/// definition it has already skipped; a re-claim that moves the slot without re-preparing the serving
/// projections. Piecewise coverage cannot see any of those, and this is the round trip
/// <c>Reconciliation/README.md</c> and <c>quickstart.md</c> both document as the way back.
/// </para>
/// <para>
/// <b>Every step is asserted against serving state, never against a return value.</b> What the pass reported and
/// what the handler returned are checked where they are the subject — the skip's diagnostic has to name the owner
/// — but "who serves this definition" is read from the activation slot and from
/// <c>IWorkflowTriggerBindingStore.ListAllByStimulusAsync</c>, the active-rows-only projection the stimulus router
/// resolves against, and confirmed by delivering a real stimulus through the production router. A loop that ends
/// with the right slot and a projection that never came back would be a broken hand-back reported as a working
/// one.
/// </para>
/// <para>
/// <b>The two artifacts are different in a way the engine can observe.</b> Each version's start trigger carries a
/// different authored event name, so "what is serving" is answerable by delivering a stimulus rather than by
/// comparing ids: at every step exactly one of the two names starts a workflow and the other routes nowhere.
/// </para>
/// </remarks>
public sealed class ArtifactSlotHandbackLoopTests : IDisposable
{
    private const string DefinitionId = "definition-orders";
    private const string MountedVersionId = "version-orders-1";
    private const string PublishedVersionId = "version-orders-2";
    private const string MountedEvent = "order-placed";
    private const string PublishedEvent = "order-amended";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-handback-loop",
        Guid.NewGuid().ToString("N"));

    public ArtifactSlotHandbackLoopTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task Import_claims_publish_takes_over_reconcile_skips_and_unpublish_hands_the_slot_back()
    {
        await using var engine = CombinedEngine.Create([Mounted(), Published()], _mount);
        var startedInstances = 0;

        // ---- 1. The mount claims the default slot, and serves ------------------------------------------------
        var mounted = await ExportFromAnotherEngineAsync(Mounted(), "orders-v1.json");
        var claimPass = await engine.ReconcileAsync();

        var claim = Single(claimPass, DefinitionId);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, claim.Outcome);
        var importOwned = await engine.FindSlotAsync(DefinitionId);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, importOwned!.Source!.Kind);
        Assert.Equal(CombinedEngine.MountSourceId, importOwned.Source.SourceId);
        await AssertServesAsync(engine, MountedEvent, mounted.RootArtifactId, claim.ActivationId!);
        await AssertStartsOneAsync(engine, MountedEvent, "claimed", ++startedInstances);

        // ---- 2. The explicit publish takes it over -----------------------------------------------------------
        var published = await engine.PublishAsync(PublishedVersionId);
        Assert.NotEqual(mounted.RootArtifactId, published.ArtifactId);

        var publishOwned = await engine.FindSlotAsync(DefinitionId);
        Assert.Equal(published.PublicationId, publishOwned!.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.PublishingKind, publishOwned.Source!.Kind);
        Assert.Single(await ListSlotsAsync(engine));
        await AssertServesAsync(engine, PublishedEvent, published.ArtifactId, published.PublicationId);
        // The displaced import stopped serving — a takeover that left both live would be the double activation
        // US5 forbids, and the loop would then "work" for reasons that have nothing to do with ownership.
        Assert.Empty(await engine.ListServingBindingsAsync(MountedEvent));
        await AssertStartsOneAsync(engine, PublishedEvent, "published", ++startedInstances);
        await AssertStartsNothingAsync(engine, MountedEvent, "displaced-import", startedInstances);

        // ---- 3. Reconciliation never takes it back -----------------------------------------------------------
        // This is what a shell reload replays, and it is the half of T118's asymmetry that makes the other half
        // safe: without it the reload silently reverts the operator's publish.
        var skipPass = await engine.ReconcileAsync();

        var skip = Single(skipPass, DefinitionId);
        Assert.Equal(WorkflowArtifactImportOutcome.Skipped, skip.Outcome);
        Assert.Equal(WorkflowArtifactSkipReason.ForeignSlotOwner, skip.SkipReason);
        Assert.Equal(0, skipPass.RejectedCount);
        // The diagnostic names the OWNER: an operator whose mount is being ignored has to be told who holds the
        // definition, because unpublishing is the only way back and nothing else on the engine says so.
        Assert.Contains(WorkflowActivationSource.Publishing.Describe(), skip.Diagnostic!, StringComparison.Ordinal);
        Assert.Contains(DefinitionId, skip.Diagnostic!, StringComparison.Ordinal);
        Assert.Same(skip, Assert.Single(skipPass.OwnershipSkips));

        var afterSkip = await engine.FindSlotAsync(DefinitionId);
        Assert.Equal(published.PublicationId, afterSkip!.ActiveActivationId);
        Assert.Equal(publishOwned.Revision, afterSkip.Revision);
        Assert.Equal(WorkflowActivationSource.PublishingKind, afterSkip.Source!.Kind);
        await AssertServesAsync(engine, PublishedEvent, published.ArtifactId, published.PublicationId);
        await AssertStartsOneAsync(engine, PublishedEvent, "after-skip", ++startedInstances);

        // ---- 4. Unpublishing clears ownership ----------------------------------------------------------------
        var released = await engine.UnpublishAsync(DefinitionId);

        Assert.Null(released.ActiveActivationId);
        // Ownership is CLEARED, not merely vacated. A slot with no activation but a lingering publishing Source
        // would refuse the mount's next pass, and the loop would dead-end with everything else looking correct —
        // which is exactly the seam a piecewise suite cannot see.
        Assert.Null(released.Source);
        var afterUnpublish = await engine.FindSlotAsync(DefinitionId);
        Assert.Null(afterUnpublish!.ActiveActivationId);
        Assert.Null(afterUnpublish.Source);
        // Nothing serves the definition in the gap: the publication's projections went with it.
        Assert.Empty(await engine.ListServingBindingsAsync(PublishedEvent));
        Assert.Empty(await engine.ListServingBindingsAsync(MountedEvent));
        await AssertStartsNothingAsync(engine, PublishedEvent, "after-unpublish", startedInstances);

        // ---- 5. The next reconcile re-claims, and serves again -----------------------------------------------
        // The mount is untouched throughout — this is the same file that was skipped in step 3, so what changed is
        // ownership and nothing else.
        var reclaimPass = await engine.ReconcileAsync();

        var reclaim = Single(reclaimPass, DefinitionId);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, reclaim.Outcome);
        Assert.Equal(mounted.RootArtifactId, reclaim.ArtifactId);
        Assert.Empty(reclaimPass.OwnershipSkips);
        Assert.Equal(0, reclaimPass.RejectedCount);

        var reclaimed = await engine.FindSlotAsync(DefinitionId);
        Assert.Equal(reclaim.ActivationId, reclaimed!.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, reclaimed.Source!.Kind);
        Assert.Equal(CombinedEngine.MountSourceId, reclaimed.Source.SourceId);
        Assert.Single(await ListSlotsAsync(engine));

        // The projections came back with it, and the published version's stimulus stays dead.
        await AssertServesAsync(engine, MountedEvent, mounted.RootArtifactId, reclaim.ActivationId!);
        Assert.Empty(await engine.ListServingBindingsAsync(PublishedEvent));
        await AssertStartsOneAsync(engine, MountedEvent, "reclaimed", ++startedInstances);
        await AssertStartsNothingAsync(engine, PublishedEvent, "retired-publication", startedInstances);
    }

    /// <summary>Exactly one serving binding, pointing at the expected artifact and activation.</summary>
    private static async Task AssertServesAsync(
        CombinedEngine engine,
        string eventName,
        string expectedArtifactId,
        string expectedActivationId)
    {
        var binding = Assert.Single(await engine.ListServingBindingsAsync(eventName));
        Assert.True(binding.IsActive);
        Assert.Equal(expectedArtifactId, binding.ArtifactId);
        Assert.Equal(expectedActivationId, binding.ActivationId);
    }

    /// <summary>
    /// A real stimulus starts exactly one workflow, and the engine ends up holding exactly
    /// <paramref name="expectedTotal"/> execution states.
    /// </summary>
    /// <remarks>
    /// The running total is what makes this more than a per-step check: a step that started a second instance
    /// alongside the first would satisfy <c>StartedCount == 1</c> for its own delivery and still be caught here.
    /// </remarks>
    private static async Task AssertStartsOneAsync(CombinedEngine engine, string eventName, string key, int expectedTotal)
    {
        var routing = await engine.DeliverEventAsync(eventName, $"stimulus-{key}");

        Assert.Equal(1, routing.StartedCount);
        Assert.Equal(expectedTotal, (await engine.ListExecutionsAsync()).Count);
    }

    /// <summary>The stimulus routes nowhere and the engine's execution count does not move.</summary>
    private static async Task AssertStartsNothingAsync(CombinedEngine engine, string eventName, string key, int expectedTotal)
    {
        var routing = await engine.DeliverEventAsync(eventName, $"stimulus-{key}");

        Assert.Equal(0, routing.StartedCount);
        Assert.Equal(expectedTotal, (await engine.ListExecutionsAsync()).Count);
    }

    private static async Task<IReadOnlyCollection<WorkflowActivationSlot>> ListSlotsAsync(CombinedEngine engine) =>
        await engine.Services.GetRequiredService<IWorkflowActivationAuthority>().ListByDefinitionAsync(DefinitionId);

    /// <summary>
    /// Compiles, publishes and exports one authored version on a throwaway engine, writing the closure bytes into
    /// the mount — the way a foreign artifact actually reaches a running host.
    /// </summary>
    private async Task<WorkflowArtifactClosure> ExportFromAnotherEngineAsync(
        WorkflowDefinitionVersion version,
        string fileName)
    {
        await using var builder = CombinedEngine.Create([version]);
        await builder.PublishAsync(version.Id);
        return await builder.ExportToAsync(version.Id, _mount, fileName);
    }

    private static WorkflowArtifactImportEntry Single(WorkflowArtifactReconciliationResult result, string definitionId) =>
        Assert.Single(result.Entries, entry => entry.DefinitionId == definitionId);

    /// <summary>
    /// Sorts <b>above</b> the published version, and that is load-bearing for step 3.
    /// </summary>
    /// <remarks>
    /// The importer's supersession gate runs <em>before</em> the activation request, so a mounted artifact that
    /// sorted below what is serving would be skipped as <c>OlderVersion</c> and never reach the ownership rule at
    /// all — the loop would look green while asserting nothing about ownership. Written the other way round, the
    /// first draft of this test did exactly that. Latest-wins would activate this artifact; only ownership does
    /// not.
    /// </remarks>
    private static WorkflowDefinitionVersion Mounted() =>
        CombinedEngine.EventWorkflow(DefinitionId, MountedVersionId, "3.0.0", "node-order-placed", MountedEvent);

    private static WorkflowDefinitionVersion Published() =>
        CombinedEngine.EventWorkflow(DefinitionId, PublishedVersionId, "2.0.0", "node-order-amended", PublishedEvent);
}

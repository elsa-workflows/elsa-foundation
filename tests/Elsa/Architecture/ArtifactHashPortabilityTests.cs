using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// ADR 0038's content-addressing invariant, read in the direction that was failing (T093a): <b>equal behaviour must
/// produce an equal hash</b>.
/// </summary>
/// <remarks>
/// <para>
/// The well-known direction — equal hash implies equal behaviour — was already guarded. The converse was not, and it
/// was broken: <c>DispatchPinSource</c> wrote the selected child's full source provenance into the parent node's
/// metadata, which is hash input, so the parent's artifact id depended on the publish-local identifiers the exporting
/// engine happened to mint. Two engines compiling byte-identical authored content therefore disagreed on the parent's
/// identity for a reason with no behavioural meaning. That weakens deduplication and contradicts FR-B-010's
/// portability claim, and it is what let an unresolvable pointer ride into an export envelope.
/// </para>
/// <para>
/// <b>Why a whole second engine.</b> The property is about two engines, so asserting it needs two — the same authored
/// documents driven through the same production compiler in two independently composed containers, differing only in
/// the publish-local identifiers each mints. Comparing two compilations inside one engine would leave the interesting
/// variable constant and pass for free.
/// </para>
/// <para>
/// <b>Mutation-checked.</b> Restoring any engine-local member to <c>DispatchWorkflowPinProvenance</c> turns
/// <see cref="Two_engines_that_mint_different_publish_local_identifiers_agree_on_the_parent_artifact_id"/> red.
/// </para>
/// </remarks>
public sealed class ArtifactHashPortabilityTests
{
    [Fact]
    public async Task Two_engines_that_mint_different_publish_local_identifiers_agree_on_the_parent_artifact_id()
    {
        await using var first = PublishCapableEngine.Create();
        await using var second = PublishCapableEngine.Create();

        var (firstParent, firstChild) = await first.CompileAndPublishAsync();
        var (secondParent, secondChild) = await second.CompileAndPublishAsync(
            childSourceReferenceId: "other-engine-source-child",
            parentSourceReferenceId: "other-engine-source-parent",
            childSlotId: "other-engine-slot");

        // Guard the premise: the two engines really did differ on every publish-local identifier that used to be
        // hashed. Without this the test could pass by the two engines being accidentally identical.
        var firstReference = await first.FindPublishedReferenceAsync(firstChild.Identity.ArtifactId);
        var secondReference = await second.FindPublishedReferenceAsync(secondChild.Identity.ArtifactId);
        Assert.NotEqual(firstReference.SourceReferenceId, secondReference.SourceReferenceId);
        Assert.NotEqual(firstReference.ActivationId, secondReference.ActivationId);
        Assert.NotEqual(firstReference.SlotId, secondReference.SlotId);

        // The claim. The child was never the problem — it has no pin — so it is asserted as the control: if the
        // child hashes were to diverge, something other than the pin would be leaking and the parent assertion
        // below would be measuring the wrong thing.
        Assert.Equal(firstChild.Identity.ArtifactHash, secondChild.Identity.ArtifactHash);
        Assert.Equal(firstChild.Identity.ArtifactId, secondChild.Identity.ArtifactId);
        Assert.Equal(firstParent.Identity.ArtifactHash, secondParent.Identity.ArtifactHash);
        Assert.Equal(firstParent.Identity.ArtifactId, secondParent.Identity.ArtifactId);

        // Stated at the level the invariant is actually about: the hashed bytes themselves. The pin is the only
        // place a publish-local identifier ever entered them, so comparing the metadata directly says *why* the
        // hashes match rather than leaving it to coincidence.
        var firstPin = firstParent.RootActivity.Metadata[DispatchWorkflowConstants.PinnedTargetMetadataKey];
        var secondPin = secondParent.RootActivity.Metadata[DispatchWorkflowConstants.PinnedTargetMetadataKey];
        Assert.Equal(firstPin, secondPin);
        Assert.DoesNotContain(firstReference.SourceReferenceId, firstPin, StringComparison.Ordinal);
        Assert.DoesNotContain(secondReference.SourceReferenceId, secondPin, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_behavioural_difference_still_moves_the_parent_artifact_id()
    {
        // The other half of the invariant, kept next to the first so narrowing the pin can never be "fixed" by
        // narrowing it into meaninglessness: the pin must still bind the parent to the exact child content.
        await using var engine = PublishCapableEngine.Create();
        await using var divergent = PublishCapableEngine.Create(childCorrelationEffect: "child-executed-differently");

        var (parent, child) = await engine.CompileAndPublishAsync();
        var (divergentParent, divergentChild) = await divergent.CompileAndPublishAsync();

        Assert.NotEqual(child.Identity.ArtifactHash, divergentChild.Identity.ArtifactHash);
        Assert.NotEqual(parent.Identity.ArtifactHash, divergentParent.Identity.ArtifactHash);
    }
}

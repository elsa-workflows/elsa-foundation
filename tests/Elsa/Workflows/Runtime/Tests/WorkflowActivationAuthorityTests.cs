using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Conflict rules for the one activation ledger (FR-B-006, 2026-08-15 architect review).
/// </summary>
public sealed class WorkflowActivationAuthorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowActivationSource Importer = WorkflowActivationSource.ArtifactReconciliation("prod-drop");

    [Fact]
    public async Task An_unclaimed_slot_is_claimable_and_starts_at_revision_zero()
    {
        var authority = new InMemoryWorkflowActivationAuthority();

        var transition = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        Assert.True(transition.Succeeded);
        Assert.Equal(WorkflowActivationConflict.None, transition.Conflict);
        Assert.Equal("activation-1", transition.Slot.ActiveActivationId);
        Assert.Equal(1, transition.Slot.Revision);
        Assert.Null(transition.ReplacedActivationId);
        Assert.Equal(WorkflowActivationSource.PublishingKind, transition.Slot.Source!.Kind);
    }

    [Fact]
    public async Task Replacing_an_activation_from_the_owning_source_returns_the_replaced_id()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        var first = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var second = await authority.TryActivateAsync(
            Request("activation-2", WorkflowActivationSource.Publishing, first.Slot.Revision));

        Assert.True(second.Succeeded);
        // The replaced id is what lets the coordinator retire the predecessor's reference and
        // projections; losing it would strand the previous activation's serving state.
        Assert.Equal("activation-1", second.ReplacedActivationId);
        Assert.Equal(2, second.Slot.Revision);
    }

    [Fact]
    public async Task A_stale_revision_is_a_CAS_conflict_and_does_not_move_the_slot()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        // Still presenting revision 0 after the slot advanced to 1.
        var stale = await authority.TryActivateAsync(Request("activation-2", WorkflowActivationSource.Publishing));

        Assert.False(stale.Succeeded);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, stale.Conflict);
        var slot = await authority.FindAsync("definition-1", "default");
        Assert.Equal("activation-1", slot!.ActiveActivationId);
    }

    [Fact]
    public async Task A_different_source_is_rejected_loudly_and_the_diagnostic_names_the_owner()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        var owned = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var foreign = await authority.TryActivateAsync(
            Request("activation-2", Importer, owned.Slot.Revision));

        Assert.False(foreign.Succeeded);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);
        // The diagnostic must name the OWNING source: an operator seeing this needs to know which
        // path already owns the definition, not merely that they were refused.
        Assert.Contains("publishing", foreign.Diagnostic!, StringComparison.Ordinal);
        Assert.Contains("artifact-reconciliation:prod-drop", foreign.Diagnostic!, StringComparison.Ordinal);

        var slot = await authority.FindAsync("definition-1", "default");
        Assert.Equal("activation-1", slot!.ActiveActivationId);
    }

    [Fact]
    public async Task Two_reconciliation_sources_are_different_owners()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        var owned = await authority.TryActivateAsync(Request("activation-1", Importer));

        var otherFolder = await authority.TryActivateAsync(
            Request("activation-2", WorkflowActivationSource.ArtifactReconciliation("staging-drop"), owned.Slot.Revision));

        // Same kind, different source id: one folder must not silently take over another's
        // definitions, so ownership compares the source id too.
        Assert.False(otherFolder.Succeeded);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, otherFolder.Conflict);
    }

    /// <summary>
    /// T118: a request carrying an explicit takeover intent claims a foreign-owned slot and becomes its owner.
    /// </summary>
    /// <remarks>
    /// The authority honours the intent <b>generically</b> — it never asks who declared it. Asserted with the
    /// importer taking over from publishing, i.e. the opposite of the production asymmetry, so the test cannot
    /// pass on a rule that special-cases a particular source.
    /// </remarks>
    [Fact]
    public async Task An_explicit_takeover_claims_a_foreign_owned_slot_and_becomes_the_owner()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        var owned = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var takeover = await authority.TryActivateAsync(Request(
            "activation-2",
            Importer,
            owned.Slot.Revision,
            intent: WorkflowActivationOwnershipIntent.TakeOver));

        Assert.True(takeover.Succeeded);
        Assert.Equal(WorkflowActivationConflict.None, takeover.Conflict);
        Assert.Equal("activation-1", takeover.ReplacedActivationId);
        // The displaced OWNER travels with the displaced id: compensation has to restore both, and only the
        // authority knows who it was.
        Assert.Equal(WorkflowActivationSource.PublishingKind, takeover.ReplacedSource!.Kind);

        // One slot, re-owned — never a second lane beside the incumbent.
        var slot = Assert.Single(await authority.ListByDefinitionAsync("definition-1"));
        Assert.Equal("activation-2", slot.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, slot.Source!.Kind);
        Assert.Equal("prod-drop", slot.Source.SourceId);
    }

    /// <summary>
    /// Takeover overrides ownership, never the CAS guard: a stale revision is still a conflict.
    /// </summary>
    /// <remarks>
    /// Without this, "explicit wins" would quietly become "explicit wins races too", and two concurrent operator
    /// commands could interleave with the loser's write landing last.
    /// </remarks>
    [Fact]
    public async Task An_explicit_takeover_does_not_override_the_revision_guard()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var stale = await authority.TryActivateAsync(Request(
            "activation-2",
            Importer,
            expectedRevision: 0,
            intent: WorkflowActivationOwnershipIntent.TakeOver));

        Assert.False(stale.Succeeded);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, stale.Conflict);
        Assert.Equal("activation-1", (await authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
    }

    [Fact]
    public async Task Ownership_is_not_inferred_from_activation_id_prefixes()
    {
        var authority = new InMemoryWorkflowActivationAuthority();

        // An activation id that *looks* like it came from the importer, but is owned by publishing.
        // Prefix-sniffing was the explicitly rejected design (FR-B-006): if ownership were inferred
        // from the id, the importer below would wrongly be treated as the owner and allowed through.
        var owned = await authority.TryActivateAsync(
            Request("import:prod-drop:activation-1", WorkflowActivationSource.Publishing));

        var foreign = await authority.TryActivateAsync(Request("activation-2", Importer, owned.Slot.Revision));

        Assert.False(foreign.Succeeded);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);
    }

    [Fact]
    public async Task Deactivation_clears_ownership_so_the_slot_becomes_claimable_again()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        var owned = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var cleared = await authority.TryDeactivateAsync(
            "definition-1", "default", WorkflowActivationSource.Publishing, owned.Slot.Revision, Now);

        Assert.True(cleared.Succeeded);
        Assert.Equal("activation-1", cleared.ReplacedActivationId);
        Assert.Null(cleared.Slot.ActiveActivationId);

        // Now that nothing is live, the other source may legitimately claim it.
        var claimed = await authority.TryActivateAsync(Request("activation-2", Importer, cleared.Slot.Revision));
        Assert.True(claimed.Succeeded);
    }

    [Fact]
    public async Task A_foreign_source_cannot_deactivate_an_owned_slot()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        var owned = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var foreign = await authority.TryDeactivateAsync(
            "definition-1", "default", Importer, owned.Slot.Revision, Now);

        Assert.False(foreign.Succeeded);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);
    }

    [Fact]
    public async Task Slots_are_listed_per_definition_and_are_independent_across_lanes()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));
        await authority.TryActivateAsync(Request("activation-2", Importer, slotName: "canary"));

        var slots = await authority.ListByDefinitionAsync("definition-1");

        Assert.Equal(2, slots.Count);
        Assert.Empty(await authority.ListByDefinitionAsync("definition-other"));
    }

    private static WorkflowActivationSlotRequest Request(
        string activationId,
        WorkflowActivationSource source,
        long expectedRevision = 0,
        string slotName = "default",
        WorkflowActivationOwnershipIntent intent = WorkflowActivationOwnershipIntent.RespectExistingOwner) =>
        new("definition-1", slotName, activationId, source, expectedRevision, Now, intent);
}

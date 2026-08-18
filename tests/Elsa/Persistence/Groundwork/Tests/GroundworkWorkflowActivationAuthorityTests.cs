using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Parity coverage for the durable <see cref="IWorkflowActivationAuthority"/> Groundwork bridge (FR-B-006).
/// </summary>
/// <remarks>
/// These are the conflict rules of <c>InMemoryWorkflowActivationAuthority</c>, asserted against the durable
/// ledger so that composing Groundwork persistence cannot change an activation outcome. They run against both a
/// real Groundwork SQLite provider and the in-memory document store, like the sibling projection bridges.
/// </remarks>
public sealed class GroundworkWorkflowActivationAuthorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowActivationSource Importer = WorkflowActivationSource.ArtifactReconciliation("prod-drop");

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task An_unclaimed_slot_is_claimable_and_starts_at_revision_zero(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);

        Assert.Null(await authority.FindAsync("definition-1", "default"));

        var transition = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        Assert.True(transition.Succeeded);
        Assert.Equal(WorkflowActivationConflict.None, transition.Conflict);
        Assert.Equal("activation-1", transition.Slot.ActiveActivationId);
        Assert.Equal(1, transition.Slot.Revision);
        Assert.Null(transition.ReplacedActivationId);
        Assert.Equal(WorkflowActivationSource.PublishingKind, transition.Slot.Source!.Kind);

        var reloaded = await authority.FindAsync("definition-1", "default");
        Assert.Equal("activation-1", reloaded!.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.PublishingKind, reloaded.Source!.Kind);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Replacing_an_activation_from_the_owning_source_returns_the_replaced_id(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);
        var first = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var second = await authority.TryActivateAsync(
            Request("activation-2", WorkflowActivationSource.Publishing, first.Slot.Revision));

        Assert.True(second.Succeeded);
        Assert.Equal("activation-1", second.ReplacedActivationId);
        Assert.Equal(2, second.Slot.Revision);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task A_stale_revision_is_a_CAS_conflict_and_does_not_move_the_slot(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);
        await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var stale = await authority.TryActivateAsync(Request("activation-2", WorkflowActivationSource.Publishing));

        Assert.False(stale.Succeeded);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, stale.Conflict);
        var slot = await authority.FindAsync("definition-1", "default");
        Assert.Equal("activation-1", slot!.ActiveActivationId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task A_different_source_is_rejected_loudly_and_the_diagnostic_names_the_owner(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);
        var owned = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var foreign = await authority.TryActivateAsync(Request("activation-2", Importer, owned.Slot.Revision));

        Assert.False(foreign.Succeeded);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);
        Assert.Contains("publishing", foreign.Diagnostic!, StringComparison.Ordinal);
        Assert.Contains("artifact-reconciliation:prod-drop", foreign.Diagnostic!, StringComparison.Ordinal);

        var slot = await authority.FindAsync("definition-1", "default");
        Assert.Equal("activation-1", slot!.ActiveActivationId);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Two_reconciliation_sources_are_different_owners(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);
        var owned = await authority.TryActivateAsync(Request("activation-1", Importer));

        var otherFolder = await authority.TryActivateAsync(
            Request("activation-2", WorkflowActivationSource.ArtifactReconciliation("staging-drop"), owned.Slot.Revision));

        Assert.False(otherFolder.Succeeded);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, otherFolder.Conflict);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Ownership_is_not_inferred_from_activation_id_prefixes(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);

        var owned = await authority.TryActivateAsync(
            Request("import:prod-drop:activation-1", WorkflowActivationSource.Publishing));

        var foreign = await authority.TryActivateAsync(Request("activation-2", Importer, owned.Slot.Revision));

        Assert.False(foreign.Succeeded);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Deactivation_clears_ownership_so_the_slot_becomes_claimable_again(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);
        var owned = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var cleared = await authority.TryDeactivateAsync(
            "definition-1", "default", WorkflowActivationSource.Publishing, owned.Slot.Revision, Now);

        Assert.True(cleared.Succeeded);
        Assert.Equal("activation-1", cleared.ReplacedActivationId);
        Assert.Null(cleared.Slot.ActiveActivationId);

        var claimed = await authority.TryActivateAsync(Request("activation-2", Importer, cleared.Slot.Revision));
        Assert.True(claimed.Succeeded);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task A_foreign_source_cannot_deactivate_an_owned_slot(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);
        var owned = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var foreign = await authority.TryDeactivateAsync(
            "definition-1", "default", Importer, owned.Slot.Revision, Now);

        Assert.False(foreign.Succeeded);
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Slots_are_listed_per_definition_and_are_independent_across_lanes(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);
        await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));
        await authority.TryActivateAsync(Request("activation-2", Importer, slotName: "canary"));

        var slots = await authority.ListByDefinitionAsync("definition-1");

        Assert.Equal(2, slots.Count);
        Assert.Equal(["canary", "default"], slots.Select(slot => slot.SlotName));
        Assert.Empty(await authority.ListByDefinitionAsync("definition-other"));
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task An_activation_that_is_already_live_elsewhere_cannot_claim_a_second_slot(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);
        await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var second = await authority.TryActivateAsync(
            Request("activation-1", WorkflowActivationSource.Publishing, slotName: "canary"));

        Assert.False(second.Succeeded);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, second.Conflict);
        Assert.Equal("The activation is already live in another slot.", second.Diagnostic);
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task A_deactivated_activation_is_no_longer_live_anywhere(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);
        var owned = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));
        await authority.TryDeactivateAsync(
            "definition-1", "default", WorkflowActivationSource.Publishing, owned.Slot.Revision, Now);

        // The cleared slot omits the activation id entirely, so the "already live elsewhere" guard must not
        // still find it through the by-active-activation index.
        var moved = await authority.TryActivateAsync(
            Request("activation-1", WorkflowActivationSource.Publishing, slotName: "canary"));

        Assert.True(moved.Succeeded);
    }

    /// <summary>
    /// T118 parity: an explicit takeover claims a foreign-owned slot on the durable ledger too, re-owning the one
    /// slot rather than adding a second.
    /// </summary>
    /// <remarks>
    /// Parity matters here more than usual. If only the in-memory authority honoured the intent, an operator's
    /// publish over an imported definition would succeed in every test and fail on every real deployment.
    /// </remarks>
    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task An_explicit_takeover_claims_a_foreign_owned_slot_and_becomes_the_owner(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var authority = NewAuthority(fixture);
        var owned = await authority.TryActivateAsync(Request("activation-1", WorkflowActivationSource.Publishing));

        var takeover = await authority.TryActivateAsync(Request(
            "activation-2",
            Importer,
            owned.Slot.Revision,
            intent: WorkflowActivationOwnershipIntent.TakeOver));

        Assert.True(takeover.Succeeded);
        Assert.Equal("activation-1", takeover.ReplacedActivationId);
        Assert.Equal(WorkflowActivationSource.PublishingKind, takeover.ReplacedSource!.Kind);

        var slot = Assert.Single(await authority.ListByDefinitionAsync("definition-1"));
        Assert.Equal("activation-2", slot.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, slot.Source!.Kind);
        Assert.Equal("prod-drop", slot.Source.SourceId);
    }

    private static IWorkflowActivationAuthority NewAuthority(GroundworkDocumentStoreFixture fixture) =>
        new GroundworkWorkflowActivationAuthority(fixture.DocumentStore, GroundworkTestSerialization.Serializer);

    private static WorkflowActivationSlotRequest Request(
        string activationId,
        WorkflowActivationSource source,
        long expectedRevision = 0,
        string slotName = "default",
        WorkflowActivationOwnershipIntent intent = WorkflowActivationOwnershipIntent.RespectExistingOwner) =>
        new("definition-1", slotName, activationId, source, expectedRevision, Now, intent);
}

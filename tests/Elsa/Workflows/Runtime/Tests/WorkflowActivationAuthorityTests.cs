using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowActivationAuthorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowActivationSource Importer = WorkflowActivationSource.ArtifactReconciliation("prod-drop");

    [Fact]
    public async Task First_claim_uses_revision_zero_and_advances_to_one()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        var result = await authority.TryActivateAsync(Request("a", WorkflowActivationSource.Publishing));
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Slot.Revision);
        Assert.Null(result.ReplacedActivationId);
    }

    [Fact]
    public async Task Same_owner_replaces_and_stale_cas_does_not_move_slot()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        var first = await authority.TryActivateAsync(Request("a", WorkflowActivationSource.Publishing));
        var replacement = await authority.TryActivateAsync(Request("b", WorkflowActivationSource.Publishing, first.Slot.Revision));
        Assert.True(replacement.Succeeded);
        Assert.Equal("a", replacement.ReplacedActivationId);
        var stale = await authority.TryActivateAsync(Request("c", WorkflowActivationSource.Publishing));
        Assert.False(stale.Succeeded);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, stale.Conflict);
        Assert.Equal("b", (await authority.FindAsync("definition-1", "default"))!.ActiveActivationId);
    }

    [Fact]
    public async Task Foreign_owner_is_rejected_but_explicit_takeover_is_generic()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        var first = await authority.TryActivateAsync(Request("a", WorkflowActivationSource.Publishing));
        var foreign = await authority.TryActivateAsync(Request("b", Importer, first.Slot.Revision));
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);
        var takeover = await authority.TryActivateAsync(Request("b", Importer, first.Slot.Revision, intent: WorkflowActivationOwnershipIntent.TakeOver));
        Assert.True(takeover.Succeeded);
        Assert.Equal(WorkflowActivationSource.PublishingKind, takeover.ReplacedSource!.Kind);
    }

    [Fact]
    public async Task Activation_id_prefix_does_not_define_ownership_and_deactivate_clears_it()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        var first = await authority.TryActivateAsync(Request("import:prod-drop:a", WorkflowActivationSource.Publishing));
        var foreign = await authority.TryActivateAsync(Request("b", Importer, first.Slot.Revision));
        Assert.Equal(WorkflowActivationConflict.ForeignSource, foreign.Conflict);
        var cleared = await authority.TryDeactivateAsync("definition-1", "default", WorkflowActivationSource.Publishing, first.Slot.Revision, Now);
        Assert.True(cleared.Succeeded);
        Assert.True((await authority.TryActivateAsync(Request("b", Importer, cleared.Slot.Revision))).Succeeded);
    }

    [Fact]
    public async Task One_activation_cannot_be_live_in_two_lanes_and_listing_is_deterministic()
    {
        var authority = new InMemoryWorkflowActivationAuthority();
        await authority.TryActivateAsync(Request("a", WorkflowActivationSource.Publishing, slotName: "zulu"));
        var duplicate = await authority.TryActivateAsync(Request("a", WorkflowActivationSource.Publishing, slotName: "alpha"));
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, duplicate.Conflict);
        var slots = await authority.ListByDefinitionAsync("definition-1");
        Assert.Single(slots);
        Assert.Equal("zulu", slots.Single().SlotName);
    }

    private static WorkflowActivationSlotRequest Request(string id, WorkflowActivationSource source, long revision = 0, string slotName = "default", WorkflowActivationOwnershipIntent intent = WorkflowActivationOwnershipIntent.RespectExistingOwner) =>
        new("definition-1", slotName, id, source, revision, Now, intent);
}

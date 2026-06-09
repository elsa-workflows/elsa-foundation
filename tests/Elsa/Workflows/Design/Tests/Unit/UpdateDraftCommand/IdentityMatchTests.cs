using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T015 (SC-015, FR-013d) — identity vs payload. Same match key + changed payload → a single
/// UPDATE event. A differing key → a REMOVE of the old + an ADD of the new (no update event
/// crosses an identity boundary).
/// </summary>
public sealed class IdentityMatchTests
{
    [Fact]
    public async Task Same_key_changed_payload_emits_a_single_update()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(variables: [Variable("v1", "MyVar")]));

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(variables: [Variable("v1", "RenamedVar")]));

        var diff = DiffEventsSince(host, skip);
        Assert.IsType<OnVariableUpdatedInDraft>(Assert.Single(diff));
        Assert.Empty(diff.OfType<OnVariableDeclaredInDraft>());
        Assert.Empty(diff.OfType<OnVariableRemovedFromDraft>());
    }

    [Fact]
    public async Task Differing_key_emits_a_remove_and_an_add()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(variables: [Variable("v1", "MyVar")]));

        // The key itself changes (v1 → v2) — an identity change, not a rename of the payload.
        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(variables: [Variable("v2", "MyVar")]));

        var diff = DiffEventsSince(host, skip);
        Assert.Single(diff.OfType<OnVariableDeclaredInDraft>(), e => e.Variable.ReferenceKey == "v2");
        Assert.Single(diff.OfType<OnVariableRemovedFromDraft>(), e => e.VariableReferenceKey == "v1");
        Assert.Empty(diff.OfType<OnVariableUpdatedInDraft>());
    }
}

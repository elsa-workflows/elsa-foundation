using Elsa.Workflows.Design.Core.Events;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T015 (SC-015, FR-013d) — identity vs payload, driving
/// <see cref="Elsa.Workflows.Design.Persistence.Core.Services.DraftStateDiffEngine"/> directly
/// (per-diff mutation-event publication retired from the mutation command). Same match key +
/// changed payload → a single UPDATE event. A differing key → a REMOVE of the old + an ADD of the
/// new (no update event crosses an identity boundary).
/// </summary>
public sealed class IdentityMatchTests
{
    [Fact]
    public void Same_key_changed_payload_emits_a_single_update()
    {
        var diff = Evaluate(State(variables: [Variable("v1", "MyVar")]), State(variables: [Variable("v1", "RenamedVar")]));

        Assert.IsType<VariableUpdatedInDraft>(Assert.Single(diff));
        Assert.Empty(diff.OfType<VariableDeclaredInDraft>());
        Assert.Empty(diff.OfType<VariableRemovedFromDraft>());
    }

    [Fact]
    public void Differing_key_emits_a_remove_and_an_add()
    {
        // The key itself changes (v1 → v2) — an identity change, not a rename of the payload.
        var diff = Evaluate(State(variables: [Variable("v1", "MyVar")]), State(variables: [Variable("v2", "MyVar")]));

        Assert.Single(diff.OfType<VariableDeclaredInDraft>(), e => e.Variable.ReferenceKey == "v2");
        Assert.Single(diff.OfType<VariableRemovedFromDraft>(), e => e.VariableReferenceKey == "v1");
        Assert.Empty(diff.OfType<VariableUpdatedInDraft>());
    }
}

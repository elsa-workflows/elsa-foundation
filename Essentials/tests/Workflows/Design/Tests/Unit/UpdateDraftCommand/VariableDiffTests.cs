using Elsa.Workflows.Design.Core.Events;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T009 (FR-013, SC-010) — variable diff coverage, driving
/// <see cref="Elsa.Workflows.Design.Persistence.Core.Services.DraftStateDiffEngine"/> directly
/// (per-diff mutation-event publication retired from the mutation command). Variables match by
/// <c>ReferenceKey</c>: declare/update/remove → <c>VariableDeclaredInDraft</c> /
/// <c>VariableUpdatedInDraft</c> / <c>VariableRemovedFromDraft</c>.
/// </summary>
public sealed class VariableDiffTests
{
    [Fact]
    public void Declaring_a_variable_emits_VariableDeclaredInDraft()
    {
        var diff = Evaluate(State(), State(variables: [Variable("v1", "MyVar")]));

        var declared = Assert.IsType<VariableDeclaredInDraft>(Assert.Single(diff));
        Assert.Equal("MyVar", declared.Variable.Name);
    }

    [Fact]
    public void Updating_a_variable_payload_emits_VariableUpdatedInDraft()
    {
        var diff = Evaluate(State(variables: [Variable("v1", "MyVar")]), State(variables: [Variable("v1", "Renamed")]));

        var updated = Assert.IsType<VariableUpdatedInDraft>(Assert.Single(diff));
        Assert.Equal("v1", updated.VariableReferenceKey);
        Assert.Equal("MyVar", updated.OldValue.Name);
        Assert.Equal("Renamed", updated.NewValue.Name);
    }

    [Fact]
    public void Removing_a_variable_emits_VariableRemovedFromDraft()
    {
        var diff = Evaluate(State(variables: [Variable("v1", "MyVar")]), State(variables: []));

        var removed = Assert.IsType<VariableRemovedFromDraft>(Assert.Single(diff));
        Assert.Equal("v1", removed.VariableReferenceKey);
    }
}

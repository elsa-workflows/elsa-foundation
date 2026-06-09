using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T009 (FR-013, SC-010) — migrated variable diff coverage. Variables match by
/// <c>ReferenceKey</c>: declare/update/remove → <c>OnVariableDeclaredInDraft</c> /
/// <c>OnVariableUpdatedInDraft</c> / <c>OnVariableRemovedFromDraft</c>.
/// </summary>
public sealed class VariableDiffTests
{
    [Fact]
    public async Task Declaring_a_variable_emits_OnVariableDeclaredInDraft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(variables: [Variable("v1", "MyVar")]));

        var declared = Assert.IsType<OnVariableDeclaredInDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal("MyVar", declared.Variable.Name);
    }

    [Fact]
    public async Task Updating_a_variable_payload_emits_OnVariableUpdatedInDraft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(variables: [Variable("v1", "MyVar")]));

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(variables: [Variable("v1", "Renamed")]));

        var updated = Assert.IsType<OnVariableUpdatedInDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal("v1", updated.VariableReferenceKey);
        Assert.Equal("MyVar", updated.OldValue.Name);
        Assert.Equal("Renamed", updated.NewValue.Name);
    }

    [Fact]
    public async Task Removing_a_variable_emits_OnVariableRemovedFromDraft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);
        await Update(host, draftId, State(variables: [Variable("v1", "MyVar")]));

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(variables: []));

        var removed = Assert.IsType<OnVariableRemovedFromDraft>(Assert.Single(DiffEventsSince(host, skip)));
        Assert.Equal("v1", removed.VariableReferenceKey);
    }
}

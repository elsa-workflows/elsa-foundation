using Elsa.Workflows.Design.Persistence.EFCore.Constants;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T026 (SC-006) — the command IS the mutation shell (US3). A single <c>Execute</c> producing N
/// per-diff events: acquires the per-Draft lock exactly once; publishes <c>OnDraftValidating</c>
/// exactly once (the in-lock gate) against the POST-diff state; flushes State + the validation
/// sibling together; then publishes the N mutation events (the causes) followed by a single
/// <c>OnDraftValidated</c> (the consequence) — cause-before-effect across the whole stream.
/// </summary>
public sealed class ShellOrderingTests
{
    [Fact]
    public async Task One_execute_runs_the_shell_in_cause_before_effect_order_with_a_single_lock_and_transaction()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        // Seed an initial state so the asserting update diffs across several dimensions.
        await Update(host, draftId, State(
            variables: [Variable("v1", "MyVar")],
            activities: [Node("a"), Node("b")],
            connections: [Connection("a", "b")]));

        var key = LockKeys.DraftKey(draftId);
        var locksBefore = host.LockProvider.AcquireCounts.GetValueOrDefault(key, 0);
        var skip = host.EventPublisher.CapturedEvents.Count;

        // Desired: add activity c, drop connection a→b, rename v1 → exactly 3 mutation events.
        await Update(host, draftId, State(
            variables: [Variable("v1", "RenamedVar")],
            activities: [Node("a"), Node("b"), Node("c")],
            connections: []));

        // (1) One lock acquisition for this call.
        Assert.Equal(locksBefore + 1, host.LockProvider.AcquireCounts[key]);

        var window = host.EventPublisher.CapturedEvents.Skip(skip).ToList();

        // (2) Exactly one validation gate, fired against POST-diff state (sees c + the rename).
        var validating = Assert.Single(window.OfType<OnDraftValidating>());
        Assert.Contains("c", validating.Draft.State.Activities.Select(n => n.NodeId));
        Assert.Contains(validating.Draft.State.Variables, v => v.Name == "RenamedVar");

        // (3) Exactly one validation outcome.
        Assert.Single(window.OfType<OnDraftValidated>());

        // (4) Exactly the three per-diff mutation events.
        var diffEvents = DiffEventsSince(host, skip);
        Assert.Equal(3, diffEvents.Count);

        // (5) Cause-before-effect: the gate first, then every diff event, then the outcome last.
        var validatingIdx = window.FindIndex(e => e is OnDraftValidating);
        var validatedIdx = window.FindIndex(e => e is OnDraftValidated);
        foreach (var diff in diffEvents)
        {
            var idx = window.IndexOf(diff);
            Assert.True(idx > validatingIdx, "diff events publish after the in-lock validating gate");
            Assert.True(idx < validatedIdx, "diff events (causes) publish before OnDraftValidated (consequence)");
        }

        // (6) State + validation sibling both persisted — the single in-lock transaction.
        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("c", draft.StateSource);
        Assert.Contains("RenamedVar", draft.StateSource);

        var sibling = await ctx.WorkflowDefinitionDraftValidations
            .FirstAsync(v => v.WorkflowDefinitionDraftId == draftId);
        Assert.Empty(sibling.Errors);
    }
}

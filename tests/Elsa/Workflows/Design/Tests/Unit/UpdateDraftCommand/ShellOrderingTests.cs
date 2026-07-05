using LockKeys = Elsa.Workflows.Design.Persistence.Core.Constants.WorkflowDesignPersistenceLockKeys;
using Elsa.Persistence.EFCore.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit.UpdateDraftCommand;

/// <summary>
/// T026 (SC-006) — the command IS the mutation shell (US3). A single <c>Execute</c> acquires the
/// per-Draft lock exactly once, then runs the new shell in order: <c>OnEntityLoading</c>
/// (Sequential hydration) → <c>OnDraftValidating</c> (Sequential, the in-lock validation gate)
/// → save → lock release → <c>OnDraftValidated</c> (Background, after commit, carrying the error
/// set). Per-diff mutation events are no longer computed or published anywhere in the window
/// (publication retired), so none of the 20 mutation event types appear.
/// </summary>
public sealed class ShellOrderingTests
{
    [Fact]
    public async Task One_execute_runs_the_new_shell_in_order_with_a_single_lock_and_transaction()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        // Seed an initial state so the asserting update mutates across several dimensions.
        await Update(host, draftId, State(
            variables: [Variable("v1", "MyVar")],
            activities: [Node("a"), Node("b")]));

        var key = LockKeys.DraftKey(draftId);
        var locksBefore = host.LockProvider.AcquireCounts.GetValueOrDefault(key, 0);
        var skip = host.EventPublisher.CapturedEvents.Count;

        // Desired: add activity c, rename v1.
        await Update(host, draftId, State(
            variables: [Variable("v1", "RenamedVar")],
            activities: [Node("a"), Node("b"), Node("c")]));

        // (1) One lock acquisition for this call.
        Assert.Equal(locksBefore + 1, host.LockProvider.AcquireCounts[key]);

        var window = host.EventPublisher.CapturedEvents.Skip(skip).ToList();

        // (2) Exactly one hydration + one validation gate, fired against POST-mutation state.
        Assert.Single(window.OfType<OnEntityLoading>());
        var validating = Assert.Single(window.OfType<OnDraftValidating>());
        var activityNodeIds = validating.Draft.State.RootActivity?.Structure?.Payload.GetProperty("activities")
            .EnumerateArray()
            .Select(activity => activity.GetProperty("nodeId").GetString())
            .ToArray() ?? [];
        Assert.Contains("c", activityNodeIds);
        Assert.Contains(validating.Draft.State.Variables, v => v.Name == "RenamedVar");

        // (3) Exactly one validation outcome.
        Assert.Single(window.OfType<OnDraftValidated>());

        // (4) No per-diff mutation events — publication retired.
        Assert.Empty(DiffEventsSince(host, skip));

        // (5) Ordering: hydrate → validate (in-lock) → validated (after commit).
        var loadingIdx = window.FindIndex(e => e is OnEntityLoading);
        var validatingIdx = window.FindIndex(e => e is OnDraftValidating);
        var validatedIdx = window.FindIndex(e => e is OnDraftValidated);
        Assert.True(loadingIdx < validatingIdx, "OnEntityLoading precedes the in-lock validating gate");
        Assert.True(validatingIdx < validatedIdx, "OnDraftValidating (in-lock) precedes OnDraftValidated (after commit)");

        // (6) State persisted — the single in-lock transaction.
        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("c", draft.StateSource);
        Assert.Contains("RenamedVar", draft.StateSource);
    }
}

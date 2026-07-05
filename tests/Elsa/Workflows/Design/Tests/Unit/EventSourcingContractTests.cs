using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// T031 (SC-012) + T032 (SC-013), superseded 2026-07-05. The FR-017 "open/closed" event-sourcing
/// contract has been inverted: per-diff mutation-event publication is retired until an
/// event-sourcing consumer exists, so <see cref="Persistence.Core.Contracts.IUpdateDraftCommand"/>
/// no longer publishes any of the 20 <c>Elsa.Workflows.Design.Core.Events</c> mutation event types
/// — even when a subscriber for one is registered. This file now pins that retirement: a
/// registered mutation-event subscriber receives nothing, the command still completes and persists,
/// and the Background-shielding guarantee is observed via the surviving <c>OnDraftValidated</c>
/// event (a throwing subscriber there must not break Execute or lose the Draft).
/// </summary>
public sealed class EventSourcingContractTests
{
    [Fact]
    public async Task No_mutation_event_is_published_even_when_a_subscriber_is_registered()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        var received = new List<OnActivityAddedToDraft>();
        host.EventPublisher.Subscribe<OnActivityAddedToDraft>(e =>
        {
            received.Add(e);
            return Task.CompletedTask;
        });

        var skip = host.EventPublisher.CapturedEvents.Count;
        await Update(host, draftId, State(activities: [Node("node-1")]));

        // Publication retired: the subscriber receives nothing, and no mutation event appears on the
        // substrate at all.
        Assert.Empty(received);
        Assert.Empty(DiffEventsSince(host, skip));
    }

    [Fact]
    public async Task The_command_still_completes_and_persists_with_no_mutation_subscriber()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        // No mutation-event publication happens, but the Draft still persists.
        await Update(host, draftId, State(activities: [Node("node-1")]));

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("node-1", draft.StateSource);
    }

    [Fact]
    public async Task Background_shielding_a_throwing_OnDraftValidated_subscriber_does_not_break_Execute_or_lose_the_Draft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        // OnDraftValidated publishes Background (after commit): a faulty subscriber must not break
        // the command, and the Draft must still persist (the Background strategy owns its resilience).
        host.EventPublisher.Subscribe<OnDraftValidated>(_ =>
            throw new InvalidOperationException("subscriber blew up"));

        var ex = await Record.ExceptionAsync(() =>
            Update(host, draftId, State(activities: [Node("node-1")])));
        Assert.Null(ex);

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("node-1", draft.StateSource);
    }
}

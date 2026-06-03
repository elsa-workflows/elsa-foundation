using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.UpdateDraftTestKit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// T031 (SC-012) + T032 (SC-013) — the event-sourcing seam stays open. The per-diff mutation
/// events <see cref="Persistence.Core.Contracts.IUpdateDraftCommand"/> emits are observable by
/// any subscriber (open mode), the command is unaffected by the absence of subscribers (closed
/// mode), and — because the diff events publish Background — a faulty subscriber cannot break the
/// command or roll back the persisted Draft (shielding).
/// </summary>
public sealed class EventSourcingContractTests
{
    [Fact]
    public async Task Open_mode_a_registered_subscriber_receives_the_mutation_event()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        var received = new List<OnActivityAddedToDraft>();
        host.EventPublisher.Subscribe<OnActivityAddedToDraft>(e =>
        {
            received.Add(e);
            return Task.CompletedTask;
        });

        await Update(host, draftId, State(activities: [Node("node-1")]));

        var observed = Assert.Single(received);
        Assert.Equal("node-1", observed.NodeId);
    }

    [Fact]
    public async Task Closed_mode_with_no_subscriber_the_command_still_completes_and_persists()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        // No subscriber registered — the diff still fires onto the substrate and the Draft persists.
        await Update(host, draftId, State(activities: [Node("node-1")]));

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("node-1", draft.StateSource);
    }

    [Fact]
    public async Task Background_shielding_a_throwing_subscriber_does_not_break_Execute_or_lose_the_Draft()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await SeedEmptyDraft(host);

        // A Background-published mutation event with a faulty subscriber: the command must still
        // complete and the Draft must still persist (the Background strategy owns its resilience).
        host.EventPublisher.Subscribe<OnActivityAddedToDraft>(_ =>
            throw new InvalidOperationException("subscriber blew up"));

        var ex = await Record.ExceptionAsync(() =>
            Update(host, draftId, State(activities: [Node("node-1")])));
        Assert.Null(ex);

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(d => d.Id == draftId);
        Assert.Contains("node-1", draft.StateSource);
    }
}

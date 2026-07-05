using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.DraftMutationCommandTests;

/// <summary>
/// SC-018 + Unit C FR-029. <see cref="IDiscardDraftCommand"/> atomically deletes a Draft and its
/// layout sibling via the EF cascade (validation is no longer persisted, so there is no validation
/// row to delete). Versions are never touched. The command is idempotent — a second Discard on the
/// same id is a no-op.
/// </summary>
public sealed class DiscardDraftTests
{
    private const string WorkflowDefinitionId = "wf-1";

    [Fact]
    public async Task Discard_deletes_Draft_and_Layout_atomically()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await CreateDraft(host);

        await Discard(host, draftId);

        using var ctx = host.CreateContext();
        Assert.Null(await ctx.WorkflowDefinitionDrafts.FirstOrDefaultAsync(d => d.Id == draftId));
        Assert.Null(await ctx.WorkflowDefinitionDraftLayouts.FirstOrDefaultAsync(l => l.WorkflowDefinitionDraftId == draftId));
    }

    [Fact]
    public async Task Discard_does_not_touch_any_WorkflowDefinitionVersion()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await CreateDraft(host);
        var versionId = await SeedVersion(host, WorkflowDefinitionId);

        await Discard(host, draftId);

        using var ctx = host.CreateContext();
        Assert.NotNull(await ctx.WorkflowDefinitionVersions.FirstOrDefaultAsync(v => v.Id == versionId));
    }

    [Fact]
    public async Task Discard_publishes_OnDraftDiscarded()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await CreateDraft(host);

        await Discard(host, draftId);

        var published = host.EventPublisher.LastOf<OnDraftDiscarded>();
        Assert.NotNull(published);
        Assert.Equal(draftId, published!.DraftId);
        Assert.Equal(WorkflowDefinitionId, published.WorkflowDefinitionId);
    }

    [Fact]
    public async Task Second_discard_on_same_id_is_a_no_op()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var draftId = await CreateDraft(host);

        await Discard(host, draftId);
        await Discard(host, draftId); // Second call: Draft doesn't exist; command exits cleanly.

        // Exactly one OnDraftDiscarded — the second call did not re-publish.
        var discardEvents = host.EventPublisher.CapturedEvents.OfType<OnDraftDiscarded>().ToArray();
        Assert.Single(discardEvents);
    }

    private static async Task<string> CreateDraft(WorkflowsDesignTestHost host)
    {
        await host.EnsureDefinition(WorkflowDefinitionId);

        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>().Execute(WorkflowDefinitionId);
    }

    private static async Task Discard(WorkflowsDesignTestHost host, string draftId)
    {
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDiscardDraftCommand>().Execute(draftId);
    }

    private static async Task<string> SeedVersion(WorkflowsDesignTestHost host, string definitionId)
    {
        var versionId = Guid.NewGuid().ToString("N");

        using var ctx = host.CreateContext();

        if (!await ctx.WorkflowDefinitions.AnyAsync(d => d.Id == definitionId))
            ctx.WorkflowDefinitions.Add(new WorkflowDefinition { Id = definitionId, Name = "wf" });

        ctx.WorkflowDefinitionVersions.Add(new WorkflowDefinitionVersion(definitionId, "1.0.0")
        {
            Id = versionId,
            State = new WorkflowDefinitionState([], null, [], [], null, null),
        });

        await ctx.SaveChangesAsync();
        return versionId;
    }
}

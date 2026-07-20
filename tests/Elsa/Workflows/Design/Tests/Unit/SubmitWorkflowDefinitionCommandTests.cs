using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Events.Core.Contracts;
using Elsa.Persistence.EFCore.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class SubmitWorkflowDefinitionCommandTests
{
    [Fact]
    public async Task Execute_persists_definition_draft_and_initial_version_with_same_state()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var state = new WorkflowDefinitionState(
            Variables: [],
            RootActivity: new ActivityNode(
                NodeId: "root",
                ActivityVersionId: "activity-version-1",
                Inputs: [new ArgumentState("Text", new ArgumentValue("Hello", "Literal"), null, null, null, null)],
                Outputs: []),
            Inputs: [],
            Outputs: [],
            StrategyOptions: null);

        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ISubmitWorkflowDefinitionCommand>();

        var submitted = await command.Execute(WorkflowsDesignTestHost.TestOperationKey, "Demo", "Submitted over REST", state);

        await using var ctx = host.CreateContext();
        var definition = await ctx.WorkflowDefinitions.FirstOrDefaultAsync(x => x.Id == submitted.DefinitionId);
        var draft = await LoadDraft(host, ctx, submitted.DraftId);
        var version = await LoadVersion(host, ctx, submitted.VersionId);

        Assert.NotNull(definition);
        Assert.Equal("Demo", definition.Name);
        Assert.Equal(submitted.DefinitionId, draft.WorkflowDefinitionId);
        Assert.Equal(submitted.DefinitionId, version.DefinitionId);
        Assert.Equal("1.0.0", version.Version);
        Assert.Equal("root", draft.State.RootActivity?.NodeId);
        Assert.Equal("root", version.State.RootActivity?.NodeId);
        Assert.NotNull(await ctx.WorkflowDefinitionDraftLayouts.FirstOrDefaultAsync(x => x.WorkflowDefinitionDraftId == submitted.DraftId));
        Assert.NotNull(await ctx.WorkflowDefinitionVersionLayouts.FirstOrDefaultAsync(x => x.WorkflowDefinitionVersionId == submitted.VersionId));
    }

    [Fact]
    public async Task Execute_rejects_root_activity_without_activity_version_id()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var state = new WorkflowDefinitionState(
            Variables: [],
            RootActivity: new ActivityNode("root", string.Empty, [], []),
            Inputs: [],
            Outputs: [],
            StrategyOptions: null);

        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ISubmitWorkflowDefinitionCommand>();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            command.Execute(WorkflowsDesignTestHost.TestOperationKey, "Demo", null, state));

        Assert.Contains("activity version id", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Execute_rejects_missing_root_activity()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var state = new WorkflowDefinitionState([], null, [], [], null);

        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ISubmitWorkflowDefinitionCommand>();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            command.Execute(WorkflowsDesignTestHost.TestOperationKey, "Demo", null, state));

        Assert.Contains("root activity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Workflows.Design.Persistence.Core.Entities.WorkflowDefinitionDraft> LoadDraft(
        WorkflowsDesignTestHost host,
        WorkflowsDesignDbContext ctx,
        string draftId)
    {
        var draft = await ctx.WorkflowDefinitionDrafts.FirstAsync(x => x.Id == draftId);
        await ((IInlineEventPublisher)host.EventPublisher).Publish(new OnEntityLoading(ctx, draft));
        return draft;
    }

    private static async Task<Workflows.Design.Persistence.Core.Entities.WorkflowDefinitionVersion> LoadVersion(
        WorkflowsDesignTestHost host,
        WorkflowsDesignDbContext ctx,
        string versionId)
    {
        var version = await ctx.WorkflowDefinitionVersions.FirstAsync(x => x.Id == versionId);
        await ((IInlineEventPublisher)host.EventPublisher).Publish(new OnEntityLoading(ctx, version));
        return version;
    }
}

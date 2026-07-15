using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class WorkflowDefinitionSoftDeleteTests
{
    [Fact]
    public void From_preserves_soft_delete_metadata_round_trip()
    {
        var deletedAt = DateTimeOffset.UtcNow;
        var source = new WorkflowDefinition
        {
            Id = "def-1",
            Name = "Deleted",
            Description = "desc",
            DeletedAt = deletedAt,
            DeletedReason = "gone",
        };

        var copy = WorkflowDefinition.From(source);

        Assert.Equal(source.Id, copy.Id);
        Assert.Equal(source.Name, copy.Name);
        Assert.Equal(source.Description, copy.Description);
        Assert.Equal(deletedAt, copy.DeletedAt);
        Assert.Equal("gone", copy.DeletedReason);
    }

    [Fact]
    public void From_leaves_soft_delete_metadata_unset_for_non_deleted_source()
    {
        var source = new WorkflowDefinition { Id = "def-2", Name = "Active" };

        var copy = WorkflowDefinition.From(source);

        Assert.Null(copy.DeletedAt);
        Assert.Null(copy.DeletedReason);
    }

    [Fact]
    public void From_leaves_soft_delete_metadata_unset_for_non_entity_source()
    {
        // Both real callers pass a read-model (WorkflowDefinitionModel), not a WorkflowDefinition entity,
        // so `source is WorkflowDefinition` is false and the soft-delete copy branch is skipped. This pins
        // the branch the callers actually hit — the entity-source tests above cover the copy branch.
        var source = new WorkflowDefinitionModel("def-3", "Model", "desc");

        var copy = WorkflowDefinition.From(source);

        Assert.Equal("def-3", copy.Id);
        Assert.Null(copy.DeletedAt);
        Assert.Null(copy.DeletedReason);
    }

    [Fact]
    public async Task Soft_deleted_definitions_are_excluded_from_active_query_and_included_in_deleted_query()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var activeId = await Submit(host, "Active");
        var deletedId = await Submit(host, "Deleted");

        await SoftDelete(host, deletedId);

        await using var ctx = host.CreateContext();
        var active = await ctx.WorkflowDefinitions
            .Where(x => x.DeletedAt == null)
            .Select(x => x.Id)
            .ToArrayAsync();
        var deleted = await ctx.WorkflowDefinitions
            .Where(x => x.DeletedAt != null)
            .Select(x => x.Id)
            .ToArrayAsync();

        Assert.Contains(activeId, active);
        Assert.DoesNotContain(deletedId, active);
        Assert.Equal(new[] { deletedId }, deleted);
    }

    [Fact]
    public async Task Restore_clears_soft_delete_metadata()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var definitionId = await Submit(host, "Restored");
        await SoftDelete(host, definitionId);

        await using (var ctx = host.CreateContext())
        {
            var definition = await ctx.WorkflowDefinitions.FirstAsync(x => x.Id == definitionId);
            definition.DeletedAt = null;
            definition.DeletedReason = null;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = host.CreateContext())
        {
            var definition = await ctx.WorkflowDefinitions.FirstAsync(x => x.Id == definitionId);
            Assert.Null(definition.DeletedAt);
            Assert.Null(definition.DeletedReason);
        }
    }

    [Fact]
    public async Task Permanent_delete_removes_definition_dependents_transactionally_after_soft_delete()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var definitionId = await Submit(host, "Permanent");
        await SoftDelete(host, definitionId);

        await using (var ctx = host.CreateContext())
        await using (var transaction = await ctx.Database.BeginTransactionAsync())
        {
            var draftIds = await ctx.WorkflowDefinitionDrafts
                .Where(x => x.WorkflowDefinitionId == definitionId)
                .Select(x => x.Id)
                .ToArrayAsync();
            var versionIds = await ctx.WorkflowDefinitionVersions
                .Where(x => x.DefinitionId == definitionId)
                .Select(x => x.Id)
                .ToArrayAsync();

            await ctx.WorkflowDefinitionDraftLayouts
                .Where(x => draftIds.Contains(x.WorkflowDefinitionDraftId))
                .ExecuteDeleteAsync();
            await ctx.WorkflowDefinitionVersionLayouts
                .Where(x => versionIds.Contains(x.WorkflowDefinitionVersionId))
                .ExecuteDeleteAsync();
            await ctx.WorkflowDefinitionDrafts
                .Where(x => x.WorkflowDefinitionId == definitionId)
                .ExecuteDeleteAsync();
            await ctx.WorkflowDefinitionVersions
                .Where(x => x.DefinitionId == definitionId)
                .ExecuteDeleteAsync();
            await ctx.WorkflowDefinitions
                .Where(x => x.Id == definitionId)
                .ExecuteDeleteAsync();

            await transaction.CommitAsync();
        }

        await using (var ctx = host.CreateContext())
        {
            Assert.False(await ctx.WorkflowDefinitions.AnyAsync(x => x.Id == definitionId));
            Assert.False(await ctx.WorkflowDefinitionDrafts.AnyAsync(x => x.WorkflowDefinitionId == definitionId));
            Assert.False(await ctx.WorkflowDefinitionVersions.AnyAsync(x => x.DefinitionId == definitionId));
            Assert.False(await ctx.WorkflowDefinitionDraftLayouts.AnyAsync());
            Assert.False(await ctx.WorkflowDefinitionVersionLayouts.AnyAsync());
        }
    }

    private static async Task<string> Submit(WorkflowsDesignTestHost host, string name)
    {
        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<ISubmitWorkflowDefinitionCommand>();
        var submitted = await command.Execute(name, null, new WorkflowDefinitionState(
            Variables: [],
            RootActivity: new ActivityNode("root", "activity-version-1", [], []),
            Inputs: [],
            Outputs: [],
            StrategyOptions: null));

        return submitted.DefinitionId;
    }

    private static async Task SoftDelete(WorkflowsDesignTestHost host, string definitionId)
    {
        await using var ctx = host.CreateContext();
        var definition = await ctx.WorkflowDefinitions.FirstAsync(x => x.Id == definitionId);
        definition.DeletedAt = DateTimeOffset.UtcNow;
        definition.DeletedReason = "test";
        await ctx.SaveChangesAsync();
    }
}

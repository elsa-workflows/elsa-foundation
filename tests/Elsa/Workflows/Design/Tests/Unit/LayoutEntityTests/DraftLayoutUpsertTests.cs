using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.LayoutEntityTests;

/// <summary>
/// Pins the review-fix layout-origination behaviour on the EF Core provider:
/// <list type="bullet">
/// <item><c>AddWorkflowDefinition</c> creates the empty <c>WorkflowDefinitionDraftLayout</c> row at
///       origin, so a later layout submit always has a row to upsert into.</item>
/// <item><c>UpdateDraft</c> UPSERTS: when a draft has no layout row (older origination paths that
///       never created one), a non-empty submitted layout creates the row rather than being
///       silently dropped.</item>
/// </list>
/// </summary>
public sealed class DraftLayoutUpsertTests
{
    private static readonly WorkflowDefinitionState EmptyState = new([], null, [], [], null);

    [Fact]
    public async Task AddWorkflowDefinition_creates_an_empty_layout_row_at_origin()
    {
        using var host = WorkflowsDesignTestHost.Create();

        await AddDefinitionWithDraft(host, definitionId: "def-1", draftId: "draft-1");

        await using var ctx = host.CreateContext();
        var layout = await ctx.WorkflowDefinitionDraftLayouts
            .FirstOrDefaultAsync(l => l.WorkflowDefinitionDraftId == "draft-1");

        Assert.NotNull(layout);
        Assert.Empty(layout!.Records);
    }

    [Fact]
    public async Task AddWorkflowDefinition_persists_the_initial_layout_at_origin()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var records = new[] { new DesignMetadataRecord("node-1", 10, 20, 100, 80) };

        using (var scope = host.Services.CreateScope())
        {
            var add = scope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionCommand>();
            await add.Execute(
                WorkflowsDesignTestHost.TestOperationKey,
                new WorkflowDefinition { Id = "def-1", Name = "def-1" },
                new WorkflowDefinitionDraft { Id = "draft-1", WorkflowDefinitionId = "def-1", State = EmptyState },
                records,
                CancellationToken.None);
        }

        await using var ctx = host.CreateContext();
        var layout = await ctx.WorkflowDefinitionDraftLayouts
            .FirstAsync(item => item.WorkflowDefinitionDraftId == "draft-1");
        Assert.Equal(records, layout.Records);
    }

    [Fact]
    public async Task UpdateDraft_upserts_a_layout_row_when_the_draft_has_none()
    {
        using var host = WorkflowsDesignTestHost.Create();
        await AddDefinitionWithDraft(host, definitionId: "def-1", draftId: "draft-1");

        // Simulate an older origination path that left the draft without a layout row: delete the
        // one AddWorkflowDefinition created, so UpdateDraft must take the create-on-missing branch.
        await using (var ctx = host.CreateContext())
        {
            var existing = await ctx.WorkflowDefinitionDraftLayouts
                .Where(l => l.WorkflowDefinitionDraftId == "draft-1")
                .ToListAsync();
            ctx.WorkflowDefinitionDraftLayouts.RemoveRange(existing);
            await ctx.SaveChangesAsync();
        }

        var submitted = new DesignMetadataRecord[] { new("n1", 10, 20, 100, 50) };
        using (var scope = host.Services.CreateScope())
        {
            var update = scope.ServiceProvider.GetRequiredService<IUpdateDraftCommand>();
            await update.Execute(WorkflowsDesignTestHost.TestOperationKey, new UpdateDraftRequest("draft-1", EmptyState, submitted));
        }

        await using var readCtx = host.CreateContext();
        var layout = await readCtx.WorkflowDefinitionDraftLayouts
            .FirstOrDefaultAsync(l => l.WorkflowDefinitionDraftId == "draft-1");

        Assert.NotNull(layout);
        Assert.Equal(submitted.Single(), layout!.Records.Single());
    }

    private static async Task AddDefinitionWithDraft(WorkflowsDesignTestHost host, string definitionId, string draftId)
    {
        using var scope = host.Services.CreateScope();
        var add = scope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionCommand>();
        await add.Execute(
            WorkflowsDesignTestHost.TestOperationKey,
            new WorkflowDefinition { Id = definitionId, Name = definitionId },
            new WorkflowDefinitionDraft { Id = draftId, WorkflowDefinitionId = definitionId, State = EmptyState },
            CancellationToken.None);
    }
}

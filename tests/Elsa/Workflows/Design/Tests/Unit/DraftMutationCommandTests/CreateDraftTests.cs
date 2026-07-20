using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.DraftMutationCommandTests;

/// <summary>
/// <see cref="ICreateDraftCommand"/> is the single Draft-origination path (the former
/// <c>DraftMutationPipeline.ExecuteCreation</c> shell is absorbed here inline — FR-003, FR-007).
/// A fresh create persists the Draft together with its layout sibling under the command's own
/// shell and publishes the single origination event <c>OnDraftCreated</c> with <c>SourceVersionId</c>
/// null — the fresh complement to the clone path, which carries the source version id
/// (see <see cref="CloneDraftFromVersionTests"/>). The empty validation sibling + <c>OnDraftValidated</c>
/// outcome are exercised by <see cref="ValidationSiblingPersistenceTests"/>.
/// </summary>
public sealed class CreateDraftTests
{
    [Fact]
    public async Task CreateDraft_persists_the_draft_with_a_layout_sibling()
    {
        using var host = WorkflowsDesignTestHost.Create();
        await host.EnsureDefinition("wf-1");

        string draftId;
        using (var scope = host.Services.CreateScope())
            draftId = await scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>().Execute(WorkflowsDesignTestHost.TestOperationKey, "wf-1");

        using var ctx = host.CreateContext();
        var draft = await ctx.WorkflowDefinitionDrafts.FirstOrDefaultAsync(d => d.Id == draftId);
        var layout = await ctx.WorkflowDefinitionDraftLayouts.FirstOrDefaultAsync(l => l.WorkflowDefinitionDraftId == draftId);

        Assert.NotNull(draft);
        Assert.NotNull(layout);
    }

    [Fact]
    public async Task CreateDraft_publishes_OnDraftCreated_with_no_source_version_id()
    {
        using var host = WorkflowsDesignTestHost.Create();
        await host.EnsureDefinition("wf-1");

        string draftId;
        using (var scope = host.Services.CreateScope())
            draftId = await scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>().Execute(WorkflowsDesignTestHost.TestOperationKey, "wf-1");

        var created = host.EventPublisher.LastOf<OnDraftCreated>();
        Assert.NotNull(created);
        Assert.Equal(draftId, created!.DraftId);
        Assert.Equal("wf-1", created.WorkflowDefinitionId);
        // Fresh origination — the clone path is the only one that carries a source version id.
        Assert.Null(created.SourceVersionId);
    }
}

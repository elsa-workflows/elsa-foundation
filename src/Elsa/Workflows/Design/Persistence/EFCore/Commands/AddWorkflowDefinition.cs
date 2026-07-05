using Elsa.Primitives.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;


public sealed class AddWorkflowDefinition(IIdentityGenerator identityGenerator, IDbContextFactory<WorkflowsDesignDbContext> factory) : IAddWorkflowDefinitionCommand
{
    public async Task Execute(WorkflowDefinition workflowDefinition, WorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);

        // Create the empty layout sibling alongside the draft (mirrors SubmitWorkflowDefinition) so
        // the draft has a layout row from origin — otherwise a later layout submit has no row to
        // upsert into on providers that require one.
        var draftLayout = WorkflowDefinitionDraftLayout.CreateFor(identityGenerator, draft.Id);

        await dbContext.WorkflowDefinitions.AddAsync(workflowDefinition, cancellationToken);
        await dbContext.WorkflowDefinitionDrafts.AddAsync(draft, cancellationToken);
        await dbContext.WorkflowDefinitionDraftLayouts.AddAsync(draftLayout, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

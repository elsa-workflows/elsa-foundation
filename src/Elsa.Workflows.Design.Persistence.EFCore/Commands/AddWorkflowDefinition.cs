using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;


public sealed class AddWorkflowDefinition(IDbContextFactory<WorkflowsDesignDbContext> factory) : IAddWorkflowDefinitionCommand
{
    public async Task Execute(WorkflowDefinition workflowDefinition, WorkflowDefinitionDraft draft, CancellationToken cancellationToken)
    {
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);

        await dbContext.WorkflowDefinitions.AddAsync(workflowDefinition, cancellationToken);
        await dbContext.WorkflowDefinitionDrafts.AddAsync(draft, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

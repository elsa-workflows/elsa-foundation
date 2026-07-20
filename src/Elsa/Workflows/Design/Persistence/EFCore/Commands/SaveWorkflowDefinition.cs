using Elsa.Persistence.Core.Design;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EFCore.Commands;

public sealed class SaveWorkflowDefinition(IDbContextFactory<WorkflowsDesignDbContext> contextFactory)
    : ISaveWorkflowDefinitionCommand
{
    public async Task Execute(
        DesignOperationKey operationKey,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.WorkflowDefinitions.Update(definition);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

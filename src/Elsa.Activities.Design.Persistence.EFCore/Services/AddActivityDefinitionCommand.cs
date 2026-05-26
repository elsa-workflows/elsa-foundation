using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Activities.Design.Persistence.EFCore.Services;

public sealed class AddActivityDefinitionCommand(IDbContextFactory<ActivitiesDesignDbContext> factory) : IAddActivityDefinitionCommand
{
    public async Task Execute(ActivityDefinition definition, ActivityDefinitionVersion version, CancellationToken cancellationToken)
    {
        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);

        await dbContext.ActivityDefinitions.AddAsync(definition, cancellationToken);
        await dbContext.ActivityDefinitionVersions.AddAsync(version, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

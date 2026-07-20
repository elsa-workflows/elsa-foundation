using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.Core.Design;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Activities.Design.Persistence.EFCore.Services;

/// <summary>
/// Temporary EF oracle implementation of the literal activity-version command. It accepts the caller's
/// operation key solely to preserve the provider-neutral contract; replay persistence remains Groundwork-only.
/// </summary>
public sealed class AddActivityDefinitionVersionCommand(IDbContextFactory<ActivitiesDesignDbContext> factory)
    : IAddActivityDefinitionVersionCommand
{
    public async Task<ActivityDefinitionVersionAdded> Execute(
        DesignOperationKey operationKey,
        ActivityDefinitionVersion version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentNullException.ThrowIfNull(version);

        await using var dbContext = await factory.CreateDbContextAsync(cancellationToken);
        await dbContext.ActivityDefinitionVersions.AddAsync(version, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ActivityDefinitionVersionAdded(version.DefinitionId, version.Id, version.Version, version.Hash);
    }
}

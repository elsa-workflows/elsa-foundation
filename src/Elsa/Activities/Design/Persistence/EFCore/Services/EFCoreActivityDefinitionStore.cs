using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.EFCore.Services;
using Elsa.Primitives.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Activities.Design.Persistence.EFCore.Services;

/// <summary>
/// EF Core implementation of <see cref="IActivityDefinitionStore"/>. Translates the named operations
/// into the closed <see cref="Query{TEntity}"/> spec executed by <see cref="EFCoreReadStore{TDbContext,TEntity}"/>.
/// </summary>
public sealed class EFCoreActivityDefinitionStore(IDbContextFactory<ActivitiesDesignDbContext> dbContextFactory, IServiceProvider serviceProvider)
    : EFCoreReadStore<ActivitiesDesignDbContext, ActivityDefinition>(dbContextFactory, serviceProvider), IActivityDefinitionStore
{
    public async Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default)
        => await FirstOrDefaultAsync(Query<ActivityDefinition>.Where(x => x.Id, QueryOp.Equal, id), cancellationToken: cancellationToken)
           ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinition), id);

    public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(filter.ToQuery(), cancellationToken: cancellationToken);

    public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
        => QueryAsync(filter.ToQuery(), cancellationToken: cancellationToken);

    public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(
            Query<ActivityDefinition>.Where(x => x.Id, QueryOp.Equal, id)
                .Or(x => x.ActivityTypeKey, QueryOp.Equal, activityTypeKey),
            cancellationToken: cancellationToken);

    public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default)
        => AnyAsync(Query<ActivityDefinition>.Where(x => x.ActivityTypeKey, QueryOp.Equal, activityTypeKey), cancellationToken);
}

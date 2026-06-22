using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.EFCore.Services;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Activities.Design.Persistence.EFCore.Services;

/// <summary>
/// EF Core implementation of <see cref="IActivityDefinitionVersionStore"/>. Translates the named
/// operations into the closed <see cref="Query{TEntity}"/> spec executed by
/// <see cref="EFCoreReadStore{TDbContext,TEntity}"/>. The owning definition is loaded via an explicit
/// EF <c>Include</c>, which a non-relational provider would satisfy with a second aggregate read.
/// </summary>
public sealed class EFCoreActivityDefinitionVersionStore(IDbContextFactory<ActivitiesDesignDbContext> dbContextFactory, IServiceProvider serviceProvider)
    : EFCoreReadStore<ActivitiesDesignDbContext, ActivityDefinitionVersion>(dbContextFactory, serviceProvider), IActivityDefinitionVersionStore
{
    public async Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default)
        => await FirstOrDefaultAsync(ById(versionId), cancellationToken: cancellationToken)
           ?? throw new InvalidOperationException($"Entity '{typeof(ActivityDefinitionVersion)}' with id '{versionId}' cannot be found");

    public async Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default)
        => await FirstOrDefaultAsync(ById(versionId), include: q => q.Include(x => x.Definition), cancellationToken: cancellationToken)
           ?? throw new ArgumentException($"Activity definition version with id '{versionId}' does not exist");

    public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
        => FirstOrDefaultAsync(
            Query<ActivityDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId)
                .And(x => x.SemVerSortKey, QueryOp.Equal, semVerSortKey),
            cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
        => await QueryAsync(Query<ActivityDefinitionVersion>.Where(x => x.DefinitionId, QueryOp.Equal, definitionId), cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default)
        => await QueryAsync(Query<ActivityDefinitionVersion>.All(), cancellationToken: cancellationToken);

    private static Query<ActivityDefinitionVersion> ById(string versionId)
        => Query<ActivityDefinitionVersion>.Where(x => x.Id, QueryOp.Equal, versionId);
}

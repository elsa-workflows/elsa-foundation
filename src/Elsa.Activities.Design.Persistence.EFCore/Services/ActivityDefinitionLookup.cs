using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.EFCore.DbContext;
using Elsa.Persistence.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Activities.Design.Persistence.EFCore.Services;

public sealed class ActivityDefinitionLookup(
    IQueries<ActivityDefinitionVersion> versionQueries,
    IQueries<ActivityDefinition> defQueries,
    IDbContextFactory<ActivitiesDesignDbContext> contextFactory) : IActivityDefinitionLookup
{
    public async Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default)
    {
        var idFilter = new ActivityDefinitionFilter
        {
            Id = idOrActivityTypeKey
        };
        var typeKeyFilter = new ActivityDefinitionFilter
        {
            ActivityTypeKey = idOrActivityTypeKey
        };

        var idTask = defQueries.Find(idFilter, cancellationToken);
        var typeKeyTask = defQueries.Find(typeKeyFilter, cancellationToken);

        var defById = await idTask;
        var defByTypeKey = await typeKeyTask;

        return defById ?? defByTypeKey ?? throw new ArgumentException($"Activity definition could not be found for activity-type-key/id '{idOrActivityTypeKey}'");
    }

    public async Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
    {
        var result = await versionQueries.Get(versionId, cancellationToken);
        return result;
    }

    public async Task<IEnumerable<IActivityDefinition>> ListDefinitions(
        string? id = null,
        string? category = null,
        string? searchTerm = null,
        string? displayName = null,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await contextFactory.CreateDbContextAsync(cancellationToken);

        // LEFT JOIN reconciliation-state; exclude rows whose state has RemovedAt set.
        // Rows without a sibling (admin-created) pass through.
        var query = from def in ctx.ActivityDefinitions
                    join state in ctx.ActivityDefinitionReconciliationStates
                        on def.Id equals state.ActivityDefinitionId into states
                    from state in states.DefaultIfEmpty()
                    where state == null || state.RemovedAt == null
                    select def;

        if (id != null)
            query = query.Where(x => x.Id == id);
        if (category != null)
            query = query.Where(x => x.Category == category);
        if (displayName != null)
            query = query.Where(x => x.DisplayName == displayName);
        if (description != null)
            query = query.Where(x => x.Description!.Contains(description));
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm;
            query = query.Where(x =>
                (x.DisplayName != null && x.DisplayName.Contains(term))
                || (x.Category != null && x.Category.Contains(term))
                || (x.Description != null && x.Description.Contains(term))
                || x.Id.Contains(term));
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ActivityDefinitionVersionInfo>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
    {
        var filter = new ActivityDefinitionVersionFilter
        {
            DefinitionId = definitionId
        };

        var result = await versionQueries.Query<ActivityDefinitionVersionInfo>(
            filter,
            (e) => new(e.Id, e.Version, e.CreatedAt, e.ExecutionType),
            cancellationToken
        );

        return result;
    }
}

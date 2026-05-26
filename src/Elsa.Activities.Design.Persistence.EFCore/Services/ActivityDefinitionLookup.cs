using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Persistence.Core;

namespace Elsa.Activities.Design.Persistence.EFCore.Services;

public sealed class ActivityDefinitionLookup(IQueries<ActivityDefinitionVersion> versionQueries, IQueries<ActivityDefinition> defQueries) : IActivityDefinitionLookup
{
    public async Task<IActivityDefinition> GetDefinition(string idOrUniqueName, CancellationToken cancellationToken = default)
    {
        var idFilter = new ActivityDefinitionFilter
        {
            Id = idOrUniqueName
        };
        var uniqueNameFilter = new ActivityDefinitionFilter
        {
            UniqueName = idOrUniqueName
        };

        var idTask = defQueries.Find(idFilter, cancellationToken);
        var uniqueNameTask = defQueries.Find(uniqueNameFilter, cancellationToken);

        var defById = await idTask;
        var defByUniqueName = await uniqueNameTask;

        return defById ?? defByUniqueName ?? throw new ArgumentException($"Activity definition could not be found for name/id '{idOrUniqueName}'");
    }

    public async Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
    {
        var result = await versionQueries.Get(versionId, cancellationToken);
        return result;
    }

    public async Task<IEnumerable<IActivityDefinition>> ListDefinitions(string? category = null, bool? isBrowsable = null, CancellationToken cancellationToken = default)
    {
        var filter = new ActivityDefinitionFilter
        {
            Category = category,
            IsBrowsable = isBrowsable
        };

        var result = await defQueries.Query(filter, cancellationToken);
        return result;
    }

    public async Task<IEnumerable<ActivityDefinitionVersionInfo>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
    {
        var filter = new ActivityDefinitionVersionFilter
        {
            DefinitionId = definitionId
        };

        var result = await versionQueries.Query<ActivityDefinitionVersionInfo>(
            filter,
            (e) => new(e.Id, e.Version, e.CreatedAt, e.Kind),
            cancellationToken
        );

        return result;
    }
}

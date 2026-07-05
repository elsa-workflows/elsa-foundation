using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Exceptions;

namespace Elsa.Activities.Design.Persistence.Core.Services;

public sealed class ActivityDefinitionLookup(
    IActivityDefinitionVersionStore versionStore,
    IActivityDefinitionStore definitionStore) : IActivityDefinitionLookup
{
    public async Task<IActivityDefinition> GetDefinition(string idOrActivityTypeKey, CancellationToken cancellationToken = default)
    {
        return await definitionStore.FindByIdOrActivityTypeKeyAsync(idOrActivityTypeKey, idOrActivityTypeKey, cancellationToken)
            ?? throw new EntityNotFoundException($"Activity definition could not be found for activity-type-key/id '{idOrActivityTypeKey}'");
    }

    public async Task<IActivityDefinitionVersion> GetVersion(string versionId, CancellationToken cancellationToken = default)
    {
        var result = await versionStore.GetAsync(versionId, cancellationToken);
        return result;
    }

    public async Task<IEnumerable<IActivityDefinition>> ListDefinitions(
        string? id = null,
        string? category = null,
        string? searchTerm = null,
        string? displayName = null,
        string? description = null,
        bool? tenantAgnostic = null,
        CancellationToken cancellationToken = default)
    {
        var filter = new ActivityDefinitionFilter
        {
            Id = id,
            Category = category,
            SearchTerm = searchTerm,
            DisplayName = displayName,
            Description = description,
            TenantAgnostic = tenantAgnostic
        };

        return await definitionStore.ListAsync(filter, cancellationToken);
    }

    public async Task<IEnumerable<ActivityDefinitionVersionSummary>> ListVersions(string definitionId, CancellationToken cancellationToken = default)
    {
        var versions = await versionStore.ListByDefinitionAsync(definitionId, cancellationToken);
        return versions.Select(e => new ActivityDefinitionVersionSummary(e.Id, e.Version, e.CreatedAt, e.ExecutionType)).ToArray();
    }
}

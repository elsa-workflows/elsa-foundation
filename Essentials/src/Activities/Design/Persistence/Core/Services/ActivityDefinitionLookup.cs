using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Exceptions;

namespace Elsa.Activities.Design.Persistence.Core.Services;

public sealed class ActivityDefinitionLookup(
    IActivityDefinitionVersionStore versionStore,
    IActivityDefinitionStore definitionStore,
    IActivityDefinitionAuthoringStore? authoringStore,
    IActivityDefinitionVersionPublicationStore? publicationStore) : IActivityDefinitionLookup
{
    public ActivityDefinitionLookup(IActivityDefinitionVersionStore versionStore, IActivityDefinitionStore definitionStore)
        : this(versionStore, definitionStore, null, null) { }

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

    public async Task<IActivityDefinitionVersion?> FindVersion(string versionId, CancellationToken cancellationToken = default)
    {
        // The store's Get contract throws on a missing id (no nullable by-id find exists on the
        // store). Translate that single throw to null here so callers get the nullable outcome
        // without catching EntityNotFoundException themselves.
        try
        {
            return await versionStore.GetAsync(versionId, cancellationToken);
        }
        catch (EntityNotFoundException)
        {
            return null;
        }
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

    public async Task<ActivityDefinitionVersionSummary?> FindRecommendedVersion(string definitionId, CancellationToken cancellationToken = default)
    {
        if (authoringStore is null || publicationStore is null ||
            !(await definitionStore.ListAsync(new ActivityDefinitionFilter { Id = definitionId }, cancellationToken)).Any())
            return null;

        var state = await authoringStore.FindAsync(definitionId, cancellationToken);
        if (state?.RecommendedVersionId is not { } versionId)
            return null;
        var publication = await publicationStore.FindAsync(versionId, cancellationToken);
        if (publication is null || publication.DefinitionId != definitionId || publication.Lifecycle != ActivityDefinitionVersionLifecycle.Active)
            return null;
        Entities.ActivityDefinitionVersion version;
        try { version = await versionStore.GetAsync(versionId, cancellationToken); }
        catch (EntityNotFoundException) { return null; }
        return version.DefinitionId == definitionId && version.Version == publication.Version
            ? new(version.Id, version.Version, version.CreatedAt, version.ExecutionType)
            : null;
    }
}

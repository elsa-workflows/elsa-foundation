using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Exceptions;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>v2 Groundwork activity-definition-version store using explicit public query rows.</summary>
public sealed class GroundworkActivityDefinitionVersionStore(
    GroundworkV2ActivityDesignStore store,
    IActivityDefinitionStore definitions,
    IPayloadSerializer payloadSerializer) : IActivityDefinitionVersionStore
{
    private readonly JsonSerializerOptions _jsonOptions =
        GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer);

    public async Task<ActivityDefinitionVersion> GetAsync(
        string versionId,
        CancellationToken cancellationToken = default)
        => Deserialize(await store.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, versionId, cancellationToken))
           ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinitionVersion), versionId);

    public async Task<ActivityDefinitionVersion> GetWithDefinitionAsync(
        string versionId,
        CancellationToken cancellationToken = default)
    {
        var version = await GetAsync(versionId, cancellationToken);
        version.Definition = await definitions.GetAsync(version.DefinitionId, cancellationToken);
        return version;
    }

    public async Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(
        string definitionId,
        string semVerSortKey,
        CancellationToken cancellationToken = default)
    {
        var result = await store.FirstOrDefaultAsync(new(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            ActivitiesDesignStorageManifest.FindActivityDefinitionVersionByDefinitionAndSortKeyQuery,
            [
                ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField, definitionId)),
                ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField, semVerSortKey))
            ],
            [new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField)]),
            cancellationToken);
        return Deserialize(result);
    }

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default)
        => QueryAsync([
            ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField, definitionId))
        ], cancellationToken);

    public async Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(
        IEnumerable<string> definitionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitionIds);
        var versions = new List<ActivityDefinitionVersion>();
        foreach (var batch in GroundworkMembershipBatches.Create(definitionIds))
        {
            versions.AddRange(await QueryAsync([
                ActivityDesignQueryClause.Of(ActivityDesignQueryComparison.In(
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField,
                    batch.Cast<object?>()))
            ], cancellationToken));
        }

        return versions;
    }

    public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(
        CancellationToken cancellationToken = default)
        => QueryAsync([], cancellationToken);

    private async Task<IReadOnlyList<ActivityDefinitionVersion>> QueryAsync(
        IReadOnlyList<ActivityDesignQueryClause> clauses,
        CancellationToken cancellationToken)
    {
        var query = new ActivityDesignQuery(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionVersionsByDefinitionQuery,
            clauses,
            [
                new ActivityDesignQueryOrder(
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField),
                new ActivityDesignQueryOrder(
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField),
                new ActivityDesignQueryOrder(ActivitiesDesignStorageManifest.IdField)]);
        var documents = await ActivityDesignQueryPager.QueryAllAsync(
            store,
            query.DocumentKind,
            query.Identity,
            query.Clauses,
            query.Order,
            cancellationToken: cancellationToken);
        return documents.Select(Deserialize).Where(version => version is not null).Cast<ActivityDefinitionVersion>().ToArray();
    }

    private ActivityDefinitionVersion? Deserialize(ActivityDesignDocument? document)
        => document is null
            ? null
            : JsonSerializer.Deserialize<GroundworkV2ActivityDesignDocument<ActivityDefinitionVersion>>(
                document.ContentJson, _jsonOptions)?.Entity;
}

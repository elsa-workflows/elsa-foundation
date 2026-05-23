using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.OrderDefinitions;
using Elsa.Persistence.Core;
using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Persistence.Core.Filters;

namespace Elsa.Activities.Design.Persistence.Core.Extensions;

public static class ActivityDefinitionVersionQueryExtensions
{
    public static async Task<ActivityDefinitionVersion> GetVersionInlcudingDefinition(this IQueries<ActivityDefinitionVersion> queries, string versionId, CancellationToken cancellationToken)
    {
        var filter = new ActivityDefinitionVersionFilter { Id = versionId };
        var result = await queries.Find(
            filter,
            include: entity => entity.Definition,
            cancellationToken
        );

        return result ?? throw new ArgumentException($"Workflow definition version with id '{versionId}' does not exist");
    }

    public static async Task<ActivityDefinitionVersion?> FindLastVersion(this IQueries<ActivityDefinitionVersion> queries, string activityDefinitionId, CancellationToken cancellationToken)
    {
        var filter = new ActivityDefinitionVersionFilter
        {
            DefinitionId = activityDefinitionId
        };
        var order = new OrderDefinition<ActivityDefinitionVersion, int>(e => e.Version, OrderDirection.Descending);
        var result = await queries.Query(filter, order, cancellationToken);
        return result.FirstOrDefault();
    }

    public static Task<IEnumerable<ActivityDefinitionVersion>> FindVersions(this IQueries<ActivityDefinitionVersion> queries, string activityDefinitionId, CancellationToken cancellationToken)
    {
        var filter = new ActivityDefinitionVersionFilter
        {
            DefinitionId = activityDefinitionId
        };
        var order = new ActivityVersionOrderDefinition();
        return queries.Query(filter, order, cancellationToken);
    }
}

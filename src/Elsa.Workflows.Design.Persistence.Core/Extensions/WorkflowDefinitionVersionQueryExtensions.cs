using Elsa.Activities.Design.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;

namespace Elsa.Workflows.Design.Persistence.Core.Extensions;

public static class WorkflowDefinitionVersionQueryExtensions
{
    public static async Task<WorkflowDefinitionVersion> GetVersionIncludingDefinition(this IQueries<WorkflowDefinitionVersion> queries, string versionId, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionVersionFilter { Id = versionId };
        var result = await queries.Find(
            filter,
            include: entity => entity.Definition,
            cancellationToken
        );

        return result ?? throw new ArgumentException($"Workflow definition version with id '{versionId}' does not exist");
    }

    public static async Task<WorkflowDefinitionVersion?> FindLastVersion(this IQueries<WorkflowDefinitionVersion> queries, string workflowDefinitionId, CancellationToken cancellationToken)
    {
        var filter = new WorkflowDefinitionVersionFilter
        {
            DefinitionId = workflowDefinitionId
        };
        var orderDefinition = new OrderDefinition<WorkflowDefinitionVersion, int>(e => e.Version, OrderDirection.Descending);
        var result = await queries.Query(filter, orderDefinition, cancellationToken);
        return result.FirstOrDefault();
    }
}

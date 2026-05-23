using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;

namespace Elsa.Workflows.Design.Persistence.Core.Extensions
{
    public static class WorkflowDefinitionQueryExtensions
    {
        public static async Task<WorkflowDefinition> GetDefinitionInlcudingDraft(this IQueries<WorkflowDefinition> queries, string workflowDefinitionId, CancellationToken cancellationToken)
        {
            var filter = new WorkflowDefinitionFilter { Id = workflowDefinitionId };
            var result = await queries.Find(
                filter,
                include: entity => entity.Draft,
                cancellationToken
            );

            return result ?? throw new ArgumentException($"Workflow definition with id '{workflowDefinitionId}' does not exist");
        }

        /// <inheritdoc />
        public static async Task<long> CountDistinct(this IQueries<WorkflowDefinition> queries, CancellationToken cancellationToken = default)
        {
            return await queries.Count(x => true, x => x.Id, cancellationToken);
        }

        /// <inheritdoc />
        public static async Task<bool> IsNameUnique(this IQueries<WorkflowDefinition> queries, string name, string definitionId, CancellationToken cancellationToken = default)
        {
            var exists = await queries.Any(x => x.Name == name && x.Id != definitionId, cancellationToken);
            return !exists;
        }
    }
}

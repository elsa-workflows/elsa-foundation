using Elsa.Persistence.Core;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Workflows.Design.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;
using Open.Linq.AsyncExtensions;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services
{
    public sealed class EFCoreWorkflowDefinitionDeleteCommand(        
        IDeleteCommand<WorkflowDefinition> deleteCommand, 
        IQueries<WorkflowDefinition> queries) 
        : IWorkflowDefinitionDeleteCommand
    {

        /// <inheritdoc />
        public async Task<long> DeleteAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken)
        {
            var query = GetDeleteQuery(filter);
            var ids = await queries.QueryAsync(
                query, 
                selector: e => e.Id,
                cancellationToken: cancellationToken
            );
            
            return await deleteCommand.DeleteWhereAsync(
                x => ids.Contains(x.Id), 
                cancellationToken
            );
        }

        static Func<IQueryable<WorkflowDefinition>, IQueryable<WorkflowDefinition>> GetDeleteQuery(IWorkflowDefinitionFilter filter)
        {
            return queryable =>
            {
                var result = filter.Apply(queryable);
                if (filter.TenantAgnostic)
                {
                    result = result.IgnoreQueryFilters();
                }

                return result.DistinctBy(x => x.Id);
            };
        }
    }
}

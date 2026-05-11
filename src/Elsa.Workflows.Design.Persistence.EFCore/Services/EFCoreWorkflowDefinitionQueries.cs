using Elsa.Primitives.Extensions;
using Elsa.Primitives.Persistence;
using Elsa.Persistence.Core;
using Elsa.Workflows.Design.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Microsoft.EntityFrameworkCore;
using Open.Linq.AsyncExtensions;
using System.Diagnostics.CodeAnalysis;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services
{
    public sealed class EFCoreWorkflowDefinitionQueries(IQueries<WorkflowDefinition> queries, IDbContextFactory<WorkflowDefinitionDbContext> dbContextFactory) 
        : IWorkflowDefinitionQueries
    {
        /// <inheritdoc />
        public async Task<WorkflowDefinition?> FindAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
        {
            var orderBy = new WorkflowDefinitionOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Ascending);
            return await FindAsync(filter, orderBy, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<WorkflowDefinition?> FindAsync<TOrderBy>(IWorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, CancellationToken cancellationToken = default)
        {
            var query = GetQuery(filter);
            var result = await queries.QueryAsync(query, order, cancellationToken);
            return result.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<Page<WorkflowDefinition>> FindManyAsync(IWorkflowDefinitionFilter filter, PageArgs pageArgs, CancellationToken cancellationToken = default)
        {
            var orderBy = new WorkflowDefinitionOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Ascending);
            return await FindManyAsync(filter, orderBy, pageArgs, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<Page<WorkflowDefinition>> FindManyAsync<TOrderBy>(IWorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, PageArgs pageArgs, CancellationToken cancellationToken = default)
        {
            var count = await queries.QueryAsync(filter.Apply, cancellationToken).LongCount();

            IQueryable<WorkflowDefinition> query(IQueryable<WorkflowDefinition> queryable) => filter.Apply(queryable).OrderBy(order).Paginate(pageArgs);
            var results = await queries.QueryAsync(query, cancellationToken).ToList();

            return new(results, count);
        }    

        /// <inheritdoc />
        public async Task<IEnumerable<WorkflowDefinition>> FindManyAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
        {
            var orderBy = new WorkflowDefinitionOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Ascending);
            return await FindManyAsync(filter, orderBy, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IEnumerable<WorkflowDefinition>> FindManyAsync<TOrderBy>(IWorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, CancellationToken cancellationToken = default)
        {
            var query = GetQuery(filter);
            return await queries.QueryAsync(query, order, cancellationToken).ToList();
        }

        /// <inheritdoc />
        [RequiresUnreferencedCode("The method 'FindSummariesAsync' is used for serialization and requires unreferenced code to be preserved.")]
        public async Task<Page<WorkflowDefinitionSummary>> FindSummariesAsync(IWorkflowDefinitionFilter filter, PageArgs pageArgs, CancellationToken cancellationToken = default)
        {
            var orderBy = new WorkflowDefinitionOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Ascending);
            return await FindSummariesAsync(filter, orderBy, pageArgs, cancellationToken);
        }

        /// <inheritdoc />
        [RequiresUnreferencedCode("The method 'FindSummariesAsync' is used for serialization and requires unreferenced code to be preserved.")]
        public async Task<Page<WorkflowDefinitionSummary>> FindSummariesAsync<TOrderBy>(IWorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, PageArgs pageArgs, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var set = dbContext.WorkflowDefinitions
              .AsNoTracking()
              .AsQueryable()
              .OrderBy(order);

            var queryable = GetQuery(filter).Invoke(set);            
            var count = await queryable.LongCountAsync(cancellationToken);
            queryable = Paginate(queryable, pageArgs);

            var results = await queryable
                .Select(WorkflowDefinitionSummary.FromDefinitionExpression())
                .ToListAsync(cancellationToken);

            return Page.Of(results, count);
        }

        /// <inheritdoc />
        [RequiresUnreferencedCode("The method 'FindSummariesAsync' is used for serialization and requires unreferenced code to be preserved.")]
        public async Task<IEnumerable<WorkflowDefinitionSummary>> FindSummariesAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
        {
            var orderBy = new WorkflowDefinitionOrder<DateTimeOffset>(x => x.CreatedAt, OrderDirection.Ascending);
            return await FindSummariesAsync(filter, orderBy, cancellationToken);
        }

        /// <inheritdoc />
        [RequiresUnreferencedCode("The method 'FindSummariesAsync' is used for serialization and requires unreferenced code to be preserved.")]
        public async Task<IEnumerable<WorkflowDefinitionSummary>> FindSummariesAsync<TOrderBy>(IWorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var set = dbContext.WorkflowDefinitions
                .AsNoTracking()
                .AsQueryable()
                .OrderBy(order);

            var queryable = GetQuery(filter).Invoke(set);

            return await queryable
                .Select(WorkflowDefinitionSummary.FromDefinitionExpression())
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<WorkflowDefinition?> FindLastVersionAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken)
        {
            IQueryable<WorkflowDefinition> query(IQueryable<WorkflowDefinition> queryable) => filter.Apply(queryable).OrderByDescending(x => x.Version);

            var result = await queries.QueryAsync(query, cancellationToken);
            return result.FirstOrDefault();
        }


        /// <inheritdoc />
        public async Task<bool> AnyAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken = default)
        {
            return await queries.QueryAsync(filter.Apply, cancellationToken).Any();
        }

        /// <inheritdoc />
        public async Task<long> CountDistinctAsync(CancellationToken cancellationToken = default)
        {
            return await queries.CountAsync(x => true, x => x.DefinitionId, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> GetIsNameUnique(string name, string? definitionId = null, CancellationToken cancellationToken = default)
        {
            var exists = await queries.AnyAsync(x => x.Name == name && x.DefinitionId != definitionId, cancellationToken);
            return !exists;
        }
        
        private static IQueryable<WorkflowDefinition> Paginate(IQueryable<WorkflowDefinition> queryable, PageArgs? pageArgs)
        {
            if (pageArgs?.Offset != null) queryable = queryable.Skip(pageArgs.Offset.Value);
            if (pageArgs?.Limit != null) queryable = queryable.Take(pageArgs.Limit.Value);
            return queryable;
        }

        private static Func<IQueryable<WorkflowDefinition>, IQueryable<WorkflowDefinition>> GetQuery(IWorkflowDefinitionFilter filter)
        {
            return q =>
            {
                var result = filter.Apply(q);

                if (filter.TenantAgnostic)
                {
                    return result.IgnoreQueryFilters();
                }

                return result;
            };
        }
    }
}

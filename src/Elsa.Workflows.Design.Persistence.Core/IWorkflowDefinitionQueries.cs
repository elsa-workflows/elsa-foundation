using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Workflows.Design.Persistence.Core
{
    public interface IWorkflowDefinitionQueries
    {
        /// <summary>
        /// Finds a workflow definition using the specified filter.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The workflow definition.</returns>
        Task<WorkflowDefinition?> FindAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds a workflow definition using the specified filter and order.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="order">The order.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <typeparam name="TOrderBy">The type of the property to order by.</typeparam>
        /// <returns>The workflow definition.</returns>
        Task<WorkflowDefinition?> FindAsync<TOrderBy>(IWorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a paginated list of workflow definitions using the specified filter.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="pageArgs">The page arguments.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A paginated list of workflow definitions.</returns>
        Task<Page<WorkflowDefinition>> FindManyAsync(IWorkflowDefinitionFilter filter, PageArgs pageArgs, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a paginated list of workflow definitions using the specified filter and order.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="order">The order.</param>
        /// <param name="pageArgs">The page arguments.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <typeparam name="TOrderBy">The type of the property to order by.</typeparam>
        /// <returns>A paginated list of workflow definitions.</returns>
        Task<Page<WorkflowDefinition>> FindManyAsync<TOrderBy>(IWorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, PageArgs pageArgs, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a list of workflow definitions using the specified filter.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A list of workflow definitions.</returns>
        Task<IEnumerable<WorkflowDefinition>> FindManyAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a list of workflow definitions using the specified filter and order.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="order">The order.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <typeparam name="TOrderBy">The type of the property to order by.</typeparam>
        /// <returns>A list of workflow definitions.</returns>
        Task<IEnumerable<WorkflowDefinition>> FindManyAsync<TOrderBy>(IWorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a paginated list of workflow definition summaries using the specified filter.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="pageArgs">The page arguments.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A paginated list of workflow definition summaries.</returns>
        Task<Page<WorkflowDefinitionSummary>> FindSummariesAsync(IWorkflowDefinitionFilter filter, PageArgs pageArgs, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a paginated list of workflow definition summaries using the specified filter and order.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="order">The order.</param>
        /// <param name="pageArgs">The page arguments.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <typeparam name="TOrderBy">The type of the property to order by.</typeparam>
        /// <returns>A paginated list of workflow definition summaries.</returns>
        Task<Page<WorkflowDefinitionSummary>> FindSummariesAsync<TOrderBy>(IWorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, PageArgs pageArgs, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a list of workflow definition summaries using the specified filter.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A list of workflow definition summaries.</returns>
        Task<IEnumerable<WorkflowDefinitionSummary>> FindSummariesAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a list of workflow definition summaries using the specified filter and order.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="order">The order.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <typeparam name="TOrderBy">The type of the property to order by.</typeparam>
        /// <returns>A list of workflow definition summaries.</returns>
        Task<IEnumerable<WorkflowDefinitionSummary>> FindSummariesAsync<TOrderBy>(IWorkflowDefinitionFilter filter, WorkflowDefinitionOrder<TOrderBy> order, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the latest version of the workflow definition matching the specified filter.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The workflow definition.</returns>
        Task<WorkflowDefinition?> FindLastVersionAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken);

        /// <summary>
        /// Returns true if any workflow definition matches the specified filter.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>True if any workflow definition matches the specified filter.</returns>
        Task<bool> AnyAsync(IWorkflowDefinitionFilter filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns the number of logical workflow definitions.
        /// </summary>
        Task<long> CountDistinctAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a value indicating whether the specified name is unique.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="definitionId">The definition ID to exclude from the check.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task<bool> GetIsNameUnique(string name, string? definitionId = null, CancellationToken cancellationToken = default);
    }
}

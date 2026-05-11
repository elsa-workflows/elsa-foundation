using Elsa.Primitives.Entities;
using Elsa.Primitives.Persistence;
using System.Linq.Expressions;

namespace Elsa.Persistence.Core
{
    public interface IQueries<TEntity>
        where TEntity : Entity, new()
    {
        /// <summary>
        /// Finds a single entity using a query
        /// </summary>
        /// <param name="query">The query to use</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The entity if found, otherwise <c>null</c></returns>
        Task<TEntity?> FindAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Finds the entity matching the specified predicate.
        /// </summary>
        /// <param name="predicate">The predicate to use.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        Task<TEntity?> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds a list of entities using a query
        /// </summary>
        Task<IEnumerable<TEntity>> FindManyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds a list of entities using a query
        /// </summary>
        Task<IEnumerable<TEntity>> FindManyAsync<TProp>(Expression<Func<TEntity, bool>> predicate, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a list of entities using a query
        /// </summary>
        Task<Page<TEntity>> FindManyAsync(
            Expression<Func<TEntity, bool>>? predicate,            
            PageArgs? pageArgs = null,
            CancellationToken cancellationToken = default
        );


        /// <summary>
        /// Returns a list of entities using a query
        /// </summary>
        Task<Page<TEntity>> FindManyAsync<TProp>(
            Expression<Func<TEntity, bool>>? predicate,
            OrderDefinition<TEntity, TProp> order,
            PageArgs? pageArgs = null,
            CancellationToken cancellationToken = default
        );

        Task<IEnumerable<TEntity>> ListAsync(CancellationToken cancellationToken = default);


        /// <summary>
        /// Queries the database using a query and a selector.
        /// </summary>
        Task<IEnumerable<TEntity>> QueryAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Queries the database using a query and a selector.
        /// </summary>
        Task<IEnumerable<TEntity>> QueryAsync<TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default);

        /// <summary>
        /// Queries the database using a query and a selector.
        /// </summary>
        Task<IEnumerable<TResult>> QueryAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector,CancellationToken cancellationToken = default);

        /// <summary>
        /// Queries the database using a query and a selector.
        /// </summary>
        Task<IEnumerable<TResult>> QueryAsync<TResult, TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default);

        /// <summary>
        /// Counts the number of entities matching a query.
        /// </summary>
        Task<long> CountAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if any entities exist.
        /// </summary>
        Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Counts the number of entities matching a predicate.
        /// </summary>
        /// <param name="predicate">The predicate.</param>
        /// <param name="ignoreQueryFilters">Whether to ignore query filters.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Counts the distinct number of entities matching a predicate.
        /// </summary>
        /// <param name="predicate">The predicate.</param>
        /// <param name="propertySelector">The property selector to distinct by.</param>
        /// <param name="ignoreQueryFilters">Whether to ignore query filters.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task<long> CountAsync<TProperty>(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default);        
    }
}

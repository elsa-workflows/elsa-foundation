using Elsa.Primitives.Entities;
using Elsa.Primitives.Persistence;
using System.Linq.Expressions;

namespace Elsa.Persistence.Core
{
    public interface IQueries<TEntity>
        where TEntity : Entity
    {
        public async Task<TEntity> Get(string id, CancellationToken cancellationToken) => (await Find(x => x.Id == id, cancellationToken)) ?? throw new InvalidOperationException($"Entity '{typeof(TEntity)}' with id '{id}' cannot be found");

        /// <summary>
        /// Finds a single entity using a query
        /// </summary>
        /// <param name="query">The query to use</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The entity if found, otherwise <c>null</c></returns>
        Task<TEntity?> Find(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default);

        Task<TEntity?> Find(IFilter<TEntity> filter, CancellationToken cancellationToken = default);

        Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, Expression<Func<TEntity, TProperty>> include, CancellationToken cancellationToken = default);

        Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, IEnumerable<Expression<Func<TEntity, TProperty>>> include, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds the entity matching the specified predicate.
        /// </summary>
        /// <param name="predicate">The predicate to use.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        Task<TEntity?> Find(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds a list of entities using a query
        /// </summary>
        Task<IEnumerable<TEntity>> FindMany(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        Task<IEnumerable<TEntity>> FindMany(IFilter<TEntity> filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// Finds a list of entities using a query
        /// </summary>
        Task<IEnumerable<TEntity>> FindMany<TProp>(Expression<Func<TEntity, bool>> predicate, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default);

        /// <summary>
        /// Returns a list of entities using a query
        /// </summary>
        Task<Page<TEntity>> FindMany(
            Expression<Func<TEntity, bool>>? predicate,
            PageArgs? pageArgs = null,
            CancellationToken cancellationToken = default
        );

        Task<Page<TEntity>> FindMany(
            IFilter<TEntity> filter,
            PageArgs? pageArgs = null,
            CancellationToken cancellationToken = default
        );    


        /// <summary>
        /// Returns a list of entities using a query
        /// </summary>
        Task<Page<TEntity>> FindMany<TProp>(
            Expression<Func<TEntity, bool>>? predicate,
            OrderDefinition<TEntity, TProp> order,
            PageArgs? pageArgs = null,
            CancellationToken cancellationToken = default
        );

        Task<IEnumerable<TEntity>> List(CancellationToken cancellationToken = default);


        /// <summary>
        /// Queries the database using a query and a selector.
        /// </summary>
        Task<IEnumerable<TEntity>> Query(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default);

        Task<IEnumerable<TEntity>> Query(IFilter<TEntity> filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// Queries the database using a query and a selector.
        /// </summary>
        Task<IEnumerable<TEntity>> Query<TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default);

        /// <summary>
        /// Queries the database using a query and a selector.
        /// </summary>
        Task<IEnumerable<TResult>> Query<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default);

        Task<IEnumerable<TResult>> Query<TResult>(IFilter<TEntity> filter, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default);

        Task<IEnumerable<TEntity>> Query<TProperty>(IFilter<TEntity> filter, OrderDefinition<TEntity, TProperty> order, CancellationToken cancellationToken = default);

        /// <summary>
        /// Queries the database using a query and a selector.
        /// </summary>
        Task<IEnumerable<TResult>> Query<TResult, TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default);

        Task<IEnumerable<TResult>> Query<TResult, TProp>(IFilter<TEntity> filter, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default);

        /// <summary>
        /// Counts the number of entities matching a query.
        /// </summary>
        Task<long> Count(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if any entities exist.
        /// </summary>
        Task<bool> Any(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        Task<bool> Any(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default);

        Task<bool> Any(IFilter<TEntity> filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// Counts the number of entities matching a predicate.
        /// </summary>
        /// <param name="predicate">The predicate.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task<long> Count(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

        Task<long> Count(IFilter<TEntity> filter, CancellationToken cancellationToken = default);

        Task<long> Count<TProperty>(IFilter<TEntity> filter, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default);

        /// <summary>
        /// Counts the distinct number of entities matching a predicate.
        /// </summary>
        /// <param name="predicate">The predicate.</param>
        /// <param name="propertySelector">The property selector to distinct by.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task<long> Count<TProperty>(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default);
    }
}

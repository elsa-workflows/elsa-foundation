using Elsa.Persistence.Core;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Extensions;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Open.Linq.AsyncExtensions;
using System.Linq.Expressions;

namespace Elsa.Persistence.EFCore.Services
{
    /// <summary>
    /// A generic repository class around EF Core for accessing entities.
    /// </summary>
    /// <typeparam name="TDbContext">The type of the database context.</typeparam>
    /// <typeparam name="TEntity">The type of the entity.</typeparam>
    public sealed class EFCoreQueries<TDbContext, TEntity>(IDbContextFactory<TDbContext> dbContextFactory, IServiceProvider serviceProvider)
        : IQueries<TEntity>
        where TDbContext : DbContext
        where TEntity : Entity, new()
    {
        /// <summary>
        /// Creates a new instance of the database context.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The database context.</returns>
        private Task<TDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => dbContextFactory.CreateDbContextAsync(cancellationToken);


        /// <inheritdoc />
        public async Task<TEntity?> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            var entity = await set.FirstOrDefaultAsync(predicate, cancellationToken);

            if (entity == null)
                return null;

            await ApplyEntityLoadingHandlers(dbContext, entity, cancellationToken);

            return entity;
        }

        /// <inheritdoc />
        public async Task<TEntity?> FindAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
        {
            return await QueryAsync(query, cancellationToken).FirstOrDefault();
        }


        /// <inheritdoc />
        public async Task<IEnumerable<TEntity>> FindManyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            var entities = await set.Where(predicate).ToListAsync(cancellationToken);

            await ApplyEntityLoadingHandlers(dbContext, entities, cancellationToken);

            return entities;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<TEntity>> FindManyAsync<TProp>(Expression<Func<TEntity, bool>> predicate, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            set = ApplyOrder(set, order);
            var entities = await set.Where(predicate).ToListAsync(cancellationToken);

            await ApplyEntityLoadingHandlers(dbContext, entities, cancellationToken);

            return entities;
        }

        static IQueryable<TEntity> ApplyOrder<TProp>(IQueryable<TEntity> query, OrderDefinition<TEntity, TProp> order)
        {
            if (order.Direction == OrderDirection.Descending)
            {
                return query.OrderByDescending(order.KeySelector);
            }

            return query.OrderBy(order.KeySelector);
        }

        /// <inheritdoc />
        public async Task<Page<TEntity>> FindManyAsync(
            Expression<Func<TEntity, bool>>? predicate,
            PageArgs? pageArgs = null,
            CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();

            if (predicate != null)
                set = set.Where(predicate);

            var page = await set.PaginateAsync(pageArgs);

            return page;
        }

        public async Task<Page<TEntity>> FindManyAsync<TProp>(
          Expression<Func<TEntity, bool>>? predicate,
          OrderDefinition<TEntity, TProp> order,
          PageArgs? pageArgs = null,
          CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            set = ApplyOrder(set, order);

            if (predicate != null)
                set = set.Where(predicate);

            var page = await set.PaginateAsync(pageArgs);

            return page;
        }

        public async Task<IEnumerable<TEntity>> ListAsync(CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            var entities = await set.ToListAsync(cancellationToken);

            await ApplyEntityLoadingHandlers(dbContext, entities, cancellationToken);

            return entities;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<TEntity>> QueryAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);

            var loadingHandlersRegistered = LoadingHandlersRegistered();
            var set = loadingHandlersRegistered
                ? dbContext.Set<TEntity>()
                : dbContext.Set<TEntity>().AsNoTracking();

            var queryable = query(set.AsQueryable());
            var entities = await queryable.ToListAsync(cancellationToken);

            await ApplyEntityLoadingHandlers(dbContext, entities, cancellationToken);

            return entities;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<TEntity>> QueryAsync<TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);

            var loadingHandlersRegistered = LoadingHandlersRegistered();
            var set = loadingHandlersRegistered
                ? dbContext.Set<TEntity>()
                : dbContext.Set<TEntity>().AsNoTracking();

            set = ApplyOrder(set, order);

            var queryable = query(set.AsQueryable());
            var entities = await queryable.ToListAsync(cancellationToken);

            await ApplyEntityLoadingHandlers(dbContext, entities, cancellationToken);

            return entities;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<TResult>> QueryAsync<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();

            var queryable = query(set.AsQueryable());
            queryable = query(queryable);

            return await queryable
                .Select(selector)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IEnumerable<TResult>> QueryAsync<TResult, TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            set = ApplyOrder(set, order);

            var queryable = query(set.AsQueryable());
            queryable = query(queryable);

            return await queryable
                .Select(selector)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
        {
            return await CountAsync(query, false, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            var queryable = query(set.AsQueryable());

            if (ignoreQueryFilters)
                queryable = queryable.IgnoreQueryFilters();

            queryable = query(queryable);
            return await queryable.LongCountAsync(cancellationToken: cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await AnyAsync(predicate, false, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            return await set.AnyAsync(predicate, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await CountAsync(predicate, false, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(Expression<Func<TEntity, bool>> predicate, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var queryable = dbContext.Set<TEntity>().AsNoTracking();

            if (ignoreQueryFilters)
                queryable = queryable.IgnoreQueryFilters();

            return await queryable.CountAsync(predicate, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<long> CountAsync<TProperty>(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default)
        {
            return await CountAsync(predicate, propertySelector, false, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<long> CountAsync<TProperty>(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, TProperty>> propertySelector, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContextAsync(cancellationToken);
            var queryable = dbContext.Set<TEntity>().AsNoTracking();

            if (ignoreQueryFilters)
                queryable = queryable.IgnoreQueryFilters();

            return await queryable
                .Where(predicate)
                .Select(propertySelector)
                .Distinct()
                .CountAsync(cancellationToken);
        }

        Task ApplyEntityLoadingHandlers(TDbContext dbContext, List<TEntity> entities, CancellationToken cancellationToken)
        {
            using var scope = serviceProvider.CreateScope();
            var tasks = entities.Select(e => ApplyEntityLoadingHandlers(dbContext, scope, e, cancellationToken));
            return Task.WhenAll(tasks);
        }

        static async Task ApplyEntityLoadingHandlers(TDbContext dbContext, IServiceScope scope, TEntity entity, CancellationToken cancellationToken)
        {
            var handlers = scope.ServiceProvider
                .GetServices<IEntityLoadingHandler<TDbContext, TEntity>>()
                .ToList();

            foreach (var handler in handlers)
            {
                await handler.Handle(dbContext, entity, cancellationToken);
            }
        }

        Task ApplyEntityLoadingHandlers(TDbContext dbContext, TEntity entity, CancellationToken cancellationToken)
        {
            using var scope = serviceProvider.CreateScope();
            return ApplyEntityLoadingHandlers(dbContext, scope, entity, cancellationToken);
        }

        bool LoadingHandlersRegistered()
        {
            return serviceProvider
                .GetServices<IEntityLoadingHandler<TDbContext, TEntity>>()
                .Any();
        }
    }
}

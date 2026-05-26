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
            where TEntity : Entity
    {
        /// <summary>
        /// Creates a new instance of the database context.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The database context.</returns>
        private Task<TDbContext> CreateDbContext(CancellationToken cancellationToken = default) => dbContextFactory.CreateDbContextAsync(cancellationToken);


        /// <inheritdoc />
        public async Task<TEntity?> Find(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            var entity = await set.FirstOrDefaultAsync(predicate, cancellationToken);

            if (entity == null)
                return null;

            await ApplyEntityLoadingHandlers(dbContext, entity, cancellationToken);

            return entity;
        }

        /// <inheritdoc />
        public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, Expression<Func<TEntity, TProperty>> include, CancellationToken cancellationToken = default)
        {
            return Find(filter, [include], cancellationToken);
        }

        /// <inheritdoc />
        public async Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, IEnumerable<Expression<Func<TEntity, TProperty>>> include, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();

            foreach (var expression in include)
                set = set.Include(expression);

            // Apply the filter to the include-augmented set. Without this the filter was
            // built then discarded, producing SELECT ... LIMIT 1 with no WHERE — which
            // returned an arbitrary row and triggered EF's First-without-OrderBy warning.
            var query = GetQuery(filter)(set);

            var entity = await query.FirstOrDefaultAsync(cancellationToken);

            if (entity == null)
                return null;

            await ApplyEntityLoadingHandlers(dbContext, entity, cancellationToken);

            return entity;
        }

        /// <inheritdoc />
        public async Task<TEntity?> Find(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
        {
            return await Query(query, cancellationToken).FirstOrDefault();
        }


        /// <inheritdoc />
        public async Task<IEnumerable<TEntity>> FindMany(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            var entities = await set.Where(predicate).ToListAsync(cancellationToken);

            await ApplyEntityLoadingHandlers(dbContext, entities, cancellationToken);

            return entities;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<TEntity>> FindMany<TProp>(Expression<Func<TEntity, bool>> predicate, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            set = ApplyOrder(set, order);
            var entities = await set.Where(predicate).ToListAsync(cancellationToken);

            await ApplyEntityLoadingHandlers(dbContext, entities, cancellationToken);

            return entities;
        }

        private static IQueryable<TEntity> ApplyOrder<TProp>(IQueryable<TEntity> query, OrderDefinition<TEntity, TProp> order)
        {
            if (order.Direction == OrderDirection.Descending)
            {
                return query.OrderByDescending(order.KeySelector);
            }

            return query.OrderBy(order.KeySelector);
        }

        /// <inheritdoc />
        public async Task<Page<TEntity>> FindMany(
            Expression<Func<TEntity, bool>>? predicate,
            PageArgs? pageArgs = null,
            CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();

            if (predicate != null)
                set = set.Where(predicate);

            var page = await set.PaginateAsync(pageArgs);
            await ApplyEntityLoadingHandlers(dbContext, page, cancellationToken);

            return page;
        }

        public async Task<Page<TEntity>> FindMany<TProp>(
          Expression<Func<TEntity, bool>>? predicate,
          OrderDefinition<TEntity, TProp> order,
          PageArgs? pageArgs = null,
          CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            set = ApplyOrder(set, order);

            if (predicate != null)
                set = set.Where(predicate);

            var page = await set.PaginateAsync(pageArgs);
            await ApplyEntityLoadingHandlers(dbContext, page, cancellationToken);

            return page;
        }

        public async Task<IEnumerable<TEntity>> FindMany(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            set = query(set);
            var entities = await set.ToListAsync(cancellationToken);

            await ApplyEntityLoadingHandlers(dbContext, entities, cancellationToken);

            return entities;
        }

        public async Task<IEnumerable<TEntity>> List(CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            var entities = await set.ToListAsync(cancellationToken);

            await ApplyEntityLoadingHandlers(dbContext, entities, cancellationToken);

            return entities;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<TEntity>> Query(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);

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
        public async Task<IEnumerable<TEntity>> Query<TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);

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
        public async Task<IEnumerable<TResult>> Query<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();

            var queryable = query(set.AsQueryable());
            queryable = query(queryable);

            return await queryable
                .Select(selector)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<IEnumerable<TResult>> Query<TResult, TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            set = ApplyOrder(set, order);

            var queryable = query(set.AsQueryable());
            queryable = query(queryable);

            return await queryable
                .Select(selector)
                .ToListAsync(cancellationToken);
        }

        /// <inheritdoc />
        public async Task<long> Count(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
        {
            return await Count(query, false, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<long> Count(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            var queryable = query(set.AsQueryable());

            if (ignoreQueryFilters)
                queryable = queryable.IgnoreQueryFilters();

            queryable = query(queryable);
            return await queryable.LongCountAsync(cancellationToken: cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> Any(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await Any(predicate, false, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<bool> Any(Expression<Func<TEntity, bool>> predicate, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            return await set.AnyAsync(predicate, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<long> Count(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await Count(predicate, false, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<long> Count(Expression<Func<TEntity, bool>> predicate, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var queryable = dbContext.Set<TEntity>().AsNoTracking();

            if (ignoreQueryFilters)
                queryable = queryable.IgnoreQueryFilters();

            return await queryable.CountAsync(predicate, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<long> Count<TProperty>(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var queryable = dbContext.Set<TEntity>().AsNoTracking();

            return await queryable
                .Where(predicate)
                .Select(propertySelector)
                .Distinct()
                .CountAsync(cancellationToken);
        }

        public Task<TEntity?> Find(IFilter<TEntity> filter, CancellationToken cancellationToken = default)
        {
            var query = GetQuery(filter);
            return Find(query, cancellationToken);
        }

        public Task<IEnumerable<TEntity>> FindMany(IFilter<TEntity> filter, CancellationToken cancellationToken = default)
        {
            var query = GetQuery(filter);
            return FindMany(query, cancellationToken);
        }

        public Task<Page<TEntity>> FindMany(IFilter<TEntity> filter, PageArgs? pageArgs = null, CancellationToken cancellationToken = default)
        {
            var query = GetQuery(filter);
            return FindMany(query, pageArgs, cancellationToken);
        }

        public async Task<Page<TEntity>> FindMany(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, PageArgs? pageArgs = null, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            set = query(set);

            var page = await set.PaginateAsync(pageArgs);
            await ApplyEntityLoadingHandlers(dbContext, page, cancellationToken);

            return page;
        }

        public Task<IEnumerable<TEntity>> Query(IFilter<TEntity> filter, CancellationToken cancellationToken = default)
        {
            var query = GetQuery(filter);
            return Query(query, cancellationToken);
        }

        public Task<IEnumerable<TResult>> Query<TResult>(IFilter<TEntity> filter, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default)
        {
            var query = GetQuery(filter);
            return Query(query, selector, cancellationToken);
        }

        public Task<IEnumerable<TResult>> Query<TResult, TProp>(IFilter<TEntity> filter, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default)
        {
            var query = GetQuery(filter);
            return Query(query, selector, order, cancellationToken);
        }

        public Task<IEnumerable<TEntity>> Query<TProp>(IFilter<TEntity> filter, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default)
        {
            var query = GetQuery(filter);
            return Query(query, order, cancellationToken);
        }

        public Task<bool> Any(IFilter<TEntity> filter, CancellationToken cancellationToken = default)
        {
            var query = GetQuery(filter);
            return Any(query, cancellationToken);
        }


        public async Task<bool> Any(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var set = dbContext.Set<TEntity>().AsNoTracking();
            set = query(set);

            return await set.AnyAsync(cancellationToken);
        }


        public Task<long> Count(IFilter<TEntity> filter, CancellationToken cancellationToken = default)
        {
            var query = GetQuery(filter);
            return Count(query, cancellationToken);
        }

        public async Task<long> Count<TProperty>(IFilter<TEntity> filter, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default)
        {
            await using var dbContext = await CreateDbContext(cancellationToken);
            var query = GetQuery(filter);
            var queryable = dbContext.Set<TEntity>().AsNoTracking();
            queryable = query(queryable);

            return await queryable
                .Select(propertySelector)
                .Distinct()
                .CountAsync(cancellationToken);
        }

        private Task ApplyEntityLoadingHandlers(TDbContext dbContext, List<TEntity> entities, CancellationToken cancellationToken)
        {
            using var scope = serviceProvider.CreateScope();
            var tasks = entities.Select(e => ApplyEntityLoadingHandlers(dbContext, scope, e, cancellationToken));
            return Task.WhenAll(tasks);
        }

        private Task ApplyEntityLoadingHandlers(TDbContext dbContext, Page<TEntity> entities, CancellationToken cancellationToken)
        {
            using var scope = serviceProvider.CreateScope();
            var tasks = entities.Items.Select(e => ApplyEntityLoadingHandlers(dbContext, scope, e, cancellationToken));
            return Task.WhenAll(tasks);
        }

        private static async Task ApplyEntityLoadingHandlers(TDbContext dbContext, IServiceScope scope, TEntity entity, CancellationToken cancellationToken)
        {
            var handlers = scope.ServiceProvider
                .GetServices<IEntityLoadingHandler<TDbContext, TEntity>>()
                .ToList();

            foreach (var handler in handlers)
            {
                await handler.Handle(dbContext, entity, cancellationToken);
            }
        }

        private Task ApplyEntityLoadingHandlers(TDbContext dbContext, TEntity entity, CancellationToken cancellationToken)
        {
            using var scope = serviceProvider.CreateScope();
            return ApplyEntityLoadingHandlers(dbContext, scope, entity, cancellationToken);
        }

        private bool LoadingHandlersRegistered()
        {
            return serviceProvider
                .GetServices<IEntityLoadingHandler<TDbContext, TEntity>>()
                .Any();
        }

        private static Func<IQueryable<TEntity>, IQueryable<TEntity>> GetQuery(IFilter<TEntity> filter)
        {
            return q =>
            {
                var result = filter.Apply(q);

                if (filter.TenantAgnostic == true)
                {
                    return result.IgnoreQueryFilters();
                }

                return result;
            };
        }
    }
}

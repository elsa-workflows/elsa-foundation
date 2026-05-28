using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Persistence;
using System.Linq.Expressions;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// Inert <see cref="IQueries{TEntity}"/> used to construct the lookup in tests that only
/// exercise paths going through <see cref="IDbContextFactory{TContext}"/>. Every method
/// throws — if a test inadvertently routes through these dependencies, the test fails
/// loudly rather than silently bypassing the picker-visibility query.
/// </summary>
internal sealed class ThrowingQueries<TEntity> : IQueries<TEntity> where TEntity : Entity
{
    private const string Message = "Test routed through ThrowingQueries; this test should only exercise the IDbContextFactory path.";

    public Task<TEntity?> Find(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<TEntity?> Find(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, Expression<Func<TEntity, TProperty>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, IEnumerable<Expression<Func<TEntity, TProperty>>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<TEntity?> Find(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TEntity>> FindMany(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TEntity>> FindMany(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TEntity>> FindMany<TProp>(Expression<Func<TEntity, bool>> predicate, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<Page<TEntity>> FindMany(Expression<Func<TEntity, bool>>? predicate, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<Page<TEntity>> FindMany(IFilter<TEntity> filter, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<Page<TEntity>> FindMany<TProp>(Expression<Func<TEntity, bool>>? predicate, OrderDefinition<TEntity, TProp> order, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TEntity>> List(CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TEntity>> Query(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TEntity>> Query(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TEntity>> Query<TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TResult>> Query<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TResult>> Query<TResult>(IFilter<TEntity> filter, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TEntity>> Query<TProperty>(IFilter<TEntity> filter, OrderDefinition<TEntity, TProperty> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TResult>> Query<TResult, TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<IEnumerable<TResult>> Query<TResult, TProp>(IFilter<TEntity> filter, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<long> Count(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<bool> Any(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<bool> Any(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<bool> Any(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<long> Count(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<long> Count(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<long> Count<TProperty>(IFilter<TEntity> filter, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    public Task<long> Count<TProperty>(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
}

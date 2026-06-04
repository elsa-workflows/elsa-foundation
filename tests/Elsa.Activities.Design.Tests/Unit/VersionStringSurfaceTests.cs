using System.Linq.Expressions;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Api.Mapping;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Mapping.Core.Contracts;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// US2 surface test (SC-001). A persisted version carries the author's full SemVer 2.0.0 string —
/// including prerelease + build metadata — and that exact string surfaces verbatim on every read
/// path: the <see cref="IActivityDefinitionVersion"/> read contract, the <see cref="ListDefinitionVersions"/>
/// projection/handler, and the API <see cref="ActivityDefinitionVersionDetailsView"/>.
/// </summary>
public sealed class VersionStringSurfaceTests
{
    private const string AuthorVersion = "1.2.3-rc.1+build.5";

    [Fact]
    public void ReadContract_ExposesAuthorSemverVerbatim()
    {
        var (version, _) = BuildPersistedVersion();

        Assert.Equal(AuthorVersion, ((IActivityDefinitionVersion)version).Version);
    }

    [Fact]
    public async Task ListProjection_ExposesAuthorSemverVerbatim()
    {
        var (version, _) = BuildPersistedVersion();
        var handler = new ListDefinitionVersionsRequestHandler(
            new SelectingQueries<ActivityDefinitionVersion>([version]));

        var infos = (await handler.Handle(new ListDefinitionVersions(version.DefinitionId), CancellationToken.None)).ToList();

        var info = Assert.Single(infos);
        Assert.Equal(AuthorVersion, info.Version);
    }

    [Fact]
    public async Task DetailsView_ExposesAuthorSemverVerbatim()
    {
        var (version, _) = BuildPersistedVersion();
        var mapper = new ActivityDefinitionVersionToDetailsView(new StubDefinitionMapping());

        var view = await mapper.Map(version, CancellationToken.None);

        Assert.Equal(AuthorVersion, view.Version);
    }

    private static (ActivityDefinitionVersion Version, ActivityDefinition Definition) BuildPersistedVersion()
    {
        var definition = new ActivityDefinition
        {
            Id = "def-1",
            ActivityTypeKey = "Acme.Activities.Greet",
            Category = "Acme",
        };

        var version = new ActivityDefinitionVersion(AuthorVersion, definition.Id)
        {
            Definition = definition,
            ImplementationKind = ClrImplementationDescriptor.KindValue,
            ImplementationDescriptor = new ClrImplementationDescriptor(TypeInformation.Object),
        };

        return (version, definition);
    }

    private sealed class StubDefinitionMapping : IObjectMapping<ActivityDefinition, ActivityDefinitionView>
    {
        public ValueTask<ActivityDefinitionView> Map(ActivityDefinition source, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityDefinitionView(source.Id, source.ActivityTypeKey, source.Category, source.DisplayName, source.Description));
    }

    /// <summary>
    /// Minimal in-memory <see cref="IQueries{TEntity}"/> that implements only the
    /// filter+selector <c>Query</c> overload the list handler uses; every other route fails loudly.
    /// </summary>
    private sealed class SelectingQueries<TEntity>(List<TEntity> items) : IQueries<TEntity> where TEntity : Entity
    {
        private const string Msg = "SelectingQueries: only filter+selector Query is supported in this test.";

        public Task<IEnumerable<TResult>> Query<TResult>(IFilter<TEntity> filter, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) =>
            Task.FromResult(filter.Apply(items.AsQueryable()).Select(selector.Compile()).AsEnumerable());

        public Task<TEntity?> Find(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, Expression<Func<TEntity, TProperty>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, IEnumerable<Expression<Func<TEntity, TProperty>>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> FindMany(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> FindMany(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> FindMany<TProp>(Expression<Func<TEntity, bool>> predicate, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Page<TEntity>> FindMany(Expression<Func<TEntity, bool>>? predicate, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Page<TEntity>> FindMany(IFilter<TEntity> filter, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Page<TEntity>> FindMany<TProp>(Expression<Func<TEntity, bool>>? predicate, OrderDefinition<TEntity, TProp> order, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> List(CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query<TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query<TProperty>(IFilter<TEntity> filter, OrderDefinition<TEntity, TProperty> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult, TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult, TProp>(IFilter<TEntity> filter, Expression<Func<TEntity, TResult>> selector, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<bool> Any(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<bool> Any(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<bool> Any(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count<TProperty>(IFilter<TEntity> filter, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<long> Count<TProperty>(Expression<Func<TEntity, bool>> predicate, Expression<Func<TEntity, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    }
}

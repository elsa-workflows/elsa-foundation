using System.Linq.Expressions;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Persistence;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>A bare <see cref="IActivity"/> with one concrete-declared property, for projection assertions.</summary>
internal sealed class StubActivity : IActivity
{
    public string Greeting { get; set; } = "hello";

    public string Id { get; set; } = "act-1";
    public string NodeId { get; set; } = "node-1";
    public string? Name { get; set; }
    public string Type { get; set; } = "Stub";
    public string Version { get; set; } = "1.0.0";
    public Dictionary<string, object> CustomProperties { get; set; } = new() { ["author"] = "joey" };
    public Dictionary<string, object> SyntheticProperties { get; set; } = new() { ["WorkflowIdentity"] = "wf-123" };
    public Dictionary<string, object> Metadata { get; set; } = new();

    public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => ValueTask.FromResult(true);
    public ValueTask ExecuteAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;
}

/// <summary>Captures what the bridge passed across the seam and returns a preset activity.</summary>
internal sealed class FakeActivityFactory(IActivity result) : IActivityFactory
{
    public string? LastDescriptorType { get; private set; }
    public JsonElement LastPayload { get; private set; }
    public IDictionary<string, InputArgument>? LastInputs { get; private set; }
    public IDictionary<string, OutputArgument>? LastOutputs { get; private set; }

    public ValueTask<IActivity> Create(
        string descriptorType,
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken = default)
    {
        LastDescriptorType = descriptorType;
        LastPayload = payload;
        LastInputs = inputs;
        LastOutputs = outputs;
        return ValueTask.FromResult(result);
    }
}

/// <summary>Minimal in-memory queries: only the two routes the bridge uses are real; the rest fail loudly.</summary>
internal sealed class FakeVersionQueries(List<ActivityDefinitionVersion> items) : IQueries<ActivityDefinitionVersion>
{
    private const string Msg = "FakeVersionQueries: only Find(filter, include) and List are supported.";

    public Task<ActivityDefinitionVersion?> Find<TProperty>(IFilter<ActivityDefinitionVersion> filter, Expression<Func<ActivityDefinitionVersion, TProperty>> include, CancellationToken cancellationToken = default)
    {
        var queryable = filter.Apply(items.AsQueryable());
        return Task.FromResult(queryable.FirstOrDefault());
    }

    public Task<IEnumerable<ActivityDefinitionVersion>> List(CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<ActivityDefinitionVersion>>(items);

    public Task<ActivityDefinitionVersion?> Find(Func<IQueryable<ActivityDefinitionVersion>, IQueryable<ActivityDefinitionVersion>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<ActivityDefinitionVersion?> Find(IFilter<ActivityDefinitionVersion> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<ActivityDefinitionVersion?> Find<TProperty>(IFilter<ActivityDefinitionVersion> filter, IEnumerable<Expression<Func<ActivityDefinitionVersion, TProperty>>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<ActivityDefinitionVersion?> Find(Expression<Func<ActivityDefinitionVersion, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<IEnumerable<ActivityDefinitionVersion>> FindMany(Expression<Func<ActivityDefinitionVersion, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<IEnumerable<ActivityDefinitionVersion>> FindMany(IFilter<ActivityDefinitionVersion> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<IEnumerable<ActivityDefinitionVersion>> FindMany<TProp>(Expression<Func<ActivityDefinitionVersion, bool>> predicate, OrderDefinition<ActivityDefinitionVersion, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<Page<ActivityDefinitionVersion>> FindMany(Expression<Func<ActivityDefinitionVersion, bool>>? predicate, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<Page<ActivityDefinitionVersion>> FindMany(IFilter<ActivityDefinitionVersion> filter, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<Page<ActivityDefinitionVersion>> FindMany<TProp>(Expression<Func<ActivityDefinitionVersion, bool>>? predicate, OrderDefinition<ActivityDefinitionVersion, TProp> order, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<IEnumerable<ActivityDefinitionVersion>> Query(Func<IQueryable<ActivityDefinitionVersion>, IQueryable<ActivityDefinitionVersion>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<IEnumerable<ActivityDefinitionVersion>> Query(IFilter<ActivityDefinitionVersion> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<IEnumerable<ActivityDefinitionVersion>> Query<TProp>(Func<IQueryable<ActivityDefinitionVersion>, IQueryable<ActivityDefinitionVersion>> query, OrderDefinition<ActivityDefinitionVersion, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<IEnumerable<TResult>> Query<TResult>(Func<IQueryable<ActivityDefinitionVersion>, IQueryable<ActivityDefinitionVersion>> query, Expression<Func<ActivityDefinitionVersion, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<IEnumerable<TResult>> Query<TResult>(IFilter<ActivityDefinitionVersion> filter, Expression<Func<ActivityDefinitionVersion, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<IEnumerable<ActivityDefinitionVersion>> Query<TProperty>(IFilter<ActivityDefinitionVersion> filter, OrderDefinition<ActivityDefinitionVersion, TProperty> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<IEnumerable<TResult>> Query<TResult, TProp>(Func<IQueryable<ActivityDefinitionVersion>, IQueryable<ActivityDefinitionVersion>> query, Expression<Func<ActivityDefinitionVersion, TResult>> selector, OrderDefinition<ActivityDefinitionVersion, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<IEnumerable<TResult>> Query<TResult, TProp>(IFilter<ActivityDefinitionVersion> filter, Expression<Func<ActivityDefinitionVersion, TResult>> selector, OrderDefinition<ActivityDefinitionVersion, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<long> Count(Func<IQueryable<ActivityDefinitionVersion>, IQueryable<ActivityDefinitionVersion>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<bool> Any(Expression<Func<ActivityDefinitionVersion, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<bool> Any(Func<IQueryable<ActivityDefinitionVersion>, IQueryable<ActivityDefinitionVersion>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<bool> Any(IFilter<ActivityDefinitionVersion> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<long> Count(Expression<Func<ActivityDefinitionVersion, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<long> Count(IFilter<ActivityDefinitionVersion> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<long> Count<TProperty>(IFilter<ActivityDefinitionVersion> filter, Expression<Func<ActivityDefinitionVersion, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
    public Task<long> Count<TProperty>(Expression<Func<ActivityDefinitionVersion, bool>> predicate, Expression<Func<ActivityDefinitionVersion, TProperty>> propertySelector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
}

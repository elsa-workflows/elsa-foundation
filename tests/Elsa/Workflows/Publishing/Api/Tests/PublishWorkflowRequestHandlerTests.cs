using System.Linq.Expressions;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublishWorkflowRequestHandlerTests
{
    private readonly InMemoryWorkflowExecutableStore _store = new();
    private readonly ActivityDefinitionVersion _writeLineActivity = ActivityVersion("activity-write-line", "Text", TypeInformation.String);

    [Fact]
    public async Task PublishesSequentialWorkflowVersionIntoExecutableArtifact()
    {
        var workflowVersion = WorkflowVersion(
            activities:
            [
                Node("write-one", isStart: true, isTerminal: false, Text("one")),
                Node("write-two", isStart: false, isTerminal: true, Text("two"))
            ],
            connections: [Connection("write-one", "write-two")]);
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("version-1", view.DefinitionVersionId);
        Assert.Equal(2, view.NodeCount);
        Assert.Equal(1, view.EdgeCount);
        Assert.Equal(["write-one"], view.StartNodeIds);
        Assert.Equal("one", executable.NodesById["write-one"].InputBindings["Text"].LiteralValue!.Value.GetString());
        Assert.Equal($"{typeof(string).FullName}, {typeof(string).Assembly.GetName().Name}", executable.NodesById["write-one"].InputBindings["Text"].Metadata["typeName"]);
    }

    [Fact]
    public async Task RejectsWorkflowWithoutExactlyOneStartNode()
    {
        var workflowVersion = WorkflowVersion(
            activities: [Node("write-one", isStart: false, isTerminal: true, Text("one"))],
            connections: []);
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("exactly one start", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsWorkflowWithMultipleStartNodes()
    {
        var workflowVersion = WorkflowVersion(
            activities:
            [
                Node("write-one", isStart: true, isTerminal: false, Text("one")),
                Node("write-two", isStart: true, isTerminal: true, Text("two"))
            ],
            connections: [Connection("write-one", "write-two")]);
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("exactly one start", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsFanOutGraph()
    {
        var workflowVersion = WorkflowVersion(
            activities:
            [
                Node("write-one", isStart: true, isTerminal: false, Text("one")),
                Node("write-two", isStart: false, isTerminal: true, Text("two")),
                Node("write-three", isStart: false, isTerminal: true, Text("three"))
            ],
            connections:
            [
                Connection("write-one", "write-two"),
                Connection("write-one", "write-three")
            ]);
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("fan-out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsNonLiteralInput()
    {
        var workflowVersion = WorkflowVersion(
            activities: [Node("write-one", isStart: true, isTerminal: true, new WorkflowArgumentState("Text", new ArgumentValue("name", "Variable"), null, null, null, null))],
            connections: []);
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("only literal", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsUnknownActivityVersionId()
    {
        var workflowVersion = WorkflowVersion(
            activities: [new ActivityNode("missing", "missing-activity", [Text("one")], [], false, true, true, [])],
            connections: []);
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("missing-activity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Activity definition version", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Workflow definition version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private PublishWorkflowRequestHandler Handler(WorkflowDefinitionVersion workflowVersion) =>
        new(
            new FakeQueries<WorkflowDefinitionVersion>([workflowVersion]),
            new FakeQueries<ActivityDefinitionVersion>([_writeLineActivity]),
            _store);

    private static WorkflowDefinitionVersion WorkflowVersion(
        IEnumerable<ActivityNode> activities,
        IEnumerable<ActivityConnection> connections) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], connections, activities, [], [], null, null)
        };

    private static ActivityNode Node(string nodeId, bool isStart, bool isTerminal, params WorkflowArgumentState[] inputs) =>
        new(
            nodeId,
            "activity-write-line",
            inputs,
            Outputs: [],
            IsContainer: false,
            isStart,
            isTerminal,
            ChildActivities: []);

    private static WorkflowArgumentState Text(string value) =>
        new("Text", new ArgumentValue(value, "Literal"), null, null, null, null);

    private static ActivityConnection Connection(string source, string target) =>
        new(new ActivityPortConnection(source, "Done"), new ActivityPortConnection(target, "In"));

    private static ActivityDefinitionVersion ActivityVersion(string id, string inputName, TypeInformation inputType) =>
        new("1.0.0", "activity-definition-1")
        {
            Id = id,
            Definition = new ActivityDefinition
            {
                Id = "activity-definition-1",
                ActivityTypeKey = "Test.WriteLine",
                Category = "Test"
            },
            DescriptorType = typeof(TypeInformation).FullName!,
            DescriptorPayload = JsonSerializer.SerializeToElement(TypeInformation.FromType<object>()),
            Inputs = [new InputDefinition(inputName, inputName, inputType, null, inputName, null)]
        };

    private sealed class FakeQueries<TEntity>(List<TEntity> items) : IQueries<TEntity>
        where TEntity : Entity
    {
        private const string Msg = "FakeQueries only supports Find(filter, include) for these tests.";

        public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, Expression<Func<TEntity, TProperty>> include, CancellationToken cancellationToken = default)
        {
            var queryable = filter.Apply(items.AsQueryable());
            return Task.FromResult(queryable.FirstOrDefault());
        }

        public Task<TEntity?> Find(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => Task.FromResult(filter.Apply(items.AsQueryable()).FirstOrDefault());
        public Task<TEntity?> Find<TProperty>(IFilter<TEntity> filter, IEnumerable<Expression<Func<TEntity, TProperty>>> include, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<TEntity?> Find(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> List(CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<TEntity>>(items);
        public Task<IEnumerable<TEntity>> FindMany(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> FindMany(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> FindMany<TProp>(Expression<Func<TEntity, bool>> predicate, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Page<TEntity>> FindMany(Expression<Func<TEntity, bool>>? predicate, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Page<TEntity>> FindMany(IFilter<TEntity> filter, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<Page<TEntity>> FindMany<TProp>(Expression<Func<TEntity, bool>>? predicate, OrderDefinition<TEntity, TProp> order, PageArgs? pageArgs = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query(IFilter<TEntity> filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TEntity>> Query<TProp>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, OrderDefinition<TEntity, TProp> order, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult>(Func<IQueryable<TEntity>, IQueryable<TEntity>> query, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
        public Task<IEnumerable<TResult>> Query<TResult>(IFilter<TEntity> filter, Expression<Func<TEntity, TResult>> selector, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Msg);
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

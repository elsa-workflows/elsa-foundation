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
    private const string ActivitiesSlotName = "Activities";

    private readonly InMemoryWorkflowExecutableStore _store = new();
    private readonly ActivityDefinitionVersion _writeLineActivity = ActivityVersion("activity-write-line", "Text", TypeInformation.String);

    [Fact]
    public async Task PublishesRootActivityIntoExecutableArtifact()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("version-1", view.DefinitionVersionId);
        Assert.Equal("write-one", view.RootActivityId);
        Assert.Equal(1, view.NodeCount);
        Assert.Equal("write-one", executable.RootActivity.ExecutableNodeId);
        Assert.Equal("one", executable.NodesById["write-one"].InputBindings["Text"].LiteralValue!.Value.GetString());
        Assert.Equal($"{typeof(string).FullName}, {typeof(string).Assembly.GetName().Name}", executable.NodesById["write-one"].InputBindings["Text"].Metadata["typeName"]);
    }

    [Fact]
    public async Task PublishedExecutableArtifactCanBeDispatchedForExecution()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var published = await Handler(workflowVersion).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var dispatcher = new WorkflowExecutionStartDispatcher(
            _store,
            new InProcessWorkflowExecutionAgentProvider(),
            new GuidRuntimeExecutionIdGenerator());

        var result = await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(published.ArtifactId, "test"));

        Assert.Equal(published.ArtifactId, result.PinnedExecutable.ArtifactId);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        Assert.NotEmpty(result.WorkflowExecutionId);
    }

    [Fact]
    public async Task PublishesCompositeRootWithActivityOwnedChildSlot()
    {
        var root = CompositeNode(
            "composite",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ]);
        var workflowVersion = WorkflowVersion(root);
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Equal("composite", view.RootActivityId);
        Assert.Equal(3, view.NodeCount);
        var childSlot = Assert.Single(executable.RootActivity.ChildSlots);
        Assert.Equal(["write-one", "write-two"], childSlot.Activities.Select(activity => activity.ExecutableNodeId));
    }

    [Fact]
    public async Task RejectsWorkflowWithoutRootActivity()
    {
        var workflowVersion = WorkflowVersion(rootActivity: null);
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("root activity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsNonLiteralInput()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("name", "Variable"), null, null, null, null)));
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("only literal", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsUnknownActivityVersionId()
    {
        var workflowVersion = WorkflowVersion(new ActivityNode("missing", "missing-activity", [Text("one")], [], null));
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("missing-activity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Activity definition version", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Workflow definition version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ComputesSameArtifactIdForEquivalentWorkflowOrdering()
    {
        var firstWorkflowVersion = WorkflowVersion(CompositeNode(
            "composite",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two")),
                Node("write-three", Text("three"))
            ]));
        var secondWorkflowVersion = WorkflowVersion(CompositeNode(
            "composite",
            [
                Node("write-three", Text("three")),
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ]));
        var firstView = await Handler(firstWorkflowVersion).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var secondView = await Handler(secondWorkflowVersion).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.Equal(firstView.ArtifactId, secondView.ArtifactId);
        Assert.Equal(firstView.ArtifactHash, secondView.ArtifactHash);
    }

    [Fact]
    public async Task ComputesDifferentArtifactIdWhenLiteralInputTypeMetadataChanges()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("1")));
        var stringView = await Handler(workflowVersion, ActivityVersion("activity-write-line", "Text", TypeInformation.String))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var integerView = await Handler(workflowVersion, ActivityVersion("activity-write-line", "Text", TypeInformation.Integer))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.NotEqual(stringView.ArtifactId, integerView.ArtifactId);
        Assert.NotEqual(stringView.ArtifactHash, integerView.ArtifactHash);
    }

    private PublishWorkflowRequestHandler Handler(WorkflowDefinitionVersion workflowVersion) =>
        Handler(workflowVersion, _writeLineActivity);

    private PublishWorkflowRequestHandler Handler(WorkflowDefinitionVersion workflowVersion, params ActivityDefinitionVersion[] activityVersions) =>
        new(
            new FakeQueries<WorkflowDefinitionVersion>([workflowVersion]),
            new FakeQueries<ActivityDefinitionVersion>(activityVersions.ToList()),
            _store);

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], rootActivity, [], [], null, null)
        };

    private static ActivityNode Node(string nodeId, params WorkflowArgumentState[] inputs) =>
        Node(nodeId, null, inputs);

    private static ActivityNode Node(
        string nodeId,
        IReadOnlyCollection<ActivityChildSlot>? childSlots,
        params WorkflowArgumentState[] inputs) =>
        new(
            nodeId,
            "activity-write-line",
            inputs,
            Outputs: [],
            ChildSlots: childSlots);

    private static ActivityNode CompositeNode(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities) =>
        Node(
            nodeId,
            [
                new ActivityChildSlot(ActivitiesSlotName, activities)
            ]);

    private static WorkflowArgumentState Text(string value) =>
        new("Text", new ArgumentValue(value, "Literal"), null, null, null, null);

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

using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Persistence.Core;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowTestRunRequestHandlerTests
{
    private readonly ActivityDefinitionVersion _writeLineActivity = ActivityVersion("activity-write-line", "Text", new TypeReference("String"));
    private readonly IActivityStructureService _activityStructureService = ActivityStructureService();
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryWorkflowTestRunStore _testRunStore = new();

    [Fact]
    public async Task StartsTransientWorkflowTestRunWithoutListingPublishedExecutable()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("DispatchAccepted", view.Status);
        Assert.Equal("Accepted", view.CommandDispatchStatus);
        Assert.NotNull(view.WorkflowExecutionId);
        Assert.StartsWith("test-artifact-", view.ArtifactId, StringComparison.Ordinal);
        Assert.Empty(await _executableStore.ListAsync());
        Assert.NotNull(await _testRunStore.FindAsync(view.TestRunId));
    }

    [Fact]
    public async Task StartsDraftSnapshotTransientWorkflowTestRunWithoutDurableDefinitionVersion()
    {
        var dispatcher = Dispatcher();
        var handler = DraftSnapshotHandler(dispatcher);

        var view = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-1",
            State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null, null)), CancellationToken.None);

        Assert.Equal("DispatchAccepted", view.Status);
        Assert.Equal("Accepted", view.CommandDispatchStatus);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("draft:snapshot-1", view.DefinitionVersionId);
        Assert.NotNull(view.WorkflowExecutionId);
        Assert.StartsWith("test-artifact-", view.ArtifactId, StringComparison.Ordinal);
        Assert.Empty(await _executableStore.ListAsync());
        await Assert.ThrowsAsync<WorkflowExecutableNotFoundException>(() =>
            dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(view.ArtifactId!, "normal-runtime")).AsTask());
    }

    [Fact]
    public async Task RejectsDraftSnapshotCompileFailureWithSnapshotIdentity()
    {
        var handler = DraftSnapshotHandler();

        var view = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-1",
            State: new WorkflowDefinitionState([], RootActivity: null, [], [], null, null)), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("draft:snapshot-1", view.DefinitionVersionId);
        Assert.Null(view.ArtifactId);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Contains("root activity", view.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _executableStore.ListAsync(includeTransient: true));
        Assert.NotNull(await _testRunStore.FindAsync(view.TestRunId));
    }

    [Fact]
    public void RejectsBlankDraftSnapshotIdBeforeCreatingTestRunRequest()
    {
        var exception = Assert.Throws<ArgumentException>(() => new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: " ",
            State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null, null)));

        Assert.Equal("SnapshotId", exception.ParamName);
    }

    [Fact]
    public async Task RejectsMissingRootWithoutDispatchingExecution()
    {
        var handler = Handler(WorkflowVersion(rootActivity: null));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Null(view.ArtifactId);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("version-1", view.DefinitionVersionId);
        Assert.Contains("root activity", view.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _executableStore.ListAsync());
    }

    [Fact]
    public async Task RejectsUnknownActivityWithoutDispatchingExecution()
    {
        var handler = Handler(WorkflowVersion(new ActivityNode("missing", "missing-activity", [Text("hello")], [])));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Contains("missing-activity", view.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DispatchesNonLiteralExpressionInput()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("\"Hello \" + \"World\"", "JavaScript"), null, null, null, null))));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("DispatchAccepted", view.Status);
        Assert.Equal("Accepted", view.CommandDispatchStatus);
        Assert.NotNull(view.WorkflowExecutionId);
        Assert.StartsWith("test-artifact-", view.ArtifactId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsExpressionInputWithoutExpressionTextWithoutDispatchingExecution()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("", "JavaScript"), null, null, null, null))));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Contains("no expression text", view.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsInvalidLiteralConversionWithoutDispatchingExecution()
    {
        var handler = Handler(
            WorkflowVersion(Node("write-one", Text("not-an-int"))),
            activityVersions: [ActivityVersion("activity-write-line", "Text", new TypeReference("Int32"))]);

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Null(view.ArtifactId);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Contains("cannot be converted", view.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalRuntimeDispatchDoesNotStartTransientArtifact()
    {
        var dispatcher = Dispatcher();
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))), dispatcher);
        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        await Assert.ThrowsAsync<WorkflowExecutableNotFoundException>(() =>
            dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(view.ArtifactId!, "normal-runtime")).AsTask());
    }

    [Fact]
    public async Task CleanupExpiredTransientArtifactsRemovesExecutableButKeepsTestRunMetadata()
    {
        var transientStore = new InMemoryTransientWorkflowExecutableStore(_executableStore);
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))), Dispatcher(), transientStore);
        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        var deleted = await transientStore.CleanupExpiredAsync(view.ExpiresAt!.Value.AddTicks(1));

        Assert.Equal(1, deleted);
        Assert.Null(await transientStore.FindAsync(view.ArtifactId!));
        Assert.NotNull(await _testRunStore.FindAsync(view.TestRunId));
    }

    [Fact]
    public async Task CleanupExpiredTransientArtifactsUsesPersistedExpirationAfterTransientStoreRestart()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))));
        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);
        var restartedTransientStore = new InMemoryTransientWorkflowExecutableStore(_executableStore);

        var deleted = await restartedTransientStore.CleanupExpiredAsync(view.ExpiresAt!.Value.AddTicks(1));

        Assert.Equal(1, deleted);
        Assert.Null(await restartedTransientStore.FindAsync(view.ArtifactId!));
        Assert.Empty(await _executableStore.ListAsync(includeTransient: true));
    }

    private StartWorkflowTestRunRequestHandler Handler(
        WorkflowDefinitionVersion workflowVersion,
        IWorkflowStartDispatcher? dispatcher = null,
        ITransientWorkflowExecutableStore? transientStore = null,
        IReadOnlyCollection<ActivityDefinitionVersion>? activityVersions = null) =>
        new(
            new WorkflowExecutableCompiler(
                new FakeVersionStore(workflowVersion),
                new FakeActivityVersionStore((activityVersions ?? [_writeLineActivity]).ToList()),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()),
            transientStore ?? new InMemoryTransientWorkflowExecutableStore(_executableStore),
            _testRunStore,
            dispatcher ?? Dispatcher(),
            TimeProvider.System);

    private StartWorkflowTestRunRequestHandler DraftSnapshotHandler(IWorkflowStartDispatcher? dispatcher = null) =>
        new(
            new WorkflowExecutableCompiler(
                new ThrowingVersionStore(),
                new FakeActivityVersionStore([_writeLineActivity]),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()),
            new InMemoryTransientWorkflowExecutableStore(_executableStore),
            _testRunStore,
            dispatcher ?? Dispatcher(),
            TimeProvider.System);

    private WorkflowStartDispatcher Dispatcher() =>
        new(
            _executableStore,
            new InProcessWorkflowExecutionActorProvider(),
            new GuidRuntimeExecutionIdGenerator());

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], rootActivity, [], [], null, null)
        };

    private static ActivityNode Node(string nodeId, params WorkflowArgumentState[] inputs) =>
        new(nodeId, "activity-write-line", inputs, Outputs: []);

    private static WorkflowArgumentState Text(string value) =>
        new("Text", new ArgumentValue(value, "Literal"), null, null, null, null);

    private static ActivityDefinitionVersion ActivityVersion(string id, string inputName, TypeReference inputType) =>
        new("1.0.0", "activity-definition-1")
        {
            Id = id,
            Definition = new ActivityDefinition
            {
                Id = "activity-definition-1",
                ActivityTypeKey = "Test.WriteLine",
                Category = "Test"
            },
            DescriptorType = typeof(ClrActivityDescriptor).FullName!,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor("Object")),
            Inputs = [new InputDefinition(inputName, inputName, inputType, null, inputName, null)]
        };

    private static IActivityStructureService ActivityStructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    private sealed class FakeVersionStore(WorkflowDefinitionVersion version) : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            version.Id == versionId
                ? Task.FromResult(version)
                : throw new ArgumentException($"Workflow definition version with id '{versionId}' does not exist");

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingVersionStore : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Draft snapshot test runs must not read durable workflow definition versions.");

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

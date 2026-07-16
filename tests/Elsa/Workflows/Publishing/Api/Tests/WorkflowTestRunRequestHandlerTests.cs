using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Models;
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
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _sourceReferenceStore = new();
    private readonly InMemoryWorkflowTestRunStore _testRunStore = new();

    [Fact]
    public async Task StartsTransientWorkflowTestRunWithoutListingPublishedExecutable()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("DispatchAccepted", view.Status);
        Assert.Equal("Accepted", view.CommandDispatchStatus);
        Assert.NotNull(view.WorkflowExecutionId);
        Assert.StartsWith("artifact-", view.ArtifactId, StringComparison.Ordinal);
        // Under ADR 0040 there is a single content-addressed store; the test-run artifact now lives in it (scope/
        // expiry are reference facts, no longer segregated by an artifact-level list filter).
        Assert.Equal(view.ArtifactId, Assert.Single(await _executableStore.ListAsync()).Identity.ArtifactId);
        Assert.NotNull(await _testRunStore.FindAsync(view.TestRunId));
    }

    [Fact]
    public async Task ClassifiesVersionAndDraftSnapshotDispatchesAsTestRuns()
    {
        var versionDispatcher = new CapturingStartDispatcher(Dispatcher());
        var draftDispatcher = new CapturingStartDispatcher(Dispatcher());

        await Handler(WorkflowVersion(Node("write-one", Text("hello"))), versionDispatcher)
            .Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);
        await DraftSnapshotHandler(draftDispatcher).Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-1",
            State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null)), CancellationToken.None);

        var versionRequest = Assert.Single(versionDispatcher.Requests);
        var draftRequest = Assert.Single(draftDispatcher.Requests);
        Assert.Equal(WorkflowRunKind.TestRun, versionRequest.RunKind);
        Assert.Equal(WorkflowRunKind.TestRun, draftRequest.RunKind);
        await AssertExactTestRunReferenceAsync(versionRequest);
        await AssertExactTestRunReferenceAsync(draftRequest);
    }

    private async Task AssertExactTestRunReferenceAsync(WorkflowExecutionStartDispatchRequest request)
    {
        var sourceReferenceId = Assert.IsType<string>(request.SourceSelection?.SourceReferenceId);
        var reference = await _sourceReferenceStore.FindAsync(sourceReferenceId);
        Assert.NotNull(reference);
        Assert.Equal(request.ArtifactId, reference.ArtifactId);
        Assert.Equal(WorkflowExecutableReferenceScope.TestRun, reference.Scope);
    }

    [Fact]
    public async Task StartsDraftSnapshotTransientWorkflowTestRunWithoutDurableDefinitionVersion()
    {
        var dispatcher = Dispatcher();
        var handler = DraftSnapshotHandler(dispatcher);

        var view = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-1",
            State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null)), CancellationToken.None);

        Assert.Equal("DispatchAccepted", view.Status);
        Assert.Equal("Accepted", view.CommandDispatchStatus);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("draft:snapshot-1", view.DefinitionVersionId);
        Assert.NotNull(view.WorkflowExecutionId);
        Assert.StartsWith("artifact-", view.ArtifactId, StringComparison.Ordinal);
        // Single content-addressed store (ADR 0040): the draft test-run artifact is present. Reference-driven
        // scope gating for dispatch (rejecting a test-run artifact from a normal runtime dispatch) is worker W3's
        // slice; the artifact-level dispatcher no longer rejects by scope.
        Assert.Equal(view.ArtifactId, Assert.Single(await _executableStore.ListAsync()).Identity.ArtifactId);
        var snapshot = await _testRunStore.FindDraftSnapshotAsync(view.DefinitionVersionId);
        Assert.NotNull(snapshot);
        Assert.Equal("write-one", snapshot.State.RootActivity!.NodeId);
    }

    [Fact]
    public async Task DraftTestRunCopiesAuthoredInputSourceOntoItsExpiringReference()
    {
        const string authoredJson = "{\"script\":\"return input.customerId;\"}";
        using var document = JsonDocument.Parse(authoredJson);
        var state = new WorkflowDefinitionState(
            [],
            Node("write-one", new WorkflowArgumentState(
                "Text",
                new ArgumentValue(document.RootElement.Clone(), "JavaScript"),
                null,
                null,
                null,
                false)),
            [],
            [],
            null);

        var view = await DraftSnapshotHandler().Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-authored-input",
            State: state), CancellationToken.None);

        var reference = Assert.Single(await _sourceReferenceStore.ListByArtifactAsync(view.ArtifactId!));
        var authored = Assert.Single(reference.AuthoredInputs);
        Assert.Equal("write-one", authored.ExecutableNodeId);
        Assert.Equal("Text", authored.InputKey);
        Assert.Equal("JavaScript", authored.ExpressionType);
        Assert.Equal(authoredJson, authored.Value.GetRawText());
    }

    [Fact]
    public async Task TestRunDoesNotWriteSourceReferenceWhileDeletionGuardOwnsArtifact()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))));
        var first = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);
        var existingReference = Assert.Single(await _sourceReferenceStore.ListByArtifactAsync(first.ArtifactId!));
        await _sourceReferenceStore.RetireAsync(existingReference.SourceReferenceId, DateTimeOffset.UtcNow, "test-setup");
        var now = DateTimeOffset.UtcNow;
        var deletionGuard = await _executableStore.TryBeginDeletionAsync(first.ArtifactId!, "gc-test", now.AddMinutes(1), now);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() =>
            handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None));

        Assert.NotNull(deletionGuard);
        Assert.Equal(first.ArtifactId, exception.ArtifactId);
        Assert.Single(await _sourceReferenceStore.ListByArtifactAsync(first.ArtifactId!));
        Assert.Empty(await _sourceReferenceStore.ListAsync(WorkflowExecutableReferenceScope.TestRun, liveOnly: true));
    }

    [Fact]
    public async Task RejectsDraftSnapshotCompileFailureWithSnapshotIdentity()
    {
        var handler = DraftSnapshotHandler();

        var view = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-1",
            State: new WorkflowDefinitionState([], RootActivity: null, [], [], null)), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("draft:snapshot-1", view.DefinitionVersionId);
        Assert.Null(view.ArtifactId);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Contains("root activity", view.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _executableStore.ListAsync());
        Assert.NotNull(await _testRunStore.FindAsync(view.TestRunId));
        Assert.Null(await _testRunStore.FindDraftSnapshotAsync(view.DefinitionVersionId));
    }

    [Fact]
    public void RejectsBlankDraftSnapshotIdBeforeCreatingTestRunRequest()
    {
        var exception = Assert.Throws<ArgumentException>(() => new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: " ",
            State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null)));

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
        Assert.StartsWith("artifact-", view.ArtifactId, StringComparison.Ordinal);
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
    public async Task TestRunAppendsAnExpiringTestRunReferenceIntoTheSingleStore()
    {
        // ADR 0040: the test run saves the artifact into the one content-addressed store and appends an expiring
        // TestRun-scope source reference (scope/expiry are reference facts). The reference is what a dispatch gates on.
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))));
        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.NotNull(await _executableStore.FindAsync(view.ArtifactId!));
        var reference = Assert.Single(await _sourceReferenceStore.ListByArtifactAsync(view.ArtifactId!));
        Assert.Equal(WorkflowExecutableReferenceScope.TestRun, reference.Scope);
        Assert.Equal(view.ExpiresAt, reference.ExpiresAt);
        Assert.Null(reference.PublishedAt);
    }

    [Fact]
    public async Task PublishedDispatchIsRejectedForArtifactWithOnlyTestRunReferences()
    {
        // Test (d): a NORMAL published dispatch of an artifact that only carries a TestRun reference is refused by the
        // reference gate — a test-run artifact is not dispatchable as published even though it lives in the same store.
        var dispatcher = Dispatcher();
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))), dispatcher);
        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(view.ArtifactId!, "normal-runtime")).AsTask());

        Assert.Equal(view.ArtifactId, exception.ArtifactId);
        Assert.Equal(WorkflowExecutableReferenceScope.Published, exception.RequiredScope);
        Assert.Equal(WorkflowExecutableReferenceRejectionReason.NoLiveReference, exception.Reason);
    }

    [Fact]
    public async Task DraftTestRunResolvesToSameArtifactIdAsBehaviorallyIdenticalVersion()
    {
        // Test (a) — the ADR 0040 equivalence signal. A draft snapshot and a durable version with identical behavior
        // compile to the SAME artifact id because the hash is purely behavioral (ADR 0038) and the test-run flow now
        // shares the published artifact prefix (scope lives on the reference, not the id). Same id ⇒ Studio can report
        // "this draft is behaviorally identical to published vN" with no diffing.
        var identicalRoot = Node("write-one", Text("hello"));

        var versionView = await Handler(WorkflowVersion(identicalRoot))
            .Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);
        var draftView = await DraftSnapshotHandler()
            .Handle(new StartWorkflowDraftTestRun(
                DefinitionId: "definition-1",
                SnapshotId: "snapshot-1",
                State: new WorkflowDefinitionState([], identicalRoot, [], [], null)), CancellationToken.None);

        Assert.NotNull(versionView.ArtifactId);
        Assert.Equal(versionView.ArtifactId, draftView.ArtifactId);
        // Both references point at the single shared artifact.
        Assert.Single(await _executableStore.ListAsync());
        Assert.Equal(2, (await _sourceReferenceStore.ListByArtifactAsync(versionView.ArtifactId!)).Count);
    }

    [Fact]
    public async Task DispatchIsRejectedWithExpiryReasonWhenTheOnlyReferenceExpired()
    {
        // Test (b): once the test run's only (TestRun) reference has passed its ExpiresAt, dispatching the artifact is
        // refused with the distinguishing Expired reason — the artifact still exists, but nothing lets it run.
        var dispatcher = Dispatcher();
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))), dispatcher);
        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        // Drive the dispatcher's clock past the reference expiry so the same-scope reference is present but lapsed.
        var expiredDispatcher = new WorkflowStartDispatcher(
            _executableStore,
            _sourceReferenceStore,
            new InProcessWorkflowExecutionActorProvider(),
            new ShortRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(view.ExpiresAt!.Value.AddSeconds(1)));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            expiredDispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(view.ArtifactId!, "test-run"), WorkflowExecutableReferenceScope.TestRun).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.Expired, exception.Reason);
        Assert.Equal(WorkflowExecutableReferenceScope.TestRun, exception.RequiredScope);
    }

    [Fact]
    public async Task TestRunStoreDropsExpiredRunOnRead()
    {
        // #398: a test run past its ExpiresAt must never be observed, even before the periodic sweep runs.
        var store = new InMemoryWorkflowTestRunStore();
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(TestRun("testrun-expired", expiresAt: now.AddMinutes(-1)));

        Assert.Null(await store.FindAsync("testrun-expired"));
    }

    [Fact]
    public async Task TestRunStoreKeepsUnexpiredAndNeverExpiringRuns()
    {
        var store = new InMemoryWorkflowTestRunStore();
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(TestRun("testrun-live", expiresAt: now.AddMinutes(30)));
        await store.SaveAsync(TestRun("testrun-eternal", expiresAt: null));

        Assert.NotNull(await store.FindAsync("testrun-live"));
        Assert.NotNull(await store.FindAsync("testrun-eternal"));
    }

    [Fact]
    public async Task TestRunStoreKeepsUnexpiredDraftSnapshot()
    {
        var store = new InMemoryWorkflowTestRunStore();
        var snapshot = DraftSnapshot("draft:snapshot-live", expiresAt: DateTimeOffset.UtcNow.AddMinutes(30));
        await store.SaveDraftSnapshotAsync(snapshot);

        var stored = await store.FindDraftSnapshotAsync("draft:snapshot-live");

        Assert.NotNull(stored);
        Assert.Equal("write-one", stored.State.RootActivity!.NodeId);
    }

    [Fact]
    public async Task TestRunStoreDropsExpiredDraftSnapshotOnRead()
    {
        var store = new InMemoryWorkflowTestRunStore();
        await store.SaveDraftSnapshotAsync(DraftSnapshot("draft:snapshot-expired", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Null(await store.FindDraftSnapshotAsync("draft:snapshot-expired"));
    }

    [Fact]
    public async Task CleanupExpiredTestRunsRemovesOnlyExpiredRuns()
    {
        // #398: the store used to grow without bound; CleanupExpiredAsync now evicts every run whose retention
        // window has elapsed while leaving live and never-expiring runs in place.
        var store = new InMemoryWorkflowTestRunStore();
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(TestRun("testrun-old", expiresAt: now.AddMinutes(-5)));
        await store.SaveAsync(TestRun("testrun-live", expiresAt: now.AddMinutes(30)));
        await store.SaveAsync(TestRun("testrun-eternal", expiresAt: null));

        var deleted = await store.CleanupExpiredAsync(now);

        Assert.Equal(1, deleted);
        Assert.Null(await store.FindAsync("testrun-old"));
        Assert.NotNull(await store.FindAsync("testrun-live"));
        Assert.NotNull(await store.FindAsync("testrun-eternal"));
    }

    [Fact]
    public async Task CleanupExpiredTestRunsRemovesExpiredDraftSnapshots()
    {
        var store = new InMemoryWorkflowTestRunStore();
        var now = DateTimeOffset.UtcNow;
        await store.SaveDraftSnapshotAsync(DraftSnapshot("draft:snapshot-old", expiresAt: now.AddMinutes(-5)));
        await store.SaveDraftSnapshotAsync(DraftSnapshot("draft:snapshot-live", expiresAt: now.AddMinutes(30)));

        var deleted = await store.CleanupExpiredAsync(now);

        Assert.Equal(1, deleted);
        Assert.Null(await store.FindDraftSnapshotAsync("draft:snapshot-old"));
        Assert.NotNull(await store.FindDraftSnapshotAsync("draft:snapshot-live"));
    }

    private static WorkflowTestRun TestRun(string testRunId, DateTimeOffset? expiresAt) =>
        new(
            TestRunId: testRunId,
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactId: null,
            WorkflowExecutionId: null,
            Status: WorkflowTestRunStatus.Rejected,
            RequestedBy: "test",
            RequestedAt: expiresAt?.AddMinutes(-30) ?? DateTimeOffset.UtcNow,
            ExpiresAt: expiresAt,
            Reason: null,
            Metadata: new Dictionary<string, string>());

    private static WorkflowTestRunDraftSnapshot DraftSnapshot(string definitionVersionId, DateTimeOffset? expiresAt) =>
        new(
            DefinitionId: "definition-1",
            DefinitionVersionId: definitionVersionId,
            ArtifactVersion: "draft",
            State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null),
            RequestedAt: expiresAt?.AddMinutes(-30) ?? DateTimeOffset.UtcNow,
            ExpiresAt: expiresAt);

    private StartWorkflowTestRunRequestHandler Handler(
        WorkflowDefinitionVersion workflowVersion,
        IWorkflowStartDispatcher? dispatcher = null,
        IReadOnlyCollection<ActivityDefinitionVersion>? activityVersions = null)
    {
        var versionStore = new FakeVersionStore(workflowVersion);
        return new(
            TestCompiler.Create(
                versionStore,
                new FakeActivityVersionStore((activityVersions ?? [_writeLineActivity]).ToList()),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()),
            _executableStore,
            _sourceReferenceStore,
            new EmptyWorkflowDefinitionVersionLayoutStore(),
            _testRunStore,
            dispatcher ?? Dispatcher(),
            TestRootWriteLeases.Create(_executableStore),
            TimeProvider.System,
            versionStore,
            new WorkflowExecutableAuthoredInputsSidecar(new ActivityTreeProjector(_activityStructureService)));
    }

    private StartWorkflowTestRunRequestHandler DraftSnapshotHandler(IWorkflowStartDispatcher? dispatcher = null)
    {
        return new(
            TestCompiler.Create(
                new ThrowingVersionStore(),
                new FakeActivityVersionStore([_writeLineActivity]),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()),
            _executableStore,
            _sourceReferenceStore,
            new EmptyWorkflowDefinitionVersionLayoutStore(),
            _testRunStore,
            dispatcher ?? Dispatcher(),
            TestRootWriteLeases.Create(_executableStore),
            TimeProvider.System,
            authoredInputsSidecar: new WorkflowExecutableAuthoredInputsSidecar(new ActivityTreeProjector(_activityStructureService)));
    }

    private WorkflowStartDispatcher Dispatcher() =>
        new(
            _executableStore,
            _sourceReferenceStore,
            new InProcessWorkflowExecutionActorProvider(),
            new ShortRuntimeExecutionIdGenerator());

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], rootActivity, [], [], null)
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
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
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

    private sealed class CapturingStartDispatcher(IWorkflowStartDispatcher inner) : IWorkflowStartDispatcher
    {
        public List<WorkflowExecutionStartDispatchRequest> Requests { get; } = [];

        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.DispatchAsync(request, requiredScope, dispatchOptions, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

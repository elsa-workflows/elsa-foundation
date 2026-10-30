using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeBookmarkStimulusResumeDispatchTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 16, 0, 0, TimeSpan.Zero);
    private readonly WorkflowExecutableIdentity _identity = new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    [Fact]
    public async Task FindAsync_ReturnsOnlyNonExpiredStimulusMatch()
    {
        var store = new InMemoryBookmarkStateStore();
        var lookup = new BookmarkStimulusLookup(store);
        await store.SaveAsync(NewBookmark("bookmark-expired", expiresAt: _now.AddSeconds(-1)));
        await store.SaveAsync(NewBookmark("bookmark-other-type", stimulusType: "other"));
        await store.SaveAsync(NewBookmark("bookmark-other-hash", stimulusHash: "sha256:other"));
        await store.SaveAsync(NewBookmark("bookmark-active"));

        var result = await lookup.FindAsync(NewLookupRequest());

        Assert.Equal(BookmarkStimulusLookupStatus.Found, result.Status);
        Assert.Equal("bookmark-active", result.Bookmark!.BookmarkId);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task FindAsync_RejectsAmbiguousStimulusMatches()
    {
        var store = new InMemoryBookmarkStateStore();
        var lookup = new BookmarkStimulusLookup(store);
        await store.SaveAsync(NewBookmark("bookmark-b", createdAt: _now.AddMinutes(-1)));
        await store.SaveAsync(NewBookmark("bookmark-a", createdAt: _now.AddMinutes(-1)));

        var result = await lookup.FindAsync(NewLookupRequest());

        Assert.Equal(BookmarkStimulusLookupStatus.Ambiguous, result.Status);
        Assert.Null(result.Bookmark);
        Assert.Equal(["bookmark-a", "bookmark-b"], result.AmbiguousBookmarkIds);
        Assert.Contains("Multiple active bookmarks matched stimulus", result.Reason);
    }

    [Fact]
    public async Task DispatchAsync_ResolvesPinnedExecutableAndEnqueuesResumeBookmarkCommand()
    {
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var agentProvider = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(store, workflowStateStore, executableStore, agentProvider);
        await store.SaveAsync(NewBookmark("bookmark-1"));
        await workflowStateStore.SaveAsync(NewWorkflowExecution());
        await executableStore.SaveAsync(NewExecutable());
        var input = Json("""{"orderId":"order-123"}""");

        var result = await dispatcher.DispatchAsync(new BookmarkResumeDispatchRequest(
            workflowExecutionId: "wfexec-1",
            stimulusType: "delivery-status",
            stimulusHash: "sha256:delivery-status:order-123",
            input: input,
            requestedBy: "adapter",
            metadata: new Dictionary<string, string>
            {
                ["adapter"] = "test"
            }));

        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, result.Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch!.Status);
        Assert.Equal("bookmark-1", result.Bookmark!.BookmarkId);
        Assert.Equal("resume-target:delivery", result.Resolution!.ResumeTarget.ResumeTargetId);
        var activation = Assert.Single(agentProvider.Activations);
        Assert.Equal(WorkflowExecutionActorActivationReason.ResumeBookmark, activation.Reason);
        Assert.Equal("adapter", activation.RequestedBy);
        var envelope = Assert.Single(agentProvider.Agent.Envelopes);
        Assert.Equal("envelope-1", envelope.EnvelopeId);
        Assert.Equal("wfexec-1:bookmark-resume:bookmark-1:delivery-status:sha256:delivery-status:order-123", envelope.IdempotencyKey);
        Assert.Equal(WorkflowExecutionCommandKind.ResumeBookmark, envelope.Command.Kind);
        Assert.Equal("command-1", envelope.Command.CommandId);
        Assert.Equal("resume-target:delivery", envelope.Command.Metadata["runtime.resumeTargetId"]);
        Assert.Equal("test", envelope.Command.Metadata["adapter"]);
        var payload = envelope.Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
        Assert.Equal(_identity, payload.PinnedExecutable);
        Assert.Equal("bookmark-1", payload.BookmarkId);
        Assert.Equal("actexec-1", payload.ActivityExecutionId);
        Assert.Equal("node-wait", payload.ExecutableNodeId);
        Assert.Equal("resume-target:delivery", payload.ResumeTargetId);
        Assert.Equal("delivery-status", payload.StimulusType);
        Assert.Equal("sha256:delivery-status:order-123", payload.StimulusHash);
        Assert.Equal("order-123", payload.Input!.Value.GetProperty("orderId").GetString());
        Assert.DoesNotContain(
            typeof(RuntimeResumeBookmarkCommandPayload).GetProperties().Select(property => property.Name),
            propertyName => propertyName.Contains("Method", StringComparison.OrdinalIgnoreCase) || propertyName.Contains("Callback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Broadcast_retry_keeps_the_pre_upgrade_mailbox_key_and_distinguishes_exact_bookmarks()
    {
        var bookmarks = new InMemoryBookmarkStateStore();
        var executions = new InMemoryWorkflowExecutionStateStore();
        var executables = new InMemoryWorkflowExecutableStore();
        var actors = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(bookmarks, executions, executables, actors);
        var router = new StimulusRouter(new InMemoryWorkflowTriggerBindingStore(), new GlobalBookmarkStimulusLookup(bookmarks),
            new NoStarts(), dispatcher, new FakeTimeProvider(_now));
        await bookmarks.SaveAsync(NewBookmark("bookmark-1"));
        await executions.SaveAsync(NewWorkflowExecution());
        await executables.SaveAsync(NewExecutable());
        await router.RouteAsync(new StimulusDispatchRequest("delivery-status", "sha256:delivery-status:order-123",
            mode: StimulusRoutingMode.ResumeOnly, idempotencyKey: "publication"));
        var oldKey = Assert.Single(actors.Agent.Envelopes).IdempotencyKey;
        // The pre-upgrade command is accepted but still pending. A second exact wait is also present.
        await bookmarks.SaveAsync(NewBookmark("bookmark-2"));
        await router.RouteAsync(new StimulusDispatchRequest("delivery-status", "sha256:delivery-status:order-123",
            mode: StimulusRoutingMode.ResumeOnly, idempotencyKey: "publication") { ResumeAllMatchingBookmarks = true });
        Assert.Equal(3, actors.Agent.Envelopes.Count);
        Assert.Equal(oldKey, actors.Agent.Envelopes[1].IdempotencyKey);
        Assert.NotEqual(oldKey, actors.Agent.Envelopes[2].IdempotencyKey);
    }

    private sealed class NoStarts : IWorkflowStartDispatcher
    {
        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Resume-only routing must never dispatch a start.");
    }

    [Fact]
    public async Task DispatchAsync_UsesCallerIdempotencyKeyAsDeterministicSuffix()
    {
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var agentProvider = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(store, workflowStateStore, executableStore, agentProvider);
        await store.SaveAsync(NewBookmark("bookmark-1"));
        await workflowStateStore.SaveAsync(NewWorkflowExecution());
        await executableStore.SaveAsync(NewExecutable());

        await dispatcher.DispatchAsync(new BookmarkResumeDispatchRequest(
            workflowExecutionId: "wfexec-1",
            stimulusType: "delivery-status",
            stimulusHash: "sha256:delivery-status:order-123",
            idempotencyKey: "adapter-delivery-1"));

        Assert.Equal(
            "wfexec-1:bookmark-resume:bookmark-1:delivery-status:sha256:delivery-status:order-123:adapter-delivery-1",
            Assert.Single(agentProvider.Agent.Envelopes).IdempotencyKey);
    }

    [Fact]
    public async Task DispatchAsync_PropagatesPersistedWorkflowPartition()
    {
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var agentProvider = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(store, workflowStateStore, executableStore, agentProvider);
        var partition = new WorkflowExecutionPartition("partition-eu");
        await store.SaveAsync(NewBookmark("bookmark-1"));
        await workflowStateStore.SaveAsync(NewWorkflowExecution() with { Partition = partition });
        await executableStore.SaveAsync(NewExecutable());

        await dispatcher.DispatchAsync(NewDispatchRequest());

        Assert.Equal(partition, Assert.Single(agentProvider.Activations).Partition);
        Assert.Equal(partition, Assert.Single(agentProvider.Agent.Envelopes).Partition);
    }

    [Fact]
    public async Task DispatchAsync_TypedDeliveryCarriesProviderTypeAndStableDeduplicationMetadata()
    {
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var agentProvider = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(store, workflowStateStore, executableStore, agentProvider);
        await store.SaveAsync(NewBookmark("bookmark-1"));
        await workflowStateStore.SaveAsync(NewWorkflowExecution());
        await executableStore.SaveAsync(NewExecutable());
        var payloadType = new ValueTypeDescriptor("test/delivery-status", schemaVersion: 2);

        var result = await dispatcher.DispatchAsync(new BookmarkResumeDispatchRequest(
            workflowExecutionId: "wfexec-1",
            stimulusType: "delivery-status",
            stimulusHash: "sha256:delivery-status:order-123",
            input: Json("""{"orderId":"order-123"}"""),
            idempotencyKey: "provider-delivery-42",
            requestedBy: "adapter",
            payloadType: payloadType,
            providerId: "provider.delivery-status"));

        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, result.Status);
        var envelope = Assert.Single(agentProvider.Agent.Envelopes);
        var payload = envelope.Command.Payload!.Value.Deserialize<RuntimeResumeBookmarkCommandPayload>()!;
        var delivery = Assert.IsType<RuntimeTypedTriggerDeliveryMetadata>(payload.TriggerDelivery);
        Assert.Equal("command-1", delivery.DeliveryId);
        Assert.Equal(payloadType, delivery.PayloadType);
        Assert.Equal("provider.delivery-status", delivery.ProviderId);
        Assert.Equal(_now, delivery.ReceivedAt);
        Assert.Equal(envelope.IdempotencyKey, delivery.DeduplicationKey);
        Assert.Equal(WorkflowExecutionActorActivationReason.ResumeBookmark, Assert.Single(agentProvider.Activations).Reason);
    }

    [Fact]
    public async Task TestRunDeliveryId_AdmitsOnlyOneResumeAcrossConcurrentWaitAndConfigurationChanges()
    {
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var processor = new CountingCommandExecutor();
        using var agentProvider = new InProcessWorkflowExecutionActorProvider(processor);
        var dispatcher = NewDispatcher(store, workflowStateStore, executableStore, agentProvider);
        await store.SaveAsync(NewBookmark("wait-1", stimulusHash: "hash-1"));
        await store.SaveAsync(NewBookmark("wait-2", stimulusHash: "hash-2"));
        await workflowStateStore.SaveAsync(NewWorkflowExecution() with
        {
            RunKind = WorkflowRunKind.TestRun,
            TestScope = new("test-run", _now.AddMinutes(30), null, new("default"))
        });
        await executableStore.SaveAsync(NewExecutable());

        var deliveryId = Guid.NewGuid().ToString("D");
        var firstInput = Json("{\"value\":1}");
        var secondInput = Json("{\"value\":2}");
        var firstReceipt = new TestRunDeliveryReceipt("test-run", deliveryId, "wait-1",
            TestRunDeliveryReceipt.Digest(Json("{\"modeId\":\"event\",\"data\":{\"value\":1}}")),
            TestRunDeliveryReceipt.Digest(firstInput));
        var secondReceipt = new TestRunDeliveryReceipt("test-run", deliveryId, "wait-2",
            TestRunDeliveryReceipt.Digest(Json("{\"modeId\":\"event\",\"data\":{\"value\":2}}")),
            TestRunDeliveryReceipt.Digest(secondInput));

        var results = await Task.WhenAll(
            dispatcher.DispatchAsync(new BookmarkResumeDispatchRequest(
                "wfexec-1", "delivery-status", "hash-1", firstInput,
                idempotencyKey: null, requestedBy: "runtime.bookmark", metadata: null,
                payloadType: null, providerId: null,
                bookmarkId: "wait-1", testRunDelivery: firstReceipt)).AsTask(),
            dispatcher.DispatchAsync(new BookmarkResumeDispatchRequest(
                "wfexec-1", "delivery-status", "hash-2", secondInput,
                idempotencyKey: null, requestedBy: "runtime.bookmark", metadata: null,
                payloadType: null, providerId: null,
                bookmarkId: "wait-2", testRunDelivery: secondReceipt)).AsTask());

        Assert.Contains(results, result => result.Status == BookmarkResumeDispatchStatus.Dispatched);
        Assert.Contains(results, result => result.Status == BookmarkResumeDispatchStatus.Duplicate);
        Assert.Equal(1, processor.ProcessCalls);
    }

    [Fact]
    public async Task DispatchAsync_TypedMetadataCannotBypassExistingBookmarkProviderRecognition()
    {
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var agentProvider = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(store, workflowStateStore, executableStore, agentProvider);
        await store.SaveAsync(NewBookmark("bookmark-1"));
        await workflowStateStore.SaveAsync(NewWorkflowExecution());
        await executableStore.SaveAsync(NewExecutable());

        var result = await dispatcher.DispatchAsync(new BookmarkResumeDispatchRequest(
            workflowExecutionId: "wfexec-1",
            stimulusType: "unrecognized-provider-type",
            stimulusHash: "sha256:delivery-status:order-123",
            input: Json("""{"orderId":"order-123"}"""),
            payloadType: new ValueTypeDescriptor("test/delivery-status"),
            providerId: "provider.delivery-status"));

        Assert.Equal(BookmarkResumeDispatchStatus.NotFound, result.Status);
        Assert.Empty(agentProvider.Activations);
        Assert.Empty(agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_ForwardsDispatchOptionsToAgentEnqueue()
    {
        // The resume dispatcher forwards the caller's dispatch options verbatim
        // into the agent mailbox so a sync-authored endpoint's resume can author the live response on the caller's
        // async flow using the ambient request scope.
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var agentProvider = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(store, workflowStateStore, executableStore, agentProvider);
        await store.SaveAsync(NewBookmark("bookmark-1"));
        await workflowStateStore.SaveAsync(NewWorkflowExecution());
        await executableStore.SaveAsync(NewExecutable());
        var options = new WorkflowExecutionCommandDispatchOptions();

        await dispatcher.DispatchAsync(NewDispatchRequest(), options);

        Assert.Same(options, Assert.Single(agentProvider.Agent.DispatchOptions));
    }

    [Fact]
    public async Task DispatchAsync_WithoutDispatchOptions_EnqueuesDefaultOptions()
    {
        // Absent options ⇒ the dispatcher enqueues WorkflowExecutionCommandDispatchOptions.Default, pinning the
        // pre-089 single-arg behavior (no ambient services attached).
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var agentProvider = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(store, workflowStateStore, executableStore, agentProvider);
        await store.SaveAsync(NewBookmark("bookmark-1"));
        await workflowStateStore.SaveAsync(NewWorkflowExecution());
        await executableStore.SaveAsync(NewExecutable());

        await dispatcher.DispatchAsync(NewDispatchRequest());

        var options = Assert.Single(agentProvider.Agent.DispatchOptions);
        Assert.Same(WorkflowExecutionCommandDispatchOptions.Default, options);
        Assert.Null(options!.AmbientServices);
    }

    [Fact]
    public async Task DispatchAsync_DoesNotEnqueueWhenWorkflowStateIsMissing()
    {
        var store = new InMemoryBookmarkStateStore();
        var agentProvider = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(store, new InMemoryWorkflowExecutionStateStore(), new InMemoryWorkflowExecutableStore(), agentProvider);
        await store.SaveAsync(NewBookmark("bookmark-1"));

        var result = await dispatcher.DispatchAsync(NewDispatchRequest());

        Assert.Equal(BookmarkResumeDispatchStatus.WorkflowExecutionMissing, result.Status);
        Assert.Empty(agentProvider.Activations);
        Assert.Empty(agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_DoesNotEnqueueWhenExecutableArtifactIsMissing()
    {
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var agentProvider = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(store, workflowStateStore, new InMemoryWorkflowExecutableStore(), agentProvider);
        await store.SaveAsync(NewBookmark("bookmark-1"));
        await workflowStateStore.SaveAsync(NewWorkflowExecution());

        var result = await dispatcher.DispatchAsync(NewDispatchRequest());

        Assert.Equal(BookmarkResumeDispatchStatus.ExecutableMissing, result.Status);
        Assert.Empty(agentProvider.Activations);
        Assert.Empty(agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_DoesNotEnqueueWhenResumeTargetIsMissing()
    {
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var agentProvider = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(store, workflowStateStore, executableStore, agentProvider);
        await store.SaveAsync(NewBookmark("bookmark-1"));
        await workflowStateStore.SaveAsync(NewWorkflowExecution());
        await executableStore.SaveAsync(NewExecutable(resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>()));

        var result = await dispatcher.DispatchAsync(NewDispatchRequest());

        Assert.Equal(BookmarkResumeDispatchStatus.ResumeResolutionFailed, result.Status);
        Assert.Contains("Resume target 'resume-target:delivery' is not declared", result.Reason);
        Assert.Empty(agentProvider.Activations);
        Assert.Empty(agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task DispatchAsync_DoesNotEnqueueWhenBookmarkLookupIsAmbiguous()
    {
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var agentProvider = new RecordingWorkflowExecutionActorProvider();
        var dispatcher = NewDispatcher(store, workflowStateStore, executableStore, agentProvider);
        await store.SaveAsync(NewBookmark("bookmark-a"));
        await store.SaveAsync(NewBookmark("bookmark-b"));
        await workflowStateStore.SaveAsync(NewWorkflowExecution());
        await executableStore.SaveAsync(NewExecutable());

        var result = await dispatcher.DispatchAsync(NewDispatchRequest());

        Assert.Equal(BookmarkResumeDispatchStatus.Ambiguous, result.Status);
        Assert.Empty(agentProvider.Activations);
        Assert.Empty(agentProvider.Agent.Envelopes);
    }

    private BookmarkResumeDispatcher NewDispatcher(
        IBookmarkStateStore bookmarkStateStore,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutionActorProvider agentProvider) =>
        new(
            new BookmarkStimulusLookup(bookmarkStateStore),
            workflowExecutionStateStore,
            executableStore,
            new BookmarkResumeResolver(),
            agentProvider,
            new FixedRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(_now));

    private BookmarkStimulusLookupRequest NewLookupRequest() =>
        new("wfexec-1", "delivery-status", "sha256:delivery-status:order-123", _now);

    private BookmarkResumeDispatchRequest NewDispatchRequest() =>
        new("wfexec-1", "delivery-status", "sha256:delivery-status:order-123");

    private BookmarkState NewBookmark(
        string bookmarkId,
        string stimulusType = "delivery-status",
        string stimulusHash = "sha256:delivery-status:order-123",
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null) =>
        new(
            BookmarkId: bookmarkId,
            WorkflowExecutionId: "wfexec-1",
            ActivityExecutionId: "actexec-1",
            ExecutableNodeId: "node-wait",
            ResumeTargetId: "resume-target:delivery",
            StimulusType: stimulusType,
            StimulusHash: stimulusHash,
            Payload: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: createdAt ?? _now,
            ExpiresAt: expiresAt);

    private WorkflowExecutionState NewWorkflowExecution() =>
        new(
            WorkflowExecutionId: "wfexec-1",
            PinnedExecutable: _identity,
            Status: WorkflowExecutionStatus.Suspended,
            SubStatus: "BookmarkWaiting",
            CreatedAt: _now.AddMinutes(-5),
            StartedAt: _now.AddMinutes(-4),
            UpdatedAt: _now.AddMinutes(-1),
            CompletedAt: null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());

    private WorkflowExecutable NewExecutable(IReadOnlyDictionary<string, WorkflowExecutableResumeTarget>? resumeTargets = null) =>
        new(
            identity: _identity,
            rootActivity: NewNode("node-wait"),
            resumeTargets: resumeTargets ?? new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                ["resume-target:delivery"] = new(
                    ResumeTargetId: "resume-target:delivery",
                    ExecutableNodeId: "node-wait",
                    HandlerKey: "delivery-handler",
                    Metadata: new Dictionary<string, string>())
            },
            createdAt: _now.AddMinutes(-10),
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);

    private static ExecutableNode NewNode(string nodeId)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class RecordingWorkflowExecutionActorProvider : IWorkflowExecutionActorProvider
    {
        public RecordingWorkflowExecutionActor Agent { get; } = new();
        public List<WorkflowExecutionActorActivationRequest> Activations { get; } = [];
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.InProcessMailbox;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            Activations.Add(request);
            return new(Agent);
        }

        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingWorkflowExecutionActor : IWorkflowExecutionActor
    {
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];
        public List<WorkflowExecutionCommandDispatchOptions?> DispatchOptions { get; } = [];

        public WorkflowExecutionActorDescriptor Descriptor { get; } = new(
            workflowExecutionId: "wfexec-1",
            agentId: "agent-1",
            providerName: "recording",
            status: WorkflowExecutionActorStatus.Active,
            capabilities: WorkflowExecutionActorCapabilities.InProcessMailbox,
            activatedAt: DateTimeOffset.UnixEpoch);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
            Record(envelope, null, cancellationToken);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, WorkflowExecutionCommandDispatchOptions options, CancellationToken cancellationToken = default) =>
            Record(envelope, options, cancellationToken);

        private ValueTask<WorkflowExecutionCommandDispatchResult> Record(WorkflowExecutionCommandEnvelope envelope, WorkflowExecutionCommandDispatchOptions? options, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            cancellationToken.ThrowIfCancellationRequested();

            Envelopes.Add(envelope);
            DispatchOptions.Add(options);
            return new(new WorkflowExecutionCommandDispatchResult(
                envelopeId: envelope.EnvelopeId,
                workflowExecutionId: envelope.WorkflowExecutionId,
                status: WorkflowExecutionCommandDispatchStatus.Accepted,
                recordedAt: DateTimeOffset.UnixEpoch));
        }
    }

    private sealed class FixedRuntimeExecutionIdGenerator : IRuntimeExecutionIdGenerator
    {
        public string NewWorkflowExecutionId() => "wfexec-new";

        public string NewWorkflowExecutionCommandId() => "command-1";

        public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-1";

        public string NewActivityExecutionId() => "actexec-new";
    }

    private sealed class CountingCommandExecutor : IWorkflowExecutionCommandExecutor
    {
        public int ProcessCalls { get; private set; }

        public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(
            WorkflowExecutionCommandEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProcessCalls++;
            return ValueTask.FromResult(WorkflowExecutionCommandProcessResult.NoDrain);
        }
    }
}

using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

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
        var agentProvider = new RecordingWorkflowExecutionAgentProvider();
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
        Assert.Equal(WorkflowExecutionAgentActivationReason.ResumeBookmark, activation.Reason);
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
    public async Task DispatchAsync_UsesCallerIdempotencyKeyAsDeterministicSuffix()
    {
        var store = new InMemoryBookmarkStateStore();
        var workflowStateStore = new InMemoryWorkflowExecutionStateStore();
        var executableStore = new InMemoryWorkflowExecutableStore();
        var agentProvider = new RecordingWorkflowExecutionAgentProvider();
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
    public async Task DispatchAsync_DoesNotEnqueueWhenWorkflowStateIsMissing()
    {
        var store = new InMemoryBookmarkStateStore();
        var agentProvider = new RecordingWorkflowExecutionAgentProvider();
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
        var agentProvider = new RecordingWorkflowExecutionAgentProvider();
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
        var agentProvider = new RecordingWorkflowExecutionAgentProvider();
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
        var agentProvider = new RecordingWorkflowExecutionAgentProvider();
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
        IWorkflowExecutionAgentProvider agentProvider) =>
        new(
            new BookmarkStimulusLookup(bookmarkStateStore),
            workflowExecutionStateStore,
            executableStore,
            new BookmarkResumeResolver(),
            agentProvider,
            new FixedRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(_now));

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
            publishedAt: _now.AddMinutes(-9),
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode NewNode(string nodeId)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class RecordingWorkflowExecutionAgentProvider : IWorkflowExecutionAgentProvider
    {
        public RecordingWorkflowExecutionAgent Agent { get; } = new();
        public List<WorkflowExecutionAgentActivationRequest> Activations { get; } = [];
        public WorkflowExecutionAgentCapabilities Capabilities => WorkflowExecutionAgentCapabilities.InProcessMailbox;

        public ValueTask<IWorkflowExecutionAgent> GetAgentAsync(WorkflowExecutionAgentActivationRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            Activations.Add(request);
            return new(Agent);
        }

        public ValueTask PassivateAsync(WorkflowExecutionAgentPassivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingWorkflowExecutionAgent : IWorkflowExecutionAgent
    {
        public List<WorkflowExecutionCommandEnvelope> Envelopes { get; } = [];

        public WorkflowExecutionAgentDescriptor Descriptor { get; } = new(
            workflowExecutionId: "wfexec-1",
            agentId: "agent-1",
            providerName: "recording",
            status: WorkflowExecutionAgentStatus.Active,
            capabilities: WorkflowExecutionAgentCapabilities.InProcessMailbox,
            activatedAt: DateTimeOffset.UnixEpoch);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            cancellationToken.ThrowIfCancellationRequested();

            Envelopes.Add(envelope);
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

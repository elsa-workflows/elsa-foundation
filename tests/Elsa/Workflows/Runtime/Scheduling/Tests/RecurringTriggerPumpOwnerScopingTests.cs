using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

/// <summary>
/// Pins the pump's owner-scoped StartOnly dispatch through the REAL <see cref="StimulusRouter"/> and trigger
/// binding store: a recurring schedule firing must start ONLY the workflow that owns the schedule, never every
/// published workflow whose Timer/Cron trigger happens to share the same (StimulusType, StimulusHash) — the
/// cross-artifact "two workflows with Timer(PT1M) double-start each other" routing collision. Externally-sent
/// stimuli (e.g. Event) keep their intentional cross-artifact fan-out; only pump-internal recurring dispatch
/// is scoped (see StimulusRouterTests.Route_StartOnly_StartsOneInstancePerMatchingTrigger for the fan-out pin).
/// </summary>
public sealed class RecurringTriggerPumpOwnerScopingTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private const string SharedHash = "sha256:interval:pt1m";

    private readonly InMemoryRecurringTriggerScheduleStore _scheduleStore = new();
    private readonly InMemoryWorkflowTriggerBindingStore _bindingStore = new();
    private readonly RecordingStartDispatcher _startDispatcher = new();
    private readonly RecurringTriggerPumpTask _pump;

    public RecurringTriggerPumpOwnerScopingTests()
    {
        var router = new StimulusRouter(
            _bindingStore,
            new GlobalBookmarkStimulusLookup(new InMemoryBookmarkStateStore()),
            _startDispatcher,
            new NoResumeDispatcher(),
            new InMemoryStimulusStartDeduplicator(),
            new FixedTimeProvider(Now));
        var options = Microsoft.Extensions.Options.Options.Create(new RecurringTriggerPumpOptions
        {
            SweepInterval = TimeSpan.FromSeconds(10),
            MaxBackoffInterval = TimeSpan.FromMinutes(5),
            MaxSchedulesPerTick = 100
        });
        _pump = new RecurringTriggerPumpTask(
            _scheduleStore,
            _bindingStore,
            router,
            new RecurringScheduleCalculator(),
            options,
            new FixedTimeProvider(Now),
            NullLogger<RecurringTriggerPumpTask>.Instance);
    }

    [Fact]
    public async Task FiringOneSchedule_StartsOnlyTheOwningArtifact_NotEveryHashMatch()
    {
        // Two published workflows authored the same interval → identical (StimulusType, StimulusHash).
        await _bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        await _bindingStore.SaveAsync(Binding("artifact-2", "node-a"));
        // Only artifact-1's schedule is due; artifact-2's fires later.
        await _scheduleStore.SaveAsync(Schedule("artifact-1", "node-a", Now.AddMinutes(-1)));
        await _scheduleStore.SaveAsync(Schedule("artifact-2", "node-a", Now.AddMinutes(3)));

        await _pump.ExecuteAsync(CancellationToken.None);

        var started = Assert.Single(_startDispatcher.Requests);
        Assert.Equal("artifact-1", started.ArtifactId);
    }

    [Fact]
    public async Task TwoSameHashSchedules_DueTogether_EachStartsItsOwnArtifactOnce()
    {
        await _bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        await _bindingStore.SaveAsync(Binding("artifact-2", "node-a"));
        await _scheduleStore.SaveAsync(Schedule("artifact-1", "node-a", Now.AddMinutes(-1)));
        await _scheduleStore.SaveAsync(Schedule("artifact-2", "node-a", Now.AddMinutes(-1)));

        await _pump.ExecuteAsync(CancellationToken.None);

        // One start per schedule, each for its own artifact — not the 4 (2 fires × 2 hash matches) the
        // unscoped hash-broadcast produced.
        Assert.Equal(2, _startDispatcher.Requests.Count);
        Assert.Equal(
            ["artifact-1", "artifact-2"],
            _startDispatcher.Requests.Select(request => request.ArtifactId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task SameArtifact_TwoTriggerNodesWithSameHash_EachFireStartsOnlyItsOwnNode()
    {
        // One workflow with TWO Timer start triggers authored to the same interval: same (type, hash) on two
        // nodes of one artifact. Each node's schedule fire must start exactly one instance, attributed to its
        // own trigger node.
        await _bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        await _bindingStore.SaveAsync(Binding("artifact-1", "node-b"));
        await _scheduleStore.SaveAsync(Schedule("artifact-1", "node-a", Now.AddMinutes(-1)));
        await _scheduleStore.SaveAsync(Schedule("artifact-1", "node-b", Now.AddMinutes(-1)));

        await _pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, _startDispatcher.Requests.Count);
        Assert.Equal(
            ["node-a", "node-b"],
            _startDispatcher.Requests.Select(request => request.TriggerNodeId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task PublicationScopedSchedule_StartsOnlyItsOwnPublication()
    {
        // Named slots may share one artifact: the same trigger node exists under two publications. A schedule
        // owned by publication-1 must start through publication-1's binding only.
        await _bindingStore.SaveAsync(Binding("artifact-1", "node-a", "publication-1", "slot-blue"));
        await _bindingStore.SaveAsync(Binding("artifact-1", "node-a", "publication-2", "slot-green"));
        await _scheduleStore.SaveAsync(Schedule("artifact-1", "node-a", Now.AddMinutes(-1), "publication-1", "slot-blue"));

        await _pump.ExecuteAsync(CancellationToken.None);

        var started = Assert.Single(_startDispatcher.Requests);
        Assert.Equal("publication-1", started.SourceSelection!.PublicationId);
        Assert.Equal("slot-blue", started.SourceSelection.SlotId);
    }

    private static RecurringTriggerSchedule Schedule(
        string artifactId,
        string nodeId,
        DateTimeOffset next,
        string? publicationId = null,
        string? slotId = null) => new(
        ScheduleId: publicationId is null
            ? RecurringTriggerSchedule.BuildId(artifactId, nodeId)
            : RecurringTriggerSchedule.BuildId(publicationId, artifactId, nodeId),
        ArtifactId: artifactId,
        ExecutableNodeId: nodeId,
        StimulusType: "Timer",
        StimulusHash: SharedHash,
        Kind: RecurringScheduleKind.Interval,
        Expression: "PT1M",
        NextOccurrence: next,
        CreatedAt: Now,
        PublicationId: publicationId,
        SlotId: slotId);

    private static WorkflowTriggerBinding Binding(
        string artifactId,
        string nodeId,
        string? publicationId = null,
        string? slotId = null) => new(
        TriggerBindingId: publicationId is null
            ? WorkflowTriggerBinding.BuildId(artifactId, nodeId, SharedHash)
            : WorkflowTriggerBinding.BuildId(publicationId, artifactId, nodeId, SharedHash),
        ArtifactId: artifactId,
        DefinitionId: "definition-1",
        ArtifactVersion: "1.0.0",
        ArtifactHash: "sha256:artifact",
        ExecutableNodeId: nodeId,
        StimulusType: "Timer",
        StimulusHash: SharedHash,
        CorrelationScope: null,
        Metadata: new Dictionary<string, string>(),
        CreatedAt: Now,
        PublicationId: publicationId,
        SlotId: slotId);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingStartDispatcher : IWorkflowStartDispatcher
    {
        private int _counter;
        public List<WorkflowExecutionStartDispatchRequest> Requests { get; } = [];

        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var executionId = $"wfexec-new-{++_counter}";
            return new ValueTask<WorkflowExecutionStartDispatchResult>(new WorkflowExecutionStartDispatchResult(
                executionId,
                new WorkflowExecutableIdentity(request.ArtifactId, "definition-1", "version-1", "1.0.0", "sha256:artifact"),
                new WorkflowExecutionCommandDispatchResult(
                    envelopeId: $"envelope-{executionId}",
                    workflowExecutionId: executionId,
                    status: WorkflowExecutionCommandDispatchStatus.Accepted,
                    recordedAt: DateTimeOffset.UnixEpoch),
                new WorkflowExecutionActorDescriptor(
                    workflowExecutionId: executionId,
                    agentId: $"agent-{executionId}",
                    providerName: "recording",
                    status: WorkflowExecutionActorStatus.Active,
                    capabilities: WorkflowExecutionActorCapabilities.InProcessMailbox,
                    activatedAt: DateTimeOffset.UnixEpoch)));
        }
    }

    /// <summary>StartOnly pump dispatch must never reach the resume path.</summary>
    private sealed class NoResumeDispatcher : IBookmarkResumeDispatcher
    {
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
            BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The pump's StartOnly dispatch must not resume anything.");
    }
}

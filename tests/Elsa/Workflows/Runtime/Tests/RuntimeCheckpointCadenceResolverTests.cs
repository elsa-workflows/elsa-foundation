using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Unit matrix for the per-execution checkpoint-cadence resolver (ADR 0032 R5). Precedence under test:
/// per-run stamp &gt; per-workflow authored cadence (read off the pinned executable) &gt; host default; and the honest
/// reachability boundary — authored Coalesced on an Immediate host clamps to Immediate because the coalescing
/// machinery is not registered. The mandatory-boundary override lives in the commit layer and is covered by the
/// coalescing policy/store tests, not here.
/// </summary>
public sealed class RuntimeCheckpointCadenceResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryWorkflowExecutionStateStore _stateStore = new();

    private RuntimeCheckpointCadenceResolver Resolver(int? hostCap) =>
        new(
            _executableStore,
            _stateStore,
            hostCap is { } cap
                ? [new CoalescingRuntimeCheckpointPersistenceOptions { MaxSegmentCheckpoints = cap }]
                : []);

    [Fact]
    public void AuthoredNone_UsesHostDefault()
    {
        Assert.Equal(ResolvedCheckpointCadence.Immediate, Resolver(hostCap: null).Resolve(Executable(null)));
        Assert.Equal(ResolvedCheckpointCadence.CoalescedWith(50), Resolver(hostCap: 50).Resolve(Executable(null)));
    }

    [Fact]
    public void AuthoredImmediate_OverridesCoalescedHost()
    {
        var authored = new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.ImmediateMode);
        Assert.Equal(ResolvedCheckpointCadence.Immediate, Resolver(hostCap: 50).Resolve(Executable(authored)));
    }

    [Fact]
    public void AuthoredCoalesced_OnCoalescedHost_UsesAuthoredCapOverHostCap()
    {
        var authored = new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.CoalescedMode, 8);
        Assert.Equal(ResolvedCheckpointCadence.CoalescedWith(8), Resolver(hostCap: 50).Resolve(Executable(authored)));
    }

    [Fact]
    public void AuthoredCoalesced_WithoutOwnCap_FallsBackToHostCap()
    {
        var authored = new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.CoalescedMode);
        Assert.Equal(ResolvedCheckpointCadence.CoalescedWith(50), Resolver(hostCap: 50).Resolve(Executable(authored)));
    }

    [Fact]
    public void AuthoredCoalesced_OnImmediateHost_ClampsToImmediate()
    {
        // Honest reachability: without AddCoalescingRuntimeCheckpointPersistence the shell has no coalescing session,
        // decorated stores, or policy, so authored Coalesced degrades to extra durability rather than pretending.
        var authored = new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.CoalescedMode, 8);
        Assert.Equal(ResolvedCheckpointCadence.Immediate, Resolver(hostCap: null).Resolve(Executable(authored)));
    }

    [Fact]
    public async Task ResolveAsync_PrefersThePerRunStamp_OverExecutableAndHost()
    {
        // The run stamped Immediate at start; a later host reconfiguration to Coalesced (or the executable's authored
        // Coalesced) must not change what this run executes under on resume drains.
        await _stateStore.SaveAsync(State(stampMode: WorkflowExecutableCheckpointCadence.ImmediateMode));
        await _executableStore.SaveAsync(Executable(
            new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.CoalescedMode, 8)));

        var resolved = await Resolver(hostCap: 50).ResolveAsync(Envelope());

        Assert.Equal(ResolvedCheckpointCadence.Immediate, resolved);
    }

    [Fact]
    public async Task ResolveAsync_ReadsCoalescedStampWithItsCap()
    {
        await _stateStore.SaveAsync(State(stampMode: WorkflowExecutableCheckpointCadence.CoalescedMode, stampCap: "8"));

        var resolved = await Resolver(hostCap: 50).ResolveAsync(Envelope());

        Assert.Equal(ResolvedCheckpointCadence.CoalescedWith(8), resolved);
    }

    [Fact]
    public async Task ResolveAsync_OnFirstDrain_ResolvesFromThePinnedExecutable_ViaEnvelopeArtifactId()
    {
        // No state row yet (first drain of a new run): the artifact-id breadcrumb on the command metadata locates the
        // pinned executable, whose authored cadence wins over the host default.
        await _executableStore.SaveAsync(Executable(
            new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.ImmediateMode)));

        var resolved = await Resolver(hostCap: 50).ResolveAsync(Envelope(artifactIdMetadata: "artifact-1"));

        Assert.Equal(ResolvedCheckpointCadence.Immediate, resolved);
    }

    [Fact]
    public async Task ResolveAsync_UnstampedLegacyState_FallsBackToThePinnedExecutable()
    {
        await _stateStore.SaveAsync(State(stampMode: null));
        await _executableStore.SaveAsync(Executable(
            new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.ImmediateMode)));

        var resolved = await Resolver(hostCap: 50).ResolveAsync(Envelope());

        Assert.Equal(ResolvedCheckpointCadence.Immediate, resolved);
    }

    [Fact]
    public async Task ResolveAsync_NoStateNoArtifactId_UsesHostDefault()
    {
        var resolved = await Resolver(hostCap: 50).ResolveAsync(Envelope());

        Assert.Equal(ResolvedCheckpointCadence.CoalescedWith(50), resolved);
    }

    private static WorkflowExecutable Executable(WorkflowExecutableCheckpointCadence? cadence)
    {
        var node = new ExecutableNode(
            executableNodeId: "node-1",
            authoredActivityId: "authored-node-1",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(
                "test",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                System.Text.Json.JsonSerializer.SerializeToElement(new { type = "test" })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            checkpointCadence: cadence);
    }

    private static WorkflowExecutionState State(string? stampMode, string? stampCap = null)
    {
        var metadata = new Dictionary<string, string>();
        if (stampMode is not null)
            metadata[RuntimeMetadataKeys.CheckpointCadence] = stampMode;
        if (stampCap is not null)
            metadata[RuntimeMetadataKeys.CheckpointMaxSegmentCheckpoints] = stampCap;

        return new(
            WorkflowExecutionId: "wfexec-1",
            PinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            Status: WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: Now,
            StartedAt: Now,
            UpdatedAt: Now,
            CompletedAt: null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: metadata);
    }

    private static WorkflowExecutionCommandEnvelope Envelope(string? artifactIdMetadata = null)
    {
        var metadata = new Dictionary<string, string> { ["source"] = "test" };
        if (artifactIdMetadata is not null)
            metadata["runtime.artifactId"] = artifactIdMetadata;

        var command = new WorkflowExecutionCommand(
            CommandId: "command-1",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: Now,
            Payload: null,
            Metadata: metadata);

        return new(
            envelopeId: "envelope-1",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:test",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now);
    }
}

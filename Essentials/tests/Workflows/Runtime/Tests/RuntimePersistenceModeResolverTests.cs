using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Unit matrix for the per-execution persistence-mode resolver. Precedence under test: the mode the run recorded at
/// start, then the workflow's chosen mode (read off the pinned executable), then the host default. Boundary checkpoints
/// flush in every mode; that guarantee lives in the commit layer and is covered by the folding policy/store tests.
/// </summary>
public sealed class RuntimePersistenceModeResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryWorkflowExecutionStateStore _stateStore = new();

    private RuntimePersistenceModeResolver Resolver(PersistenceMode? hostMode) =>
        new(
            _executableStore,
            _stateStore,
            hostMode is { } mode
                ? [new PersistenceModeOptions { Mode = mode }]
                : []);

    [Fact]
    public void NoChoice_UsesHostDefault()
    {
        Assert.Equal(PersistenceMode.Durable, Resolver(hostMode: null).Resolve(Executable(null)));
        Assert.Equal(PersistenceMode.Balanced, Resolver(hostMode: PersistenceMode.Balanced).Resolve(Executable(null)));
        Assert.Equal(PersistenceMode.Durable, Resolver(hostMode: PersistenceMode.Durable).Resolve(Executable(null)));
    }

    [Fact]
    public void ChosenDurable_OverridesBalancedHost()
    {
        Assert.Equal(PersistenceMode.Durable, Resolver(hostMode: PersistenceMode.Balanced).Resolve(Executable(PersistenceMode.Durable)));
    }

    [Fact]
    public void ChosenBalanced_OnBalancedHost_IsBalanced()
    {
        Assert.Equal(PersistenceMode.Balanced, Resolver(hostMode: PersistenceMode.Balanced).Resolve(Executable(PersistenceMode.Balanced)));
    }

    [Fact]
    public void ChosenBalanced_OnHostWithoutPersistenceModes_ClampsToDurable()
    {
        Assert.Equal(PersistenceMode.Durable, Resolver(hostMode: null).Resolve(Executable(PersistenceMode.Balanced)));
    }

    [Fact]
    public async Task ResolveAsync_PrefersTheRecordedMode_OverExecutableAndHost()
    {
        // The run recorded Durable at start; a later host reconfiguration to Balanced (or the executable's chosen
        // Balanced) must not change what this run executes under on later drains.
        await _stateStore.SaveAsync(State(recordedMode: nameof(PersistenceMode.Durable)));
        await _executableStore.SaveAsync(Executable(PersistenceMode.Balanced));

        var resolved = await Resolver(hostMode: PersistenceMode.Balanced).ResolveAsync(Envelope());

        Assert.Equal(new ResolvedPersistenceMode(PersistenceMode.Durable), resolved);
    }

    [Fact]
    public async Task ResolveAsync_ReadsARecordedBalancedMode()
    {
        await _stateStore.SaveAsync(State(recordedMode: nameof(PersistenceMode.Balanced)));

        var resolved = await Resolver(hostMode: PersistenceMode.Durable).ResolveAsync(Envelope());

        Assert.Equal(new ResolvedPersistenceMode(PersistenceMode.Balanced), resolved);
    }

    [Fact]
    public async Task ResolveAsync_OnFirstDrain_ResolvesFromThePinnedExecutable_ViaEnvelopeArtifactId()
    {
        // No state row yet (first drain of a new run): the artifact-id breadcrumb on the command metadata locates the
        // pinned executable, whose chosen mode wins over the host default.
        await _executableStore.SaveAsync(Executable(PersistenceMode.Durable));

        var resolved = await Resolver(hostMode: PersistenceMode.Balanced).ResolveAsync(Envelope(artifactIdMetadata: "artifact-1"));

        Assert.Equal(new ResolvedPersistenceMode(PersistenceMode.Durable), resolved);
    }

    [Fact]
    public async Task ResolveAsync_StateWithoutRecordedMode_FallsBackToThePinnedExecutable()
    {
        await _stateStore.SaveAsync(State(recordedMode: null));
        await _executableStore.SaveAsync(Executable(PersistenceMode.Durable));

        var resolved = await Resolver(hostMode: PersistenceMode.Balanced).ResolveAsync(Envelope());

        Assert.Equal(new ResolvedPersistenceMode(PersistenceMode.Durable), resolved);
    }

    [Fact]
    public async Task ResolveAsync_NoStateNoArtifactId_UsesHostDefault()
    {
        var resolved = await Resolver(hostMode: PersistenceMode.Balanced).ResolveAsync(Envelope());

        Assert.Equal(new ResolvedPersistenceMode(PersistenceMode.Balanced), resolved);
    }

    private static WorkflowExecutable Executable(PersistenceMode? persistenceMode)
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
            persistenceMode: persistenceMode);
    }

    private static WorkflowExecutionState State(string? recordedMode)
    {
        var metadata = new Dictionary<string, string>();
        if (recordedMode is not null)
            metadata[RuntimeMetadataKeys.PersistenceMode] = recordedMode;

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

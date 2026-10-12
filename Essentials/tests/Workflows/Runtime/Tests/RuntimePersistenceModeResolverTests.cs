using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Unit matrix for the per-execution persistence-mode resolver, as the runtime composes it. A run's mode is the one its
/// first commit recorded; before that, the one its start carries, read off the start command or, for any other command,
/// off the start the queue still holds; a start without one is resolved from its pinned workflow, whose choice wins over
/// the host default. A run whose mode cannot be determined is refused. Boundary checkpoints flush in every mode; that
/// guarantee lives in the commit layer and is covered by the folding policy/store tests.
/// </summary>
public sealed class RuntimePersistenceModeResolverTests : IAsyncDisposable
{
    private const string WorkflowExecutionId = "wfexec-1";
    private const string ArtifactId = "artifact-1";
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider? _provider;

    public ValueTask DisposeAsync() => _provider?.DisposeAsync() ?? ValueTask.CompletedTask;

    public static TheoryData<PersistenceMode, PersistenceMode> ChosenAndHostModes()
    {
        var data = new TheoryData<PersistenceMode, PersistenceMode>();
        foreach (var host in Enum.GetValues<PersistenceMode>())
        foreach (var chosen in Enum.GetValues<PersistenceMode>())
            data.Add(host, chosen);
        return data;
    }

    [Theory]
    [MemberData(nameof(ChosenAndHostModes))]
    public async Task TheWorkflowsChoiceWinsOverEveryHostDefault(PersistenceMode hostMode, PersistenceMode chosen)
    {
        var resolver = Compose(hostMode);
        await SaveExecutableAsync(chosen);

        Assert.Equal(chosen, resolver.Resolve(Executable(chosen)));
        Assert.Equal(new ResolvedPersistenceMode(chosen, RunHasRecord: false), await resolver.ResolveAsync(StartEnvelope()));
    }

    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task AWorkflowThatChoosesNothingRunsTheHostDefault(PersistenceMode hostMode)
    {
        var resolver = Compose(hostMode);
        await SaveExecutableAsync(chosen: null);

        Assert.Equal(hostMode, resolver.Resolve(Executable(null)));
        Assert.Equal(new ResolvedPersistenceMode(hostMode, RunHasRecord: false), await resolver.ResolveAsync(StartEnvelope()));
    }

    [Fact]
    public async Task TheRecordedModeWinsOverTheWorkflowAndTheHost()
    {
        // The run recorded Durable at its first commit; a later host reconfiguration to Balanced (or the executable's
        // chosen Balanced) must not change what this run executes under on later drains.
        var resolver = Compose(PersistenceMode.Balanced);
        await SaveStateAsync(recordedMode: nameof(PersistenceMode.Durable));
        await SaveExecutableAsync(PersistenceMode.Balanced);

        Assert.Equal(new ResolvedPersistenceMode(PersistenceMode.Durable, RunHasRecord: true), await resolver.ResolveAsync(RedriveEnvelope()));
    }

    [Fact]
    public async Task AStartCommandThatCarriesAModeIsResolvedToIt_WhateverTheWorkflowAndHostSay()
    {
        // The start dispatcher decided the mode when it started the run; that decision holds even when the host default
        // has changed since, and without reading the workflow again.
        var resolver = Compose(PersistenceMode.Ephemeral);

        Assert.Equal(
            new ResolvedPersistenceMode(PersistenceMode.Balanced, RunHasRecord: false),
            await resolver.ResolveAsync(StartEnvelope(stampedMode: nameof(PersistenceMode.Balanced))));
    }

    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    [InlineData(PersistenceMode.Ephemeral)]
    public async Task AnyOtherCommandForARunNeverCommitted_ResolvesTheModeOfTheStartTheQueueHolds(PersistenceMode chosen)
    {
        // A drain the resumption sweep starts carries nothing but its source. The run's start is still queued, because
        // nothing of the run was committed, and it decides the mode.
        var resolver = Compose(chosen == PersistenceMode.Ephemeral ? PersistenceMode.Balanced : PersistenceMode.Ephemeral);
        await QueueAsync(StartEnvelope(stampedMode: chosen.ToString()));

        Assert.Equal(new ResolvedPersistenceMode(chosen, RunHasRecord: false), await resolver.ResolveAsync(RedriveEnvelope()));
    }

    [Fact]
    public async Task AQueuedStartWithoutAMode_IsResolvedFromItsPinnedWorkflow()
    {
        var resolver = Compose(PersistenceMode.Ephemeral);
        await SaveExecutableAsync(PersistenceMode.Durable);
        await QueueAsync(StartEnvelope());

        Assert.Equal(new ResolvedPersistenceMode(PersistenceMode.Durable, RunHasRecord: false), await resolver.ResolveAsync(RedriveEnvelope()));
    }

    [Fact]
    public async Task ARecordWithoutAMode_IsResolvedFromItsPinnedWorkflow()
    {
        // A run faulted into existence by a start that carried no mode.
        var resolver = Compose(PersistenceMode.Balanced);
        await SaveStateAsync(recordedMode: null);
        await SaveExecutableAsync(PersistenceMode.Durable);

        Assert.Equal(new ResolvedPersistenceMode(PersistenceMode.Durable, RunHasRecord: true), await resolver.ResolveAsync(RedriveEnvelope()));
    }

    [Fact]
    public async Task ACommandForARunWithNeitherARecordNorAQueuedStart_RunsTheHostDefault()
    {
        // Nothing of the run is durable, so there is no choice to keep: a command for a run that never started here, or
        // for an Ephemeral run that completed without a record.
        var resolver = Compose(PersistenceMode.Balanced);

        Assert.Equal(new ResolvedPersistenceMode(PersistenceMode.Balanced, RunHasRecord: false), await resolver.ResolveAsync(RedriveEnvelope()));
    }

    [Fact]
    public async Task ARunWhoseModeCannotBeDetermined_IsRefused()
    {
        // Each of these is a run that started, so running it in the host default could contradict its choice.
        var unknownRecordedMode = Compose(PersistenceMode.Balanced);
        await SaveStateAsync(recordedMode: "Coalesced");
        await AssertRefusedAsync(unknownRecordedMode, RedriveEnvelope(), "'Coalesced'");

        var queuedStartOfAMissingWorkflow = Compose(PersistenceMode.Balanced);
        await QueueAsync(StartEnvelope());
        await AssertRefusedAsync(queuedStartOfAMissingWorkflow, RedriveEnvelope(), $"'{ArtifactId}'");

        var startOfAMissingWorkflow = Compose(PersistenceMode.Balanced);
        await AssertRefusedAsync(startOfAMissingWorkflow, StartEnvelope(), $"'{ArtifactId}'");

        var unknownStampedMode = Compose(PersistenceMode.Balanced);
        await AssertRefusedAsync(unknownStampedMode, StartEnvelope(stampedMode: "balanced"), "'balanced'");
    }

    private static async Task AssertRefusedAsync(IRuntimePersistenceModeResolver resolver, WorkflowExecutionCommandEnvelope envelope, string named)
    {
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(async () => await resolver.ResolveAsync(envelope));
        Assert.Contains(WorkflowExecutionId, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(named, refusal.Message, StringComparison.Ordinal);
    }

    // The resolver as AddWorkflowRuntime registers it, over the runtime's in-memory stores. Each call starts a new host.
    private IRuntimePersistenceModeResolver Compose(PersistenceMode hostMode)
    {
        _provider?.Dispose();
        var services = new ServiceCollection().AddWorkflowRuntime();
        services.AddPersistenceModeOptions(new PersistenceModeOptions { Mode = hostMode });
        _provider = services.BuildServiceProvider();
        return _provider.CreateScope().ServiceProvider.GetRequiredService<IRuntimePersistenceModeResolver>();
    }

    private ValueTask SaveExecutableAsync(PersistenceMode? chosen) =>
        _provider!.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(Executable(chosen));

    private async Task SaveStateAsync(string? recordedMode) =>
        await _provider!.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(State(recordedMode));

    private async Task QueueAsync(WorkflowExecutionCommandEnvelope start) =>
        await _provider!.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(new RuntimeSchedulerWorkItem(
            workItemId: start.EnvelopeId,
            workflowExecutionId: WorkflowExecutionId,
            commandId: start.Command.CommandId,
            commandKind: start.Command.Kind,
            envelopeId: start.EnvelopeId,
            idempotencyKey: start.IdempotencyKey,
            enqueuedAt: Now,
            recordedAt: Now,
            payload: start.Command.Payload,
            commandMetadata: start.Command.Metadata,
            envelopeMetadata: start.Metadata));

    internal static WorkflowExecutable Executable(PersistenceMode? persistenceMode)
    {
        var node = new ExecutableNode(
            executableNodeId: "node-1",
            authoredActivityId: "authored-node-1",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(
                "test",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { type = "test" })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: Identity,
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

    private static readonly WorkflowExecutableIdentity Identity = new(ArtifactId, "definition-1", "version-1", "1.0.0", "sha256:test");

    private static WorkflowExecutionState State(string? recordedMode)
    {
        var metadata = new Dictionary<string, string>();
        if (recordedMode is not null)
            metadata[RuntimeMetadataKeys.PersistenceMode] = recordedMode;

        return new(
            WorkflowExecutionId: WorkflowExecutionId,
            PinnedExecutable: Identity,
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

    // A start as the start dispatcher makes one, with the mode it decided when one is given.
    private static WorkflowExecutionCommandEnvelope StartEnvelope(string? stampedMode = null)
    {
        var metadata = new Dictionary<string, string> { ["runtime.artifactId"] = ArtifactId };
        if (stampedMode is not null)
            metadata[RuntimeMetadataKeys.PersistenceMode] = stampedMode;

        return Envelope(
            WorkflowExecutionCommandKind.Start,
            "start",
            JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(Identity, ArtifactId)),
            metadata);
    }

    // A drain the resumption sweep starts: a RunSchedulerWork command that carries only its source.
    private static WorkflowExecutionCommandEnvelope RedriveEnvelope() =>
        Envelope(WorkflowExecutionCommandKind.RunSchedulerWork, "redrive", payload: null, new Dictionary<string, string> { ["source"] = "runtime-resumption" });

    private static WorkflowExecutionCommandEnvelope Envelope(
        WorkflowExecutionCommandKind kind,
        string name,
        JsonElement? payload,
        IReadOnlyDictionary<string, string> metadata) =>
        new(
            envelopeId: $"envelope-{name}",
            workflowExecutionId: WorkflowExecutionId,
            command: new WorkflowExecutionCommand($"command-{name}", WorkflowExecutionId, kind, Now, payload, metadata),
            idempotencyKey: $"{WorkflowExecutionId}:{name}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            metadata: metadata);
}

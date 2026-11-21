using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Supplied start commands through actual native start/checkpoint handlers; no saved-source admission or activity activation.</summary>
public sealed class RuntimeHistoryStartCausationTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1", "hash");
    private static readonly RuntimeHistoryCausation Origin = new("publication-event", "publisher", "source:send", "publisher-command", "publisher-work");

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Optional_start_and_checkpoint_cause_roundtrips_without_changing_the_legacy_absent_shape(bool web)
    {
        var options = web ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : new JsonSerializerOptions();
        var key = web ? "historyCausation" : "HistoryCausation";
        var absentStart = new WorkflowExecutionStartCommandPayload(Pin, Pin.ArtifactId);
        var absentCheckpoint = new RuntimeCheckpointCommandPayload(Pin, RuntimeCheckpointNames.WorkflowStarted, [], RuntimeCheckpointCommandPayload.WorkflowStartReason);
        Assert.False(JsonSerializer.SerializeToElement(absentStart, options).TryGetProperty(key, out _));
        Assert.False(JsonSerializer.SerializeToElement(absentCheckpoint, options).TryGetProperty(key, out _));
        var start = new WorkflowExecutionStartCommandPayload(Pin, Pin.ArtifactId) { HistoryCausation = Origin };
        var checkpoint = new RuntimeCheckpointCommandPayload(Pin, RuntimeCheckpointNames.WorkflowStarted, [], RuntimeCheckpointCommandPayload.WorkflowStartReason)
            { HistoryCausation = Origin };
        Assert.Equal(Origin, JsonSerializer.Deserialize<WorkflowExecutionStartCommandPayload>(JsonSerializer.Serialize(start, options), options)!.HistoryCausation);
        Assert.Equal(Origin, JsonSerializer.Deserialize<RuntimeCheckpointCommandPayload>(JsonSerializer.Serialize(checkpoint, options), options)!.HistoryCausation);
        Assert.Null(start.StartAuthority); Assert.Null(start.Authority); // Diagnostic origin does not create start authority.
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Native_start_and_checkpoint_handlers_keep_original_cause_without_retrospectively_enabling_history(bool captured, bool supplied)
    {
        var registrations = new ServiceCollection(); var clock = new FakeTimeProvider(At);
        registrations.AddLogging(); registrations.AddSingleton<TimeProvider>(clock);
        registrations.AddSingleton<IPersistenceAccessContextAccessor, Access>();
        registrations.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
        registrations.AddWorkflowRuntime(); RuntimeHistoryRegistration.AddServices(registrations, new() { Enabled = true });
        registrations.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "start-causation-history-tests-key-at-least-32-bytes");
        using var provider = registrations.BuildServiceProvider(validateScopes: true); using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var executableStore = services.GetRequiredService<IWorkflowExecutableStore>();
        await executableStore.SaveAsync(new(Pin,
            new("node", "authored", "test/activity", "1", "test", JsonSerializer.SerializeToElement(new { type = "test" }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), At, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference));
        var policy = captured ? RuntimeHistoryRegistration.ResolvePolicy(services, "run") : null;
        var expected = supplied ? Origin : null;
        var payload = new WorkflowExecutionStartCommandPayload(Pin, Pin.ArtifactId, tenantId: "scope")
            { HistoryCausation = expected, HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = captured };
        var queue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var states = services.GetRequiredService<IWorkflowExecutionStateStore>();
        var committer = services.GetRequiredService<RuntimeCheckpointCommitter>();
        var start = new WorkflowStartSchedulerWorkHandler(executableStore, queue, services.GetRequiredService<IRuntimeExecutionIdGenerator>(), clock, states);
        await start.HandleAsync(new("start-work", "run", "start-command", WorkflowExecutionCommandKind.Start, "envelope", "start-key", At, At,
            payload: JsonSerializer.SerializeToElement(payload)));
        var work = Assert.Single((await queue.ListAsync(new("run"))).Items);
        var checkpointPayload = work.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!;
        Assert.Equal(expected, checkpointPayload.HistoryCausation);
        if (policy is null) Assert.Null(checkpointPayload.HistoryCapturePolicy);
        else Assert.True(policy.IsEquivalentTo(checkpointPayload.HistoryCapturePolicy));
        var checkpoint = new WorkflowCheckpointSchedulerWorkHandler(services.GetRequiredService<IActivityExecutionStateStore>(), committer,
            services.GetService<IRuntimeActivityExecutionInspectionAccumulator>(), clock, states, executableStore);
        await checkpoint.HandleAsync(work);
        var native = Assert.Single(services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits()).Commit;
        Assert.Equal(expected, native.Checkpoint.HistoryCausation);
        var state = (await states.FindAsync("run"))!;
        Assert.Equal(captured, state.HistoryCaptureEnabledAtAdmission);
        var history = await services.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new("run"));
        if (captured)
        {
            var started = Assert.Single(history.Items, e => e.Name == "WorkflowStarted");
            if (expected is null) Assert.Null(started.Causation?.HistoryEventId);
            else Assert.Equal(expected, started.Causation);
        }
        else { Assert.Null(history.Head); Assert.Empty(history.Items); Assert.Null(state.HistoryCapturePolicy); }
        Assert.Equal(0, await services.GetRequiredService<IActivityExecutionStateStore>().CountAsync("run"));
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("scope")); }
}

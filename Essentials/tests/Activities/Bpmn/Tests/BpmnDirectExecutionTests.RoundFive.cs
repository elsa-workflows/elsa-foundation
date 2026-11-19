using System.Diagnostics.Metrics;
using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Malformed_shared_model_envelopes_retain_context_before_admission_or_claim(bool retained, bool invalidObject)
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var now = DateTimeOffset.UtcNow;
        const string execution = "malformed-shared-execution";
        const string input = "malformed-shared-input";
        const string workId = "malformed-shared-work";
        var payload = invalidObject ? JsonSerializer.SerializeToElement(new { }) :
            JsonSerializer.SerializeToElement("malformed-owned-model-envelope");
        if (retained)
            await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(
                new(workId, execution, input, WorkflowExecutionCommandKind.ExecuteModel, "envelope", "key", now, now, payload: payload));
        var error = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () =>
        {
            if (retained)
                await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerDrainer>().DrainAsync(new(execution));
            else
                await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionCommandExecutor>().ProcessAsync(
                    new("envelope", execution, new(input, execution, WorkflowExecutionCommandKind.ExecuteModel, now,
                        payload, new Dictionary<string, string>()), "key", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now), new());
        });
        if (invalidObject) Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
        else Assert.IsAssignableFrom<JsonException>(error.InnerException);
        Assert.Contains(execution, error.Message);
        Assert.Contains(input, error.Message);
        if (retained)
        {
            Assert.Contains(workId, error.Message);
            var database = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
            var row = Assert.Single(await database.SchedulerWorkItems.AsNoTracking().ToArrayAsync());
            Assert.Equal(1, row.Revision);
            Assert.Null(row.ClaimOwnerId);
            Assert.Equal(0, row.ClaimToken);
        }
        else Assert.Empty((await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(execution))).Items);
    }

    [Fact]
    public async Task Settled_models_release_mailboxes_and_reactivate_for_late_inputs_and_exact_replay()
    {
        using var meter = new Meter("bpmn-model-eviction-" + Guid.NewGuid().ToString("N"));
        var observed = -1;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter == meter && instrument.Name == InProcessWorkflowExecutionActorProvider.LiveMailboxGaugeName)
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((_, count, _, _) => observed = count);
        listener.Start();
        await using var host = await OpenAsync(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionActorProvider>(provider =>
                new InProcessWorkflowExecutionActorProvider(provider.GetRequiredService<IWorkflowExecutionCommandExecutor>(),
                    InProcessWorkflowExecutionActorProvider.DefaultMaxProcessedIdempotencyKeysPerAgent,
                    provider.GetRequiredService<RuntimeActorEvictionOptions>(), meter))));
        var actors = host.Services.GetRequiredService<IWorkflowExecutionActorProvider>();
        Assert.True(host.Services.GetRequiredService<RuntimeActorEvictionOptions>().PassivateOnTerminal);
        listener.RecordObservableInstruments();
        Assert.Equal(0, observed);
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var process = new BpmnProcessDefinition("immediate", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("finish", "start", "end")]);
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, Web), []);
        var caller = new BpmnModelCaller("eviction-operator", null);
        var publication = await runtime.PublishAsync(definition, "eviction-publication", caller);
        var executions = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var start = await runtime.StartAsync(publication.SourceReferenceId, "eviction-start-" + i, null, caller);
            executions.Add(start.ExecutionId);
            Assert.False(start.Pending);
            Assert.Equal(RuntimeModelLifecycle.TerminalSettled, (await runtime.FindExecutionAsync(start.ExecutionId, caller))!.Lifecycle);
            listener.RecordObservableInstruments();
            Assert.Equal(0, observed);
            var now = DateTimeOffset.UtcNow;
            var actor = await actors.GetAgentAsync(new(start.ExecutionId, WorkflowExecutionActorActivationReason.Start,
                now, "late-input", WorkflowExecutionActorCapabilities.InProcessMailbox));
            var key = "late-input-" + i;
            var redrive = await actor.EnqueueAsync(new(key, start.ExecutionId,
                new(key, start.ExecutionId, WorkflowExecutionCommandKind.RunSchedulerWork, now, null, new Dictionary<string, string>()),
                key, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
            Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, redrive.Status);
            Assert.Equal("true", redrive.Metadata[InProcessWorkflowExecutionActorProvider.WorkflowTerminatedMetadataKey]);
            listener.RecordObservableInstruments();
            Assert.Equal(0, observed);
        }
        for (var i = 0; i < executions.Count; i++)
        {
            var replay = await runtime.StartAsync(publication.SourceReferenceId, "eviction-start-" + i, null, caller);
            Assert.True(replay.Replay);
            Assert.False(replay.Pending);
            Assert.Equal(executions[i], replay.ExecutionId);
        }
        var waitingPublication = await runtime.PublishAsync(Material().Definition.Deserialize<BpmnModelDefinition>(Web)!,
            "eviction-waiting-publication", caller);
        var waiting = await runtime.StartAsync(waitingPublication.SourceReferenceId, "eviction-waiting-start", null, caller);
        var work = Assert.Single((await runtime.ListWorkAsync(waiting.ExecutionId, new(), caller)).Items);
        listener.RecordObservableInstruments();
        Assert.Equal(1, observed);
        await runtime.CompleteHumanAsync(waiting.ExecutionId, "eviction-complete", work.InvocationId, work.AttemptId, null, caller);
        listener.RecordObservableInstruments();
        Assert.Equal(0, observed);
        var late = await runtime.CompleteHumanAsync(waiting.ExecutionId, "eviction-late", work.InvocationId, work.AttemptId, null, caller);
        Assert.Equal("Rejected", late.Result!.Value.GetProperty("status").GetString());
        Assert.NotNull(await runtime.FindCommandAsync(waiting.ExecutionId, "eviction-late", caller));
        listener.RecordObservableInstruments();
        Assert.Equal(0, observed);
    }
}

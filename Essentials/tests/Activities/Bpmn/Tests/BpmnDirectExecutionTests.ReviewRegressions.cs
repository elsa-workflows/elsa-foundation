using System.IO.Compression;
using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task An_external_observer_can_subscribe_to_the_public_Core_protocol_startup_event()
    {
        var observed = new ProtocolStartupObservation();
        await using var host = await OpenAsync(configure: services =>
        {
            services.AddSingleton(observed);
            services.AddEventHandler<RuntimeExecutionProtocolsInitializing, ObserveProtocolStartup>();
        });
        Assert.Same(typeof(IRuntimeExecutionProtocolSource).Assembly, typeof(RuntimeExecutionProtocolsInitializing).Assembly);
        Assert.True(typeof(RuntimeExecutionProtocolsInitializing).IsPublic);
        Assert.Equal(1, observed.Calls);
    }

    private sealed class ProtocolStartupObservation { public int Calls { get; set; } }

    private sealed class ObserveProtocolStartup(ProtocolStartupObservation observation) : IEventHandler<RuntimeExecutionProtocolsInitializing>
    {
        public Task Handle(RuntimeExecutionProtocolsInitializing domainEvent, CancellationToken cancellationToken)
        {
            observation.Calls++;
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_invalid_service_retry_of_human_work_is_rejected_without_blocking_following_commands(bool cancel)
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var definition = TimedHumanMaterial("PT1H", emptyServiceInventory: true);
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveModelAsync(definition);
        var material = (await store.FindMaterialAsync(definition.Identity.ArtifactId))!;
        await SendAsync(host, material, "start", "start", new { });
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
        var waiting = (await reader.FindAsync("direct-execution"))!;
        var human = Assert.Single(BpmnDirectExecutionDriver.Read(waiting).Work, work => work.Timer is null);
        await SendAsync(host, material, "invalid-retry", "retry-service", new
        {
            incidentId = "nonexistent-incident", human.InvocationId, failedAttemptId = human.AttemptId
        });
        var rejected = (await reader.FindInputAsync("direct-execution", "invalid-retry"))!;
        Assert.Equal("Rejected", rejected.Result.GetProperty("status").GetString());
        Assert.Equal("incident-not-current", rejected.Result.GetProperty("code").GetString());
        Assert.Equal(waiting.Revision, rejected.AcceptedRevision);
        if (cancel) await SendAsync(host, material, "following", "cancel", new { reason = "ordinary cancellation" });
        else await SendAsync(host, material, "following", "complete-human", new { human.InvocationId, human.AttemptId });
        var terminal = (await reader.FindAsync("direct-execution"))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, terminal.Lifecycle);
        Assert.Equal(cancel ? "Cancelled" : "Completed", terminal.TerminalResult!.Value.GetProperty("status").GetString());
        Assert.Equal("Accepted", (await reader.FindInputAsync("direct-execution", "following"))!.Result.GetProperty("status").GetString());
        Assert.Empty((await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new("direct-execution"))).Items);
    }

    [Fact]
    public async Task An_independently_cancelled_native_worker_cannot_settle_while_its_actual_downstream_cancel_is_pending()
    {
        await using (var raw = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualIndependentNativeCancellation.sqlite.gz")))
        await using (var zipped = new GZipStream(raw, CompressionMode.Decompress))
        await using (var target = File.Create(database)) await zipped.CopyToAsync(target);
        await using var host = await OpenAsync(admitNewExecutions: false);
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        var before = Assert.Single((await reader.ListPageAsync(new())).Items);
        Assert.Empty(BpmnDirectExecutionDriver.Read(before).Cancellations ?? []);
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(before).Work).NativeWork!;
        var pending = await services.GetRequiredService<IRuntimePostCommitOutboxStore>()
            .GetDeliverableAsync(new(DateTimeOffset.UtcNow, 20, origin.WorkerExecutionId));
        var resultSource = Assert.Single(pending, item => item.Intent.Kind == BpmnNativeWorkBoundary.ResultKind);
        var cancelSource = Assert.Single(pending, item => item.Intent.Kind == "Elsa.Etl.Cancel.v1");
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, cancelSource.Status);
        var result = resultSource.Intent.Payload!.Value.Deserialize<BpmnNativeWorkResult>(Web)!;
        var material = (await services.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(before.Executable.ArtifactId))!;
        var command = new RuntimeModelExecutionCommand(before.Executable, before.MaterialIncarnationId,
            origin.ResultInputId, "native-result", resultSource.Intent.Payload.Value, before.Authority, before.TenantId);
        var now = DateTimeOffset.UtcNow;
        var item = new RuntimeSchedulerWorkItem("test-result", before.ExecutionId, command.InputId,
            WorkflowExecutionCommandKind.ExecuteModel, "test-result", "test-result", now, now);
        var context = new RuntimeModelExecutionContext(before.Scope, material, before, command, item);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => services.GetRequiredService<BpmnNativeWorkBoundary>()
            .ValidateResultAsync(context, result, CancellationToken.None).AsTask());
        Assert.Null(await reader.FindInputAsync(before.ExecutionId, origin.ResultInputId));
        Assert.Equal(JsonSerializer.Serialize(before, Web), JsonSerializer.Serialize(await reader.FindAsync(before.ExecutionId), Web));
    }

    [Fact]
    public async Task Malformed_owned_process_JSON_has_a_domain_error_with_material_context_and_original_cause()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var source = Material();
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(new { elements = 42 }), []);
        var material = new RuntimeModelMaterial(1, source.Identity, source.Protocol, source.Profile,
            JsonSerializer.SerializeToElement(definition, Web), source.InputContract, [], [], []);
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => scope.ServiceProvider.GetRequiredService<BpmnDirectExecutionDriver>()
            .ValidateMaterialAsync(material).AsTask());
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
        Assert.Contains(material.Identity.ArtifactId, error.Message);
        var runtimeError = await Assert.ThrowsAsync<BpmnModelValidationException>(() => scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>()
            .PublishAsync(definition, "malformed-process", new("operator", null)).AsTask());
        Assert.IsAssignableFrom<JsonException>(runtimeError.InnerException);
    }

    [Fact]
    public async Task Malformed_owned_command_JSON_has_a_domain_error_with_execution_and_input_context()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var definition = Material();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveModelAsync(definition);
        var material = (await store.FindMaterialAsync(definition.Identity.ArtifactId))!;
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => SendAsync(host, material,
            "malformed-start", "start", new { inputs = 42 }));
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
        Assert.Contains("direct-execution", error.Message);
        Assert.Contains("malformed-start", error.Message);
    }
}

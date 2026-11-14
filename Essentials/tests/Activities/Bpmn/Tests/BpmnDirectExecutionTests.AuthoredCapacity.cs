using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task Authored_model_admission_reports_capacity_shedding_without_writing_queue_responsibility()
    {
        var controller = new AuthoredCapacityController();
        await using var host = await OpenAsync(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IRuntimeAdmissionController>(controller)));
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var (material, activation) = await PrepareAuthoredCapacityModelAsync(services);
        var source = activation.Reference!;
        var command = new RuntimeModelExecutionCommand(material.Identity, material.IncarnationId, "capacity-command",
            BpmnDirectExecutionDriver.AuthoredKind, JsonSerializer.SerializeToElement(new
            {
                inputs = new Dictionary<string, JsonElement>(), sourceReferenceId = source.SourceReferenceId,
                authored = new AuthoredModelEntry(1, activation.Slot, source), submittedInputs = new Dictionary<string, JsonElement>()
            }, Web), new("bpmn-runtime", "operator"), null);

        var result = await services.GetRequiredService<IAuthoredModelAdmission>().AdmitAsync(
            new(activation.Slot, source, material, "capacity-execution", command, DateTimeOffset.UtcNow));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, result.Status);
        Assert.True(result.IsShed);
        Assert.Equal(TimeSpan.FromSeconds(4), result.ShedRetryAfter);
        Assert.Equal(1, controller.Decisions);
        Assert.Equal(0, controller.ActiveCharges);
        Assert.Empty(await services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListPendingWorkflowExecutionIdsAsync(10));
        Assert.Empty((await services.GetRequiredService<IRuntimeModelExecutionReader>().ListPageAsync(new())).Items);
    }

    [Fact]
    public async Task Authored_Run_retries_a_shed_request_and_preserves_accepted_replay_after_unpublication_at_capacity()
    {
        var controller = new AuthoredCapacityController();
        await using var host = await OpenAsync(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IRuntimeAdmissionController>(controller)));
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var (material, activation) = await PrepareAuthoredCapacityModelAsync(services);
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("operator", null);
        var sourceId = activation.Reference!.SourceReferenceId;

        var refusal = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
            runtime.StartAsync(sourceId, "retry-capacity", null, caller).AsTask());

        Assert.Equal("bpmn.admission-unavailable", refusal.Code);
        Assert.Empty(await services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListPendingWorkflowExecutionIdsAsync(10));
        Assert.Empty((await services.GetRequiredService<IRuntimeModelExecutionReader>().ListPageAsync(new())).Items);
        controller.Shed = false;
        var accepted = await runtime.StartAsync(sourceId, "retry-capacity", null, caller);
        Assert.True(accepted.Pending);
        Assert.Equal(2, controller.Decisions);
        Assert.Equal(0, controller.ActiveCharges);
        var queue = services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var original = Assert.Single((await queue.ListAsync(new(accepted.ExecutionId))).Items);
        Assert.True((await services.GetRequiredService<IWorkflowActivationCoordinator>().DeactivateMaterialAsync(
            new(activation.Slot.WorkflowDefinitionId, material, "default", WorkflowActivationSource.Publishing, activation.Slot.Revision))).Succeeded);
        controller.Shed = true;

        var replay = await runtime.StartAsync(sourceId, "retry-capacity", null, caller);

        Assert.True(replay.Replay);
        Assert.True(replay.Pending);
        Assert.Equal(accepted.ExecutionId, replay.ExecutionId);
        Assert.Equal(2, controller.Decisions);
        var retained = Assert.Single((await queue.ListAsync(new(accepted.ExecutionId))).Items);
        Assert.Equal(original.WorkItemId, retained.WorkItemId);
        Assert.Equal(original.Payload!.Value.GetRawText(), retained.Payload!.Value.GetRawText());
    }

    private static async ValueTask<(RuntimeExecutionMaterial Material, WorkflowActivationResult Activation)> PrepareAuthoredCapacityModelAsync(
        IServiceProvider services)
    {
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var material = await runtime.PreparePublicationAsync(Material().Definition.Deserialize<BpmnModelDefinition>(Web)!,
            new(1, []), [], new("operator", null));
        var now = DateTimeOffset.UtcNow;
        const string logical = "capacity-definition", publication = "capacity-publication";
        var source = new WorkflowExecutableSourceReference(WorkflowActivationReferenceIdentity.Create(publication), material.Identity.ArtifactId,
            "BpmnModel", "capacity-version", "1.0.0", logical, "capacity-version", "1.0.0", now, now,
            WorkflowExecutableReferenceScope.Published, ActivationId: publication, SlotId: WorkflowActivationSlotIdentity.Create(logical, "default"));
        var activation = await services.GetRequiredService<IWorkflowActivationCoordinator>().ActivateMaterialAsync(
            new(logical, material, source, "default", publication, WorkflowActivationSource.Publishing, 0));
        Assert.True(activation.Succeeded);
        Assert.NotNull(activation.Reference);
        return (material, activation);
    }

    private sealed class AuthoredCapacityController : IRuntimeAdmissionController
    {
        public double Limit => 1;
        public bool Shed { get; set; } = true;
        public int Decisions { get; private set; }
        public int ActiveCharges { get; private set; }

        public RuntimeAdmissionDecision TryAdmit()
        {
            Decisions++;
            if (Shed) return RuntimeAdmissionDecision.Shed("Test capacity exhausted.", TimeSpan.FromMilliseconds(3200), 1, Limit);
            ActiveCharges++;
            return RuntimeAdmissionDecision.Admitted(new Charge(this), _ => { }, 0, Limit, 0);
        }

        private sealed class Charge(AuthoredCapacityController controller) : IRuntimeAdmissionCharge
        {
            public long Units => 1;
            public void Dispose() => controller.ActiveCharges--;
        }
    }
}

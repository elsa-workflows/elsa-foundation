using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_worker_inputs_use_the_exact_retained_policy_after_republication_and_provider_restart(bool enabled)
    {
        var caller = new BpmnModelCaller("bpmn-test", null);
        RuntimeModelNativeWorkOrigin retainedOrigin = null!;
        string retainedModelPayload = null!;
        await using (var host = await OpenAsync(activityIds: ["policy-input-worker"]))
        await using (var scope = await ScopeAsync(host))
        {
            var worker = RetainedPolicyWorker(enabled, "original-policy-worker");
            var reference = await host.PublishAsync(worker, "original-policy-worker-source");
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            var process = new BpmnProcessDefinition("retained-policy-service", Elements:
                [new("start", BpmnElementTypes.StartEvent),
                 new("service", BpmnElementTypes.ServiceTask, bindingRef: "worker"),
                 new("end", BpmnElementTypes.EndEvent)],
                SequenceFlows: [new("to-service", "start", "service"), new("to-end", "service", "end")]);
            var definition = new BpmnModelDefinition(3, JsonSerializer.SerializeToElement(process, Web), [],
                [new("worker", worker.Identity, "business-error", new(1, BpmnServiceFaultDisposition.FinalFault, 1))]);
            BpmnWorkerInputMapping[] mappings =
                [new("worker", new Dictionary<string, BpmnModelInputSource>
                { ["count"] = new("Literal", value: JsonSerializer.SerializeToElement<object?>(null)) })];

            if (!enabled)
            {
                await Assert.ThrowsAsync<BpmnModelValidationException>(() =>
                    runtime.PreparePublicationAsync(definition, new(1, []), mappings, caller).AsTask());
                Assert.Empty((await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>()
                    .ListPageAsync(new())).Items);
                return;
            }

            var material = await runtime.PreparePublicationAsync(definition, new(1, []), mappings, caller);
            // Policies remain owned by the pinned native executable, not copied into model/origin JSON.
            Assert.False(material.Model!.Definition.GetRawText().Contains("\"valuePolicies\"", StringComparison.OrdinalIgnoreCase));
            await SendAsync(host, material, "policy-mapped-start", "start", new { inputs = new { } });
            var state = (await runtime.FindExecutionAsync("direct-execution", caller))!;
            retainedModelPayload = state.Payload.GetRawText();
            retainedOrigin = Assert.Single(BpmnDirectExecutionDriver.Read(state).Work).NativeWork!;
            Assert.Equal(worker.Identity, retainedOrigin.WorkerExecutable);
            Assert.Equal(0, retainedOrigin.Inputs!["count"].GetInt32());
            Assert.False(JsonSerializer.Serialize(retainedOrigin, Web).Contains("\"valuePolicies\"", StringComparison.OrdinalIgnoreCase));
            var source = (await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>()
                .FindDurableAsync(retainedOrigin.SourceOutboxItemId))!;
            Assert.Equal(BpmnNativeWorkBoundary.InputStartKind, source.Intent.Kind);
            Assert.True(retainedOrigin.IsEquivalentTo(source.Intent.Payload!.Value.Deserialize<RuntimeModelNativeWorkOrigin>(Web)));

            // The same logical worker now has a distinct Off publication. Never overwrite original material.
            var later = RetainedPolicyWorker(false, "later-policy-worker");
            Assert.Equal(worker.Identity.DefinitionId, later.Identity.DefinitionId);
            Assert.NotEqual(worker.Identity.ArtifactId, later.Identity.ArtifactId);
            await host.PublishAsync(later, "later-policy-worker-source");
            await host.RetirePublicationAsync(reference, "Replaced by an Off publication while model work is retained.");
        }

        await using (var host = await OpenAsync(admitNewExecutions: false, activityIds: ["policy-input-worker"]))
        await using (var scope = await ScopeAsync(host))
        {
            var services = scope.ServiceProvider;
            var runtime = services.GetRequiredService<IBpmnModelRuntime>();
            var recovered = (await runtime.FindExecutionAsync("direct-execution", caller))!;
            Assert.Equal(retainedModelPayload, recovered.Payload.GetRawText());
            Assert.True(retainedOrigin.IsEquivalentTo(Assert.Single(BpmnDirectExecutionDriver.Read(recovered).Work).NativeWork));
            var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
            await processor.ProcessAsync(new(100, workflowExecutionId: recovered.ExecutionId,
                intentKind: BpmnNativeWorkBoundary.InputStartKind));
            var actual = (await services.GetRequiredService<IWorkflowExecutionStateStore>()
                .FindAsync(retainedOrigin.WorkerExecutionId))!;
            Assert.Equal(retainedOrigin.WorkerExecutable, actual.PinnedExecutable);
            Assert.True(retainedOrigin.IsEquivalentTo(actual.ModelWorkOrigin));
            var durableInput = Assert.Single((await services.GetRequiredService<IDurableValueStateStore>()
                .ListPageAsync(new(retainedOrigin.WorkerExecutionId))).Items,
                value => value.Metadata.GetValueOrDefault(RuntimeMetadataKeys.InputName) == "count");
            Assert.Equal(0, durableInput.InlineValue!.Value.GetInt32());

            // A fresh native-result input must traverse ValidateShapeAndReplayAsync and retained Realize.
            // Replaying the original start alone would return its prior receipt before policy resolution.
            await processor.ProcessAsync(new(100, workflowExecutionId: retainedOrigin.WorkerExecutionId,
                intentKind: BpmnNativeWorkBoundary.ResultKind));
            await DrainModelAsync(host, recovered.ExecutionId);
            var completed = (await runtime.FindExecutionAsync(recovered.ExecutionId, caller))!;
            var acceptedResult = Assert.Single(BpmnDirectExecutionDriver.Read(completed).NativeResults!);
            Assert.True(retainedOrigin.IsEquivalentTo(acceptedResult.Origin));
            Assert.Equal(WorkflowExecutionStatus.Faulted, acceptedResult.Status);
            Assert.NotNull(completed.TerminalResult);
            Assert.Equal("Faulted", completed.TerminalResult!.Value.GetProperty("status").GetString());
        }
    }

    private static WorkflowExecutable RetainedPolicyWorker(bool enabled, string artifact)
    {
        var identity = WorkflowExecutionHarness.Identity with
        {
            ArtifactId = artifact, DefinitionVersionId = artifact + "-version", ArtifactHash = artifact + "-hash"
        };
        return new(identity,
            WorkflowExecutionHarness.NewFaultingNode("policy-input-worker", "Expected policy worker fault"),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), WorkflowExecutionHarness.Timestamp,
            new Dictionary<string, string>(), new(1, [new("count", new("Int32"), true)]),
            dependencies: null, runtimeRequirements: null, storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference, persistenceMode: null,
            workflowVariables: null, valuePolicies: new(DefaultOnNull: enabled));
    }
}

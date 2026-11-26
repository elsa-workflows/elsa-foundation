using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
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
    public async Task Input_profile_refuses_an_explicit_null_model_default_before_persistence()
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => runtime.PreparePublicationAsync(
            Material().Definition.Deserialize<BpmnModelDefinition>(Web)!,
            new(1, [new("value", new("String"), true, JsonSerializer.SerializeToElement<string?>(null))]), [],
            new("operator", null)).AsTask());
        Assert.Contains("explicit-null defaults", error.Message);
    }

    [Fact]
    public void Input_profile_refuses_an_explicit_null_pinned_worker_default_before_serializing_its_envelope()
    {
        var definition = Material().Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var worker = new BpmnWorkerInputMapping("worker", new Dictionary<string, BpmnModelInputSource>(),
            new(1, [new("value", new("String"), true, JsonSerializer.SerializeToElement<string?>(null))]));
        // Contract validation precedes the inventory check, so an unsupported default cannot be hashed or serialized.
        var error = Assert.Throws<BpmnModelValidationException>(() => BpmnInputSupport.Validate(new(1, []),
            new(1, definition, [worker]), new Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableInputValidator()));
        Assert.Contains("explicit-null defaults", error.Message);
    }

    [Fact]
    public async Task Closed_preparation_preserves_the_raw_material_profile_and_hash_without_minting_start_authority()
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("operator", null);
        var definition = Material().Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var prepared = await runtime.PreparePublicationAsync(definition, new(1, []), [], caller);
        Assert.Equal(BpmnModelProtocol.Profile, prepared.Model!.Profile);
        Assert.Empty((await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .ListByArtifactPageAsync(new(prepared.Identity.ArtifactId))).Items);
        var published = await runtime.PublishAsync(definition, "raw-control", caller);
        Assert.Equal(published.Executable, prepared.Identity);
        Assert.Equal(published.IncarnationId, prepared.IncarnationId);
    }

    [Fact]
    public async Task The_input_profile_enters_the_ordinary_model_root_and_retains_its_exact_inputs_across_restart()
    {
        var caller = new BpmnModelCaller("bpmn-test", null);
        string retained;
        await using (var host = await OpenAsync())
        await using (var scope = await ScopeAsync(host))
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            var material = await runtime.PreparePublicationAsync(Material().Definition.Deserialize<BpmnModelDefinition>(Web)!,
                new(1, [new("name", new("String"), true), new("limit", new("Int32"), false, JsonSerializer.SerializeToElement(7))]), [], caller);
            var normalized = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableInputValidator>()
                .Validate(material.Model!.InputContract, new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("Ada") });
            Assert.True(normalized.IsValid);
            await SendAsync(host, material, "input-start", "start", new { inputs = normalized.NormalizedInputs });
            var state = (await runtime.FindExecutionAsync("direct-execution", caller))!;
            Assert.Equal(16, state.Payload.GetProperty("formatVersion").GetInt32());
            Assert.Equal(BpmnModelProtocol.InputsProfile, state.Payload.GetProperty("profile").GetString());
            var document = BpmnDirectExecutionDriver.Read(state);
            Assert.Null(document.NativeParent);
            Assert.Equal("Ada", document.Entry!.Inputs["name"].GetString());
            Assert.Equal(7, document.Entry.Inputs["limit"].GetInt32());
            Assert.Single(document.Work);
            retained = state.Payload.GetRawText();
            await AssertNativeEmptyAsync(scope.ServiceProvider);
        }
        await using (var host = await OpenAsync(admitNewExecutions: false))
        await using (var scope = await ScopeAsync(host))
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
            var state = (await runtime.FindExecutionAsync("direct-execution", caller))!;
            Assert.Equal(retained, state.Payload.GetRawText());
            var work = Assert.Single(BpmnDirectExecutionDriver.Read(state).Work);
            await runtime.CompleteHumanAsync(state.ExecutionId, "complete-input-human", work.InvocationId, work.AttemptId, null, caller);
            var completed = (await runtime.FindExecutionAsync(state.ExecutionId, caller))!;
            Assert.Equal(RuntimeModelLifecycle.TerminalSettled, completed.Lifecycle);
            Assert.Equal(7, BpmnDirectExecutionDriver.Read(completed).Entry!.Inputs["limit"].GetInt32());
            await AssertNativeEmptyAsync(scope.ServiceProvider);
        }
    }

    [Fact]
    public async Task Model_worker_inputs_are_physically_captured_and_consumed_by_the_native_worker()
    {
        await using var host = await OpenAsync(activityIds: ["input-worker"]);
        var worker = new WorkflowExecutable(WorkflowExecutionHarness.Identity,
            WorkflowExecutionHarness.NewFaultingNode("input-worker", "Expected worker fault"),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(),
            inputContract: new(1, [new("message", new("String"), true)]), dependencies: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        await host.PublishAsync(worker, "input-worker-source");
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("bpmn-test", null);
        var process = new BpmnProcessDefinition("input-service", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "worker"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-service", "start", "service"), new("to-end", "service", "end")]);
        var material = await runtime.PreparePublicationAsync(new(3, JsonSerializer.SerializeToElement(process, Web), [],
                [new("worker", worker.Identity, "business-error", new(1, BpmnServiceFaultDisposition.FinalFault, 1))]),
            new(1, [new("text", new("String"), true)]),
            [new("worker", new Dictionary<string, BpmnModelInputSource> { ["message"] = new("Input", "text") })], caller);
        await SendAsync(host, material, "mapped-start", "start", new { inputs = new { text = "captured-value" } });
        var state = (await runtime.FindExecutionAsync("direct-execution", caller))!;
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(state).Work).NativeWork!;
        Assert.Equal(2, origin.FormatVersion);
        Assert.Equal("captured-value", origin.Inputs!["message"].GetString());
        var source = (await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(origin.SourceOutboxItemId))!;
        Assert.Equal(BpmnNativeWorkBoundary.InputStartKind, source.Intent.Kind);
        Assert.True(origin.IsEquivalentTo(source.Intent.Payload!.Value.Deserialize<RuntimeModelNativeWorkOrigin>(Web)));
        var identity = BpmnNativeWorkBoundary.WorkerIdentity(origin.Scope, origin.ExecutionId, origin.InvocationId, origin.AttemptId, origin.WorkerExecutable);
        var rejected = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>()
            .DispatchAsync(new(origin.WorkerExecutable.ArtifactId, state.Authority.SystemIdentity, origin.WorkerExecutionId, identity.StartKey,
                null, null, new Dictionary<string, object?> { ["message"] = "altered" }, null, null, WorkflowRunKind.PublishedRun,
                null, WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy, null, null, state.TenantId,
                new(origin.Scope.Value), state.Authority, WorkflowExecutableStartAuthority.FromModelWork(origin))).AsTask());
        Assert.Contains("model-work-input-mismatch", rejected.ReasonCode);
        await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(100,
            workflowExecutionId: state.ExecutionId, intentKind: BpmnNativeWorkBoundary.InputStartKind));
        var actual = (await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId))!;
        Assert.NotNull(actual);
        Assert.Null(actual.ParentWorkflowExecutionId);
        Assert.True(origin.IsEquivalentTo(actual.ModelWorkOrigin));
        var durableInput = Assert.Single((await scope.ServiceProvider.GetRequiredService<IDurableValueStateStore>()
            .ListPageAsync(new(origin.WorkerExecutionId))).Items, value => value.Metadata.GetValueOrDefault(RuntimeMetadataKeys.InputName) == "message");
        Assert.Equal("captured-value", durableInput.InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task Authored_slot_start_refuses_an_unqualified_admission_without_a_legacy_queue_fallback()
    {
        await using var host = await OpenAsync(configure: services => services.Replace(ServiceDescriptor.Scoped<IAuthoredModelAdmission, UnsupportedAuthoredAdmission>()));
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("operator", null);
        var material = await runtime.PreparePublicationAsync(Material().Definition.Deserialize<BpmnModelDefinition>(Web)!, new(1, []), [], caller);
        var now = DateTimeOffset.UtcNow;
        const string logical = "authored-definition", publication = "authored-publication";
        var sourceId = WorkflowActivationReferenceIdentity.Create(publication);
        var slotId = WorkflowActivationSlotIdentity.Create(logical, "default");
        var source = new WorkflowExecutableSourceReference(sourceId, material.Identity.ArtifactId, "BpmnModel", "authored-version", "1.0.0",
            logical, "authored-version", "1.0.0", now, now, WorkflowExecutableReferenceScope.Published, ActivationId: publication, SlotId: slotId);
        Assert.True((await scope.ServiceProvider.GetRequiredService<IWorkflowActivationCoordinator>().ActivateMaterialAsync(
            new(logical, material, source, "default", publication, WorkflowActivationSource.Publishing, 0))).Succeeded);
        await Assert.ThrowsAsync<NotSupportedException>(() => runtime.StartAsync(sourceId, "no-fallback", null, caller).AsTask());
        Assert.Empty((await scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>().ListPageAsync(new())).Items);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListPendingWorkflowExecutionIdsAsync(10));
    }

    private sealed class UnsupportedAuthoredAdmission : IAuthoredModelAdmission;

    [Fact]
    public void Version_one_native_origin_keeps_its_wire_and_version_two_detaches_its_captured_values()
    {
        var pin = Material().Identity;
        var scope = new PersistenceScope(PersistenceScope.DefaultValue);
        var protocol = new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version);
        var old = new RuntimeModelNativeWorkOrigin(1, scope, "model", protocol, pin, "incarnation", "binding", "invocation", "attempt",
            pin, "worker", "commit:start", "start", BpmnNativeWorkBoundary.ResultKind, "result");
        var oldWire = JsonSerializer.SerializeToElement(old, Web);
        Assert.False(oldWire.TryGetProperty("inputs", out _));
        Assert.False(oldWire.TryGetProperty("inputContractFingerprint", out _));
        Assert.True(old.IsEquivalentTo(oldWire.Deserialize<RuntimeModelNativeWorkOrigin>(Web)));
        var values = new Dictionary<string, JsonElement>();
        RuntimeModelNativeWorkOrigin captured;
        using (var document = JsonDocument.Parse("{\"text\":\"original\"}"))
        {
            values["message"] = document.RootElement;
            captured = new(2, scope, "model", protocol, pin, "incarnation", "binding", "invocation", "attempt", pin,
                "worker", "commit:start", "start", BpmnNativeWorkBoundary.ResultKind, "result", values, "contract");
        }
        values.Clear();
        Assert.Equal("original", captured.Inputs!["message"].GetProperty("text").GetString());
        Assert.True(captured.IsEquivalentTo(JsonSerializer.SerializeToElement(captured, Web).Deserialize<RuntimeModelNativeWorkOrigin>(Web)));
        Assert.False(captured.IsEquivalentTo(old));
    }
}

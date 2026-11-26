using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class RuntimeWorkflowExecutionStartDispatchTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task Native_model_worker_uses_the_exact_origin_inputs(int version, bool withInputs)
    {
        var child = NewExecutable(); await _store.SaveAsync(child);
        var parentPin = child.Identity with { ArtifactId = "model", DefinitionId = "model-definition" };
        var protocol = new RuntimeExecutionProtocol("test.model", 1);
        var model = new RuntimeModelMaterial(1, parentPin, protocol, "test.profile", JsonSerializer.SerializeToElement(new { }),
            new(1, []), [], [new("work", child.Identity)], []);
        var bag = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(new { number = 42, text = "input" }) };
        var origin = ModelOrigin(version, child.Identity, parentPin, protocol, withInputs ? bag : null);
        var dispatcher = new WorkflowStartDispatcher(new ModelMaterialForStart(_store, new(model, "incarnation")),
            _references, _agentProvider, new IncrementingRuntimeExecutionIdGenerator(), new FakeTimeProvider(_now));
        var requestInputs = withInputs ? new Dictionary<string, object?> { ["value"] = JsonDocument.Parse("{\"text\":\"input\",\"number\":42}").RootElement.Clone() } : null;

        await dispatcher.DispatchAsync(ModelRequest(origin, requestInputs));

        var payload = Assert.Single(_agentProvider.Agent.Envelopes).Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!;
        Assert.Equal(withInputs ? 1 : 0, payload.Inputs.Count);
        if (withInputs) Assert.True(JsonElement.DeepEquals(bag["value"], payload.Inputs["value"]));
        Assert.True(origin.IsEquivalentTo(payload.StartAuthority!.ModelWork));
    }

    [Theory]
    [InlineData(1, "unexpected")]
    [InlineData(2, "changed")]
    [InlineData(2, "missing")]
    [InlineData(2, "case")]
    public async Task Native_model_worker_copied_origin_cannot_override_the_captured_bag(int version, string defect)
    {
        var child = NewExecutable();
        var captured = version == 1 ? null : new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(42) };
        var origin = ModelOrigin(version, child.Identity, child.Identity with { ArtifactId = "model" }, new("test.model", 1), captured);
        IReadOnlyDictionary<string, object?> supplied = defect switch
        {
            "missing" => new Dictionary<string, object?>(),
            "case" => new Dictionary<string, object?> { ["Value"] = 42 },
            _ => new Dictionary<string, object?> { ["value"] = 99 }
        };

        var exception = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => _dispatcher.DispatchAsync(ModelRequest(origin, supplied)).AsTask());

        Assert.Equal(WorkflowExecutableStartRejectionCategory.Authority, exception.Category);
        Assert.Equal("model-work-input-mismatch", exception.ReasonCode);
        Assert.Empty(_agentProvider.ActivationRequests);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    [Fact]
    public async Task Altered_model_worker_inputs_are_rejected_before_even_keyed_duplicate_lookup()
    {
        var child = NewExecutable(); var keyed = KeyedWorkflowStartIdentity.For("model-work", child.Identity.ArtifactId);
        var origin = ModelOrigin(2, child.Identity, child.Identity with { ArtifactId = "model" }, new("test.model", 1),
            new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(42) }, keyed.WorkflowExecutionId);
        // No target material/execution is read: input authority is checked before duplicate reconciliation.
        var request = ModelRequest(origin, new Dictionary<string, object?> { ["value"] = 99 }, keyed.StartKey);
        var exception = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() =>
            NewDispatcher(new InMemoryWorkflowExecutionStateStore()).DispatchAsync(request).AsTask());
        Assert.Equal("model-work-input-mismatch", exception.ReasonCode);
        Assert.Empty(_agentProvider.Agent.Envelopes);
    }

    private RuntimeModelNativeWorkOrigin ModelOrigin(int version, WorkflowExecutableIdentity child, WorkflowExecutableIdentity model,
        RuntimeExecutionProtocol protocol, IReadOnlyDictionary<string, JsonElement>? inputs, string workerId = "worker") =>
        new(version, new("tenant"), "model-run", protocol, model, "incarnation", "work", "invocation", "attempt", child,
            workerId, "model-commit:start", "start", "test.result", "result-input", inputs, version == 2 ? "contract-fingerprint" : null);

    private static WorkflowExecutionStartDispatchRequest ModelRequest(RuntimeModelNativeWorkOrigin origin,
        IReadOnlyDictionary<string, object?>? inputs, string? key = null) =>
        new(origin.WorkerExecutable.ArtifactId, "model-runtime", origin.WorkerExecutionId, key, null, null, inputs, null, null,
            WorkflowRunKind.PublishedRun, null, WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy,
            null, null, "tenant", new("tenant"), new("model-runtime", "source-actor"), WorkflowExecutableStartAuthority.FromModelWork(origin));

    private sealed class ModelMaterialForStart(IWorkflowExecutableStore inner, RuntimeExecutionMaterial model) : WorkflowExecutableStoreDecorator(inner), IWorkflowExecutableStore
    {
        public ValueTask<RuntimeExecutionMaterial?> FindMaterialAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimeExecutionMaterial?>(artifactId == model.Identity.ArtifactId ? model : null);
    }
}

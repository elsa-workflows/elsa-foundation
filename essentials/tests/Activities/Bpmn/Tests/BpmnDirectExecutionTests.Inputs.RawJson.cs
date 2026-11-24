using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task Prepared_worker_mappings_retain_raw_literals_and_null_presence_across_provider_restart()
    {
        const string raw = """{ "OrderTotal":1.00, "text":"Ren\u00e9 <>&", "large":9007199254740993, "explicit":null }""";
        string artifactId;
        string retainedDefinition;
        await using (var host = await OpenAsync(activityIds: ["raw-input-worker"]))
        await using (var scope = await ScopeAsync(host))
        {
            var worker = new WorkflowExecutable(WorkflowExecutionHarness.Identity,
                WorkflowExecutionHarness.NewFaultingNode("raw-input-worker", "Expected worker fault"),
                new Dictionary<string, WorkflowExecutableResumeTarget>(), WorkflowExecutionHarness.Timestamp,
                new Dictionary<string, string>(), inputContract: new(1,
                [new("RawData", new("Any"), true), new("ExplicitNull", new("String"), true, isNullable: true),
                    new("FromModel", new("String"), true)]), dependencies: null,
                incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
            await host.PublishAsync(worker, "raw-input-worker-source");
            var process = new BpmnProcessDefinition("raw-input-service", Elements:
                [new("start", BpmnElementTypes.StartEvent),
                    new("service", BpmnElementTypes.ServiceTask, bindingRef: "worker"),
                    new("end", BpmnElementTypes.EndEvent)],
                SequenceFlows: [new("to-service", "start", "service"), new("to-end", "service", "end")]);
            BpmnWorkerInputMapping mapping;
            using (var data = JsonDocument.Parse(raw))
            using (var explicitNull = JsonDocument.Parse("null"))
                mapping = new("worker", new Dictionary<string, BpmnModelInputSource>(StringComparer.Ordinal)
                {
                    ["RawData"] = new("Literal", value: data.RootElement),
                    ["ExplicitNull"] = new("Literal", value: explicitNull.RootElement),
                    ["FromModel"] = new("Input", "modelText")
                });
            var material = await scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>().PreparePublicationAsync(
                new(3, JsonSerializer.SerializeToElement(process, Web), [],
                    [new("worker", worker.Identity, "business-error", new(1, BpmnServiceFaultDisposition.FinalFault, 1))]),
                new(1, [new("modelText", new("String"), true)]), [mapping], new("bpmn-test", null));
            artifactId = material.Identity.ArtifactId;
            retainedDefinition = material.Model!.Definition.GetRawText();
            AssertMapping(material.Model.Definition);
        }

        await using (var host = await OpenAsync(admitNewExecutions: false, activityIds: ["raw-input-worker"]))
        await using (var scope = await ScopeAsync(host))
        {
            var retained = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(artifactId);
            Assert.NotNull(retained);
            Assert.Equal(retainedDefinition, retained.Model!.Definition.GetRawText());
            AssertMapping(retained.Model.Definition);
        }

        static void AssertMapping(JsonElement definition)
        {
            var inputs = definition.GetProperty("workerInputMappings")[0].GetProperty("inputs");
            Assert.False(inputs.TryGetProperty("rawData", out _));
            var literal = inputs.GetProperty("RawData").GetProperty("value");
            Assert.Equal(raw, literal.GetRawText());
            Assert.Equal("1.00", literal.GetProperty("OrderTotal").GetRawText());
            Assert.Equal(9007199254740993L, literal.GetProperty("large").GetInt64());
            Assert.Equal(JsonValueKind.Null, literal.GetProperty("explicit").ValueKind);
            Assert.False(literal.TryGetProperty("missing", out _));
            Assert.Equal(JsonValueKind.Null, inputs.GetProperty("ExplicitNull").GetProperty("value").ValueKind);
            Assert.False(inputs.GetProperty("FromModel").TryGetProperty("value", out _));

            var roundTrip = definition.Deserialize<BpmnInputModelDefinition>(JsonBaseline.Options)!;
            var mapping = Assert.Single(roundTrip.WorkerInputMappings);
            Assert.Equal(raw, mapping.Inputs["RawData"].Value!.Value.GetRawText());
            Assert.Equal(JsonValueKind.Null, mapping.Inputs["ExplicitNull"].Value!.Value.ValueKind);
            Assert.Null(mapping.Inputs["FromModel"].Value);
            Assert.Equal(raw, JsonSerializer.SerializeToElement(roundTrip, JsonBaseline.Options)
                .GetProperty("workerInputMappings")[0].GetProperty("inputs").GetProperty("RawData").GetProperty("value").GetRawText());
        }
    }
}

using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("message")]
    [InlineData("blank-name")]
    [InlineData("extra-filter")]
    [InlineData("missing-pin")]
    [InlineData("wrong-pin")]
    [InlineData("throw")]
    public async Task The_signal_profile_admits_only_its_exact_fixed_catch_bindings(string? variant)
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var driver = scope.ServiceProvider.GetRequiredService<BpmnDirectExecutionDriver>();
        if (variant is null) await driver.ValidateMaterialAsync(SignalMaterial());
        else await Assert.ThrowsAsync<BpmnModelValidationException>(() => driver.ValidateMaterialAsync(SignalMaterial(variant)).AsTask());
    }

    private static RuntimeModelMaterial SignalMaterial(string? variant = null)
    {
        var properties = new Dictionary<string, string> { [BpmnEventDefinitionProperties.Name] = variant == "blank-name" ? " " : "release" };
        if (variant == "extra-filter") properties["correlation"] = "order-1";
        var process = new BpmnProcessDefinition("signals", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("fork", BpmnElementTypes.ParallelGateway),
                new("first", variant == "throw" ? BpmnElementTypes.IntermediateThrowEvent : BpmnElementTypes.IntermediateCatchEvent,
                    bindingRef: "first-event", eventDefinitions: [new(variant == "message" ? BpmnEventDefinitionTypes.Message : BpmnEventDefinitionTypes.Signal, properties)]),
                new("second", BpmnElementTypes.IntermediateCatchEvent, bindingRef: "second-event", eventDefinitions: [new(BpmnEventDefinitionTypes.Signal, properties)]),
                new("join", BpmnElementTypes.ParallelGateway), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-fork", "start", "fork"), new("to-first", "fork", "first"), new("to-second", "fork", "second"),
                new("first-join", "first", "join"), new("second-join", "second", "join"), new("to-end", "join", "end")]);
        var pin = new WorkflowExecutableIdentity("event-artifact", "event-definition", "event-version", "1.0.0", "event-hash");
        var definition = new BpmnModelDefinition(5, JsonSerializer.SerializeToElement(process, Web), [], null, null,
            [new("first-event", pin), new("second-event", pin)]);
        RuntimeModelDependency[] dependencies = variant == "missing-pin" ? [new("first-event", pin)] :
            [new("first-event", pin), new("second-event", variant == "wrong-pin" ? pin with { ArtifactHash = "other" } : pin)];
        return new(1, pin, new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), BpmnModelProtocol.NativeWorkProfile,
            JsonSerializer.SerializeToElement(definition, Web), new(1, []), [], dependencies, []);
    }
}

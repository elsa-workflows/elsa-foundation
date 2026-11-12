using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Primitives.Activities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnNativeEventBindingTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly WorkflowExecutableIdentity Pin = new("event-artifact", "event-definition", "event-version", "1.0.0", "event-hash");

    [Fact]
    public void Signal_material_requires_an_explicit_pin_inventory_and_round_trips_it()
    {
        var process = JsonSerializer.SerializeToElement(new { processId = "signals" });
        var definition = new BpmnModelDefinition(5, process, [], null, null, [new("signal", Pin)]);
        var saved = JsonSerializer.Serialize(definition, Web);
        Assert.Equal(saved, JsonSerializer.Serialize(JsonSerializer.Deserialize<BpmnModelDefinition>(saved, Web), Web));
        Assert.Equal(Pin, Assert.Single(definition.NativeEventBindings!).Executable);
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(5, process, []));
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(5, process, [], null, null, []));
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(5, process, ["human"], null, null, [new("signal", Pin)]));
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(5, process, [], null, null, [new("signal", Pin), new("signal", Pin)]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Older_formats_cannot_silently_admit_an_event_inventory(int version) =>
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(version,
            JsonSerializer.SerializeToElement(new { processId = "signals" }), [], null, null, [new("signal", Pin)]));

    [Theory]
    [InlineData("correct")]
    [InlineData("published-key")]
    [InlineData("absent-correlation")]
    [InlineData("null-correlation")]
    [InlineData("blank-correlation")]
    public void A_fixed_midflow_Event_is_a_broadcast_worker(string variant) =>
        BpmnModelRuntime.ValidateEventWorker(Worker(variant), "release");

    [Theory]
    [InlineData("wrong-name")]
    [InlineData("missing-name")]
    [InlineData("start-enabled")]
    [InlineData("start-default")]
    [InlineData("correlated")]
    [InlineData("wrong-type")]
    [InlineData("expression-name")]
    public void Workers_that_can_complete_without_the_signal_or_match_another_cohort_refuse(string variant) =>
        Assert.Throws<BpmnModelValidationException>(() => BpmnModelRuntime.ValidateEventWorker(Worker(variant), "release"));

    private static ExecutableNode Worker(string variant)
    {
        var values = new Dictionary<string, object?> { ["EventName"] = variant == "wrong-name" ? "other" : "release", ["CanStartWorkflow"] = variant == "start-enabled" };
        if (variant == "missing-name") values.Remove("EventName");
        if (variant == "start-default") values.Remove("CanStartWorkflow");
        if (variant is "correlated" or "null-correlation" or "blank-correlation")
            values["CorrelationId"] = variant == "correlated" ? "order-1" : variant == "blank-correlation" ? " " : null;
        var bindings = values.ToDictionary(item => item.Key, item => new RuntimeInputBinding(item.Key, new ValueTypeDescriptor("String"),
            ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement(item.Value), ValueProtectionPolicy.InstanceInline)));
        if (variant == "absent-correlation") bindings["CorrelationId"] = new("CorrelationId", new ValueTypeDescriptor("String"),
            ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            new ValueEnvelope(new ValueTypeDescriptor("String"), ValuePresence.Absent, null, null, ValueProtectionPolicy.InstanceInline));
        if (variant == "expression-name") bindings["EventName"] = new("EventName", new ValueTypeDescriptor("String"),
            ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression, expression: new("CSharp", "\"release\""));
        return new("event", "event", variant == "wrong-type" ? typeof(WriteLine).FullName! : variant is "published-key" or "absent-correlation" ? Event.ActivityType : typeof(Event).FullName!, "1.0.0", "test/clr",
            JsonSerializer.SerializeToElement(new { type = "clr" }), bindings, new Dictionary<string, string>());
    }
}

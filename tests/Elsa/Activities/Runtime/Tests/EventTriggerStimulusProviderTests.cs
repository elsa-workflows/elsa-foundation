using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class EventTriggerStimulusProviderTests
{
    private readonly EventTriggerStimulusProvider _provider = new();

    [Fact]
    public void ProviderId_IsExplicitAndStable()
    {
        Assert.Equal("Elsa.Event", ((IActivityTriggerStimulusProvider)_provider).ProviderId);
    }

    [Fact]
    public void Describe_ReturnsEventStimulus_ForEventNodeWithLiteralName()
    {
        var node = EventNode(eventName: "order-shipped");

        var descriptor = Assert.Single(_provider.Describe(node).Descriptors);

        Assert.Equal("Event", descriptor.StimulusType);
        Assert.Equal(EventStimulus.Hash("order-shipped"), descriptor.StimulusHash);
        Assert.Null(descriptor.CorrelationScope);
    }

    [Fact]
    public void Describe_CarriesCorrelationScope_WhenLiteralCorrelationPresent()
    {
        var node = EventNode(eventName: "order-shipped", correlationId: "order-7");

        var descriptor = Assert.Single(_provider.Describe(node).Descriptors);

        Assert.Equal("order-7", descriptor.CorrelationScope);
    }

    [Fact]
    public void Describe_ReturnsEmpty_ForNonEventActivityType()
    {
        var node = EventNode(eventName: "order-shipped", activityType: "Elsa.WriteLine");

        Assert.False(_provider.Describe(node).IsRecognized);
    }

    [Fact]
    public void Describe_Throws_WhenEventNameLiteralMissing()
    {
        var node = EventNode(eventName: null);

        Assert.Throws<ArgumentException>(() => _provider.Describe(node));
    }

    [Fact]
    public void Describe_Throws_WhenEventNameIsBlank()
    {
        var node = EventNode(eventName: "   ");

        Assert.Throws<ArgumentException>(() => _provider.Describe(node));
    }

    [Fact]
    public void Describe_Throws_WhenEventNameIsNonLiteral()
    {
        var node = EventNode(eventName: null, eventNameBinding: ExpressionBinding(nameof(Event.EventName)));

        Assert.Throws<ArgumentException>(() => _provider.Describe(node));
    }

    [Fact]
    public void Hash_IsDeterministicAndPrefixed()
    {
        Assert.Equal(EventStimulus.Hash("order-shipped"), EventStimulus.Hash("order-shipped"));
        Assert.NotEqual(EventStimulus.Hash("order-shipped"), EventStimulus.Hash("order-cancelled"));
        Assert.StartsWith("sha256:", EventStimulus.Hash("order-shipped"));
    }

    private static ExecutableNode EventNode(
        string? eventName,
        string? correlationId = null,
        string activityType = "Elsa.Event",
        RuntimeInputBinding? eventNameBinding = null)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        if (eventNameBinding is not null)
            bindings[nameof(Event.EventName)] = eventNameBinding;
        else if (eventName is not null)
            bindings[nameof(Event.EventName)] = LiteralBinding(nameof(Event.EventName), eventName);
        if (correlationId is not null)
            bindings[nameof(Event.CorrelationId)] = LiteralBinding(nameof(Event.CorrelationId), correlationId);

        return new ExecutableNode(
            executableNodeId: "node-event",
            authoredActivityId: "authored-node-event",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: bindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
    }

    private static RuntimeInputBinding LiteralBinding(string name, string value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return new RuntimeInputBinding(name, RuntimeInputBindingSource.Literal, literalValue: document.RootElement.Clone());
    }

    private static RuntimeInputBinding ExpressionBinding(string name) =>
        new(name, RuntimeInputBindingSource.Expression, expression: new RuntimeExpressionBinding("JavaScript", "input.eventName"));
}

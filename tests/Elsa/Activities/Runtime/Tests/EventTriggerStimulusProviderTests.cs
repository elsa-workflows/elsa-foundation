using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task Execute_returns_event_name_as_one_atomic_result()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var activation = await TypedActivityTestActivation.ActivateAsync<Event>(
            services,
            new Dictionary<string, object?>
            {
                [nameof(Event.EventName)] = "order-shipped",
                [nameof(Event.CorrelationId)] = "order-7"
            });
        var activity = Assert.IsType<Event>(activation.Activity);
        var context = new SimpleActivityExecutionContext(services, activity, CancellationToken.None);

        var transition = await ((IActivity)activity).ExecuteAsync(context);
        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<EventResult>>(transition);

        Assert.Equal("order-shipped", completion.Result.EventName);
        Assert.Equal("Done", completion.Outcome);
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
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: bindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
    }

    private static RuntimeInputBinding LiteralBinding(string name, string value)
    {
        var type = new ValueTypeDescriptor("String");
        return new RuntimeInputBinding(
            name,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }

    private static RuntimeInputBinding ExpressionBinding(string name) =>
        new(
            name,
            new ValueTypeDescriptor("String"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                "JavaScript",
                "input.eventName",
                new RuntimeValueTypeDescriptor("alias", "String", null),
                capabilityProfile: ExpressionCapabilityProfiles.BindingPureV1));
}

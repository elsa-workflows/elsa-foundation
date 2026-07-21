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
                [nameof(Event.CorrelationId)] = "order-7",
                [nameof(Event.CanStartWorkflow)] = true
            });
        var activity = Assert.IsType<Event>(activation.Activity);
        var context = new SimpleActivityExecutionContext(
            activity,
            CancellationToken.None,
            invocationId: "event-invocation",
            executableNodeId: "event-node");

        var transition = await ((IActivity)activity).ExecuteAsync(context.ToActivityExecutionContext());
        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<EventResult>>(transition);

        Assert.Equal("order-shipped", completion.Result.EventName);
        Assert.Equal("Done", completion.Outcome);
    }

    [Fact]
    public void Describe_ReturnsRecognizedEmpty_WhenCanStartWorkflowIsFalse()
    {
        // A mid-flow wait node (spec 116) is recognized but declares itself a non-start: not indexed,
        // no publish failure — and the literal-event-name rule does not apply to it.
        var node = EventNode(eventName: null, canStartWorkflow: false);

        var result = _provider.Describe(node);

        Assert.True(result.IsRecognized);
        Assert.Empty(result.Descriptors);
    }

    [Fact]
    public void Describe_IndexesNode_WhenCanStartWorkflowIsExplicitlyTrue()
    {
        var node = EventNode(eventName: "order-shipped", canStartWorkflow: true);

        var descriptor = Assert.Single(_provider.Describe(node).Descriptors);

        Assert.Equal(EventStimulus.Hash("order-shipped"), descriptor.StimulusHash);
    }

    [Fact]
    public async Task Execute_suspends_with_event_stimulus_registration_when_it_cannot_start()
    {
        var activity = await NewEventActivityAsync(canStartWorkflow: false);

        var transition = await ((IActivity)activity).ExecuteAsync(DirectContext());

        var suspension = Assert.IsAssignableFrom<IStatefulActivitySuspensionTransition<EventWaitState>>(transition);
        Assert.Equal(new EventWaitState("order-shipped"), suspension.State);
        var registration = Assert.IsType<ActivityTriggerRegistration<EventReceived>>(Assert.Single(suspension.Registrations));
        Assert.Equal(Event.ResumeTargetId, registration.ResumeTargetKey);
        Assert.Equal(EventStimulus.StimulusType, registration.StimulusType);
        Assert.Equal(EventStimulus.Hash("order-shipped"), registration.StimulusHash);
    }

    [Fact]
    public async Task Execute_suspends_when_the_start_trigger_targeted_another_node()
    {
        // In a trigger-started run every non-trigger Event node waits, regardless of CanStartWorkflow.
        var activity = await NewEventActivityAsync(canStartWorkflow: true);

        var transition = await ((IActivity)activity).ExecuteAsync(
            new ActivityExecutionContext("workflow-1", "event-invocation", "attempt-1", "event-node", CancellationToken.None, null, "other-node"));

        Assert.IsAssignableFrom<IStatefulActivitySuspensionTransition<EventWaitState>>(transition);
    }

    [Fact]
    public async Task Execute_completes_when_the_start_trigger_targeted_this_node()
    {
        var activity = await NewEventActivityAsync(canStartWorkflow: false);
        var payload = JsonSerializer.SerializeToElement(new EventReceived("order-shipped"));

        var transition = await ((IActivity)activity).ExecuteAsync(
            new ActivityExecutionContext("workflow-1", "event-invocation", "attempt-1", "event-node", CancellationToken.None, payload, "event-node"));

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<EventResult>>(transition);
        Assert.Equal("order-shipped", completion.Result.EventName);
    }

    [Fact]
    public async Task Resume_completes_with_the_committed_event_name()
    {
        var activity = await NewEventActivityAsync(canStartWorkflow: false);

        var transition = await ((IStatefulActivity<EventResult, EventWaitState, EventReceived>)activity).ResumeAsync(
            new ActivityResumeContext<EventWaitState, EventReceived>(
                DirectContext(),
                new EventWaitState("order-shipped"),
                new EventReceived("order-shipped"),
                registrationId: "registration-1",
                deliveryId: "delivery-1"));

        var completion = Assert.IsAssignableFrom<IActivityCompletionTransition<EventResult>>(transition);
        Assert.Equal("order-shipped", completion.Result.EventName);
        Assert.Equal("Done", completion.Outcome);
    }

    private static async Task<Event> NewEventActivityAsync(bool canStartWorkflow)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var activation = await TypedActivityTestActivation.ActivateAsync<Event>(
            services,
            new Dictionary<string, object?>
            {
                [nameof(Event.EventName)] = "order-shipped",
                [nameof(Event.CorrelationId)] = null,
                [nameof(Event.CanStartWorkflow)] = canStartWorkflow
            });
        return Assert.IsType<Event>(activation.Activity);
    }

    private static ActivityExecutionContext DirectContext() =>
        new SimpleActivityExecutionContext(
            new Event(),
            CancellationToken.None,
            invocationId: "event-invocation",
            executableNodeId: "event-node").ToActivityExecutionContext();

    private static ExecutableNode EventNode(
        string? eventName,
        string? correlationId = null,
        string activityType = "Elsa.Event",
        RuntimeInputBinding? eventNameBinding = null,
        bool? canStartWorkflow = null)
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        if (eventNameBinding is not null)
            bindings[nameof(Event.EventName)] = eventNameBinding;
        else if (eventName is not null)
            bindings[nameof(Event.EventName)] = LiteralBinding(nameof(Event.EventName), eventName);
        if (correlationId is not null)
            bindings[nameof(Event.CorrelationId)] = LiteralBinding(nameof(Event.CorrelationId), correlationId);
        if (canStartWorkflow is not null)
            bindings[nameof(Event.CanStartWorkflow)] = LiteralBinding(nameof(Event.CanStartWorkflow), canStartWorkflow);

        return new ExecutableNode(
            executableNodeId: "node-event",
            authoredActivityId: "authored-node-event",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: bindings,
            metadata: new Dictionary<string, string>());
    }

    private static RuntimeInputBinding LiteralBinding(string name, object value)
    {
        var type = new ValueTypeDescriptor(value is bool ? "Boolean" : "String");
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

using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Services;

/// <summary>Named-event Test Drive contribution; routing identity always comes from the selected binding.</summary>
internal sealed class EventTestRunDriver : ITestRunTriggerDriver
{
    public string DriverId => "Elsa.Event";

    public bool Supports(TestRunTriggerContext context) =>
        context.ProviderId == DriverId && context.Node.ActivityType == Event.ActivityType;

    public IReadOnlyCollection<TestRunTriggerMode> Describe(TestRunTriggerContext context) =>
    [
        new("event", "Deliver event", "Deliver one named event to this test run only.", true,
            configurationSchema: JsonSerializer.SerializeToElement(new
            {
                type = "object", additionalProperties = false,
                properties = new { payload = new { } }
            }),
            configuration: JsonSerializer.SerializeToElement(new
            {
                eventName = context.Node.InputBindings.Values.First(binding => binding.InputName == nameof(Event.EventName)).LiteralValue!.Value.GetString(),
                correlationId = context.Binding.CorrelationScope
            }))
    ];

    public TestRunTriggerValidation Validate(TestRunTriggerContext context, string modeId, JsonElement? data)
    {
        if (!Supports(context) || modeId != "event")
            return Refuse("unsupported-event-mode", "This event test mode is unavailable.");
        return EventTestRunInputs.Validate(data);
    }

    public ValueTask<TestRunTriggerPreparation> PrepareAsync(TestRunTriggerPrepareContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Validate(context.Trigger, context.ModeId, context.Data).IsValid)
            throw new ArgumentException("Invalid event Test Drive configuration.");
        var payload = EventTestRunInputs.Payload(context.Data);
        return ValueTask.FromResult(TestRunTriggerPreparation.Immediate(payload));
    }

    private static TestRunTriggerValidation Refuse(string code, string message) =>
        TestRunTriggerValidation.Failure([new(code, message)]);
}

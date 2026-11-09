using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Services;

internal sealed class EventTestRunWaitDriver : ITestRunWaitDriver
{
    public string DriverId => "Elsa.Event";

    public bool Supports(TestRunWaitContext context) =>
        context.Node.ActivityType == Event.ActivityType && context.Wait.StimulusType == EventStimulus.StimulusType &&
        context.Wait.ResumeTargetId == WorkflowExecutableResumeTarget.ComposeScopedId(context.Node.ExecutableNodeId, Event.ResumeTargetId);

    public IReadOnlyCollection<TestRunTriggerMode> Describe(TestRunWaitContext context) =>
    [new("event", "Deliver event", "Deliver to this active wait only.", true,
        configurationSchema: JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = false, properties = new { payload = new { } } }),
        configuration: JsonSerializer.SerializeToElement(new
        {
            eventName = context.Node.InputBindings.Values.FirstOrDefault(binding => binding.InputName == nameof(Event.EventName))?.LiteralValue?.GetString() ?? "Named event"
        }))];

    public TestRunTriggerValidation Validate(TestRunWaitContext context, string modeId, JsonElement? data) =>
        !Supports(context) || modeId != "event"
            ? TestRunTriggerValidation.Failure([new("unsupported-event-wait", "This event wait mode is unavailable.")])
            : EventTestRunInputs.Validate(data);

    public ValueTask<JsonElement?> PrepareAsync(TestRunWaitPrepareContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Validate(context.Wait, context.ModeId, context.Data).IsValid)
            throw new ArgumentException("Invalid event test delivery.");
        return ValueTask.FromResult(EventTestRunInputs.Payload(context.Data));
    }
}

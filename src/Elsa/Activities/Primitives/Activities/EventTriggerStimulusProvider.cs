using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// The <see cref="IActivityTriggerStimulusProvider"/> for the <see cref="Event"/> start trigger (W7, E3-1). It
/// recognizes published <see cref="Event"/> nodes and derives their stimulus identity from the authored
/// <see cref="Event.EventName"/> literal, so the trigger extractor can index the event at publish time over the
/// pinned artifact.
/// </summary>
/// <remarks>
/// The event name must be an authored literal — the stimulus identity is fixed at publish time, before any run
/// exists, so a non-literal (expression-bound) event name has no value to hash and throws, failing the publish
/// rather than persisting an unroutable trigger.
/// </remarks>
public sealed class EventTriggerStimulusProvider : IActivityTriggerStimulusProvider
{
    public string ProviderId => "Elsa.Event";
    private const string EventNameInput = nameof(Event.EventName);
    private const string CorrelationIdInput = nameof(Event.CorrelationId);
    private const string CanStartWorkflowInput = nameof(Event.CanStartWorkflow);

    public ActivityTriggerStimulusResult Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!StringComparer.Ordinal.Equals(node.ActivityType, Event.ActivityType))
            return ActivityTriggerStimulusResult.NotRecognized;

        // A mid-flow wait node (CanStartWorkflow authored false, spec 116) is recognized but declares itself a
        // non-start: no bindings, no publish failure — the HttpEndpoint provider pattern. Unlike HttpEndpoint,
        // starting is the default here (unauthored counts as true), preserving the activity's start-first identity.
        if (ReadLiteralBool(node, CanStartWorkflowInput) is false)
            return ActivityTriggerStimulusResult.Recognized([]);

        var eventName = ReadLiteralString(node, EventNameInput)
            ?? throw new ArgumentException(
                $"Event trigger node '{node.ExecutableNodeId}' has no literal '{EventNameInput}'. A start trigger's " +
                "event name must be an authored literal so its stimulus is fixed at publish time.");

        var correlationScope = ReadLiteralString(node, CorrelationIdInput);
        return ActivityTriggerStimulusResult.Recognized([EventStimulus.Describe(eventName, correlationScope)]);
    }

    private static string? ReadLiteralString(ExecutableNode node, string inputName)
    {
        if (ReadLiteral(node, inputName) is not { } literal)
            return null;

        var value = literal.ValueKind == JsonValueKind.String ? literal.GetString() : literal.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool? ReadLiteralBool(ExecutableNode node, string inputName) =>
        ReadLiteral(node, inputName) switch
        {
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            _ => null
        };

    private static JsonElement? ReadLiteral(ExecutableNode node, string inputName)
    {
        var binding = node.InputBindings
            .FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Key, inputName))
            .Value;

        if (binding?.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { } literal)
            return null;

        return literal.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : literal;
    }
}

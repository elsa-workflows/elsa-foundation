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
    private const string EventNameInput = nameof(Event.EventName);
    private const string CorrelationIdInput = nameof(Event.CorrelationId);

    public IReadOnlyCollection<TriggerStimulusDescriptor> Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!StringComparer.Ordinal.Equals(node.ActivityType, Event.ActivityType))
            return [];

        var eventName = ReadLiteralString(node, EventNameInput)
            ?? throw new ArgumentException(
                $"Event trigger node '{node.ExecutableNodeId}' has no literal '{EventNameInput}'. A start trigger's " +
                "event name must be an authored literal so its stimulus is fixed at publish time.");

        var correlationScope = ReadLiteralString(node, CorrelationIdInput);
        return [EventStimulus.Describe(eventName, correlationScope)];
    }

    private static string? ReadLiteralString(ExecutableNode node, string inputName)
    {
        var binding = node.InputBindings
            .FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Key, inputName))
            .Value;

        if (binding?.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { } literal)
            return null;

        if (literal.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var value = literal.ValueKind == JsonValueKind.String ? literal.GetString() : literal.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}

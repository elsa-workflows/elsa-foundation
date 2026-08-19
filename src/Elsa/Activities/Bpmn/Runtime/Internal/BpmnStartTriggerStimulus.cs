using System.Security.Cryptography;
using System.Text;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// Well-known trigger-binding metadata keys the BPMN start-trigger surface stamps (spec 117). The extractor
/// carries <see cref="TriggerStimulusDescriptor.Metadata"/> verbatim onto <c>WorkflowTriggerBinding.Metadata</c>,
/// which the stimulus router forwards through the start path (D4) so <c>BpmnExecutionEngine.StartAsync</c> can
/// resolve which event-defined start element a trigger delivery targeted.
/// </summary>
public static class BpmnStartTrigger
{
    /// <summary>The event-defined start element id a start trigger binding carries (read by <c>StartAsync</c> on trigger delivery).</summary>
    public const string StartElementIdMetadataKey = "bpmn.startElementId";
}

/// <summary>
/// Derives the stimulus identity of a BPMN <b>message/signal</b> start element (spec 117 D2). A message/signal
/// start collapses onto the engine's named-event routing pair so <b>one</b> event delivery both starts matching
/// processes and resumes waiting catch events. The derivation is intentionally identical to
/// <c>Elsa.Activities.Primitives.Activities.EventStimulus</c> — the same <c>StimulusType</c> and SHA-256 hash of
/// the event name — so a raised <c>Event</c> stimulus and a published BPMN message/signal start resolve to the
/// same key. The value is duplicated here (rather than referenced) to keep the BPMN module's dependency envelope
/// free of the Primitives activities package; <c>BpmnStartTriggerStimulusParityTests</c> pins the equivalence so
/// the two cannot drift.
/// </summary>
public static class BpmnMessageStartStimulus
{
    /// <summary>The named-event stimulus type shared with <c>EventStimulus.StimulusType</c>.</summary>
    public const string StimulusType = "Event";

    /// <summary>Computes the deterministic stimulus hash for an event name — identical to <c>EventStimulus.Hash</c>.</summary>
    public static string Hash(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(eventName));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    /// <summary>
    /// Builds the start-trigger descriptor for a message/signal start element, reading the event name from the
    /// element's single event definition (<see cref="BpmnEventDefinitionProperties.Name"/>). A missing/blank name
    /// throws — the stimulus identity is fixed at publish time, so an unresolvable name fails the publish rather
    /// than persisting an unroutable start trigger.
    /// </summary>
    public static TriggerStimulusDescriptor Describe(BpmnElement element)
    {
        var definition = element.EventDefinitions.Single();
        if (!definition.Properties.TryGetValue(BpmnEventDefinitionProperties.Name, out var name) || string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                $"BPMN {definition.Type} start event '{element.ElementId}' has no '{BpmnEventDefinitionProperties.Name}' property; a message/signal start's event name is required so its stimulus is fixed at publish time.");

        return new TriggerStimulusDescriptor(
            StimulusType,
            Hash(name),
            correlationScope: null,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { [BpmnStartTrigger.StartElementIdMetadataKey] = element.ElementId });
    }
}

/// <summary>
/// Derives the stimulus identity and recurring-schedule spec of a BPMN <b>timer</b> start element (spec 117 D3).
/// Timer stimuli are pump-internal (never externally sent), so the element id is folded into the hash to give
/// per-element uniqueness within a process, under a BPMN-owned <see cref="StimulusType"/> that fully isolates it
/// from the <c>Timer</c>/<c>Cron</c> activities. The stimulus provider (trigger binding) and the recurring
/// schedule provider both derive their <c>(StimulusType, StimulusHash)</c> here, so the pump's <c>StartOnly</c>
/// dispatch for a due schedule matches exactly the element's start binding.
/// </summary>
public static class BpmnTimerStartStimulus
{
    /// <summary>The BPMN-owned timer-start stimulus type (isolated from the <c>Timer</c>/<c>Cron</c> activities).</summary>
    public const string StimulusType = "Bpmn.TimerStart";

    /// <summary>The resolved recurrence spec of a timer start element: interval-xor-cron, normalized for a stable hash.</summary>
    public readonly record struct Spec(RecurringScheduleKind Kind, string Expression);

    /// <summary>
    /// Resolves a timer start element's recurrence spec from its event-definition properties (spec 117 D3):
    /// exactly one of <see cref="BpmnEventDefinitionProperties.Interval"/> (ISO-8601 duration) xor
    /// <see cref="BpmnEventDefinitionProperties.Cron"/>. Missing-both or both-present throws, failing the publish.
    /// </summary>
    public static Spec ResolveSpec(BpmnElement element)
    {
        var properties = element.EventDefinitions.Single().Properties;
        var hasInterval = properties.TryGetValue(BpmnEventDefinitionProperties.Interval, out var interval) && !string.IsNullOrWhiteSpace(interval);
        var hasCron = properties.TryGetValue(BpmnEventDefinitionProperties.Cron, out var cron) && !string.IsNullOrWhiteSpace(cron);

        if (hasInterval == hasCron)
            throw new ArgumentException(
                $"BPMN timer start event '{element.ElementId}' must declare exactly one of '{BpmnEventDefinitionProperties.Interval}' or '{BpmnEventDefinitionProperties.Cron}'.");

        return hasInterval
            ? new Spec(RecurringScheduleKind.Interval, interval!.Trim())
            : new Spec(RecurringScheduleKind.Cron, NormalizeCron(cron!));
    }

    /// <summary>Computes the deterministic stimulus hash for a timer start element (element id folded in for per-element uniqueness).</summary>
    public static string Hash(string elementId, string normalizedExpression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedExpression);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{elementId}\n{normalizedExpression}"));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    /// <summary>Builds the start-trigger descriptor for a timer start element (carries the start element id in metadata).</summary>
    public static TriggerStimulusDescriptor DescribeTrigger(BpmnElement element)
    {
        var spec = ResolveSpec(element);
        return new TriggerStimulusDescriptor(
            StimulusType,
            Hash(element.ElementId, spec.Expression),
            correlationScope: null,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal) { [BpmnStartTrigger.StartElementIdMetadataKey] = element.ElementId });
    }

    /// <summary>Builds the recurring-schedule descriptor for a timer start element (same routing pair as the trigger).</summary>
    public static RecurringScheduleDescriptor DescribeSchedule(BpmnElement element)
    {
        var spec = ResolveSpec(element);
        return new RecurringScheduleDescriptor(StimulusType, Hash(element.ElementId, spec.Expression), spec.Kind, spec.Expression);
    }

    private static string NormalizeCron(string expression) =>
        string.Join(' ', expression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

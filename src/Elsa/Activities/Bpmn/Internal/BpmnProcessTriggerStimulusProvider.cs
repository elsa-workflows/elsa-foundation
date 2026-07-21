using System.Text.Json;
using Elsa.Activities.Bpmn.Exceptions;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// The <see cref="IActivityTriggerStimulusProvider"/> for the BPMN event-defined start surface (spec 117 D1/D2).
/// It recognizes published <see cref="BpmnProcessActivity"/> nodes and emits one start-trigger descriptor per
/// event-defined start element: message/signal via <see cref="BpmnMessageStartStimulus"/> (named-event stimulus,
/// shared with <c>Event</c>), timer via <see cref="BpmnTimerStartStimulus"/> (BPMN-owned pump-internal stimulus,
/// element id folded into the hash). Each descriptor carries the start element id in
/// <see cref="TriggerStimulusDescriptor.Metadata"/> so the start dispatch can seed exactly that element.
/// </summary>
/// <remarks>
/// A process with no event-defined start elements returns <see cref="ActivityTriggerStimulusResult.Recognized"/>
/// with an empty descriptor set (<c>IntentionallyNonStarting</c>) — none-start processes are started by direct
/// invocation, and the recognized-but-empty result never fails the publish. Root position is not recoverable from
/// the published node alone, so a <see cref="BpmnProcessActivity"/> bound as a child sets
/// <see cref="BpmnProcessActivity.CanStartWorkflow"/> to <c>false</c> (unauthored counts as <c>true</c>,
/// preserving the root-process identity) and is likewise recognized-but-empty. Reading the structure or a start
/// element's authored properties throws <see cref="ArgumentException"/> on an authoring error, failing the publish
/// through the trigger preflight channel rather than persisting an unroutable start trigger.
/// </remarks>
public sealed class BpmnProcessTriggerStimulusProvider : IActivityTriggerStimulusProvider
{
    public string ProviderId => "Elsa.Bpmn.Process";

    public ActivityTriggerStimulusResult Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!StringComparer.Ordinal.Equals(node.ActivityType, typeof(BpmnProcessActivity).FullName))
            return ActivityTriggerStimulusResult.NotRecognized;

        // Opt-out for a nested BpmnProcess (spec 117 D1): only an explicit literal false suppresses the start
        // trigger surface; unauthored/true preserves the root-process identity. Recognized-but-empty, no failure.
        if (BpmnStartTriggerNodeInputs.ReadCanStartWorkflow(node) is false)
            return ActivityTriggerStimulusResult.Recognized([]);

        var descriptors = ReadEventDefinedStartElements(node)
            .Select(Describe)
            .ToArray();

        return ActivityTriggerStimulusResult.Recognized(descriptors);
    }

    private static TriggerStimulusDescriptor Describe(BpmnElement element) =>
        StringComparer.Ordinal.Equals(element.EventDefinitions.Single().Type, BpmnEventDefinitionTypes.Timer)
            ? BpmnTimerStartStimulus.DescribeTrigger(element)
            : BpmnMessageStartStimulus.Describe(element);

    /// <summary>
    /// Reads the event-defined start elements from the published node's BPMN structure. Structural authoring
    /// errors (an invalid graph) are rethrown as <see cref="ArgumentException"/> so they flow through the trigger
    /// preflight channel with this provider attributed, matching how the property-level errors surface.
    /// </summary>
    internal static IReadOnlyList<BpmnElement> ReadEventDefinedStartElements(ExecutableNode node)
    {
        BpmnGraph graph;
        try
        {
            graph = BpmnGraph.From(node);
        }
        catch (BpmnExecutionException exception)
        {
            throw new ArgumentException(
                $"BPMN process node '{node.ExecutableNodeId}' has an invalid structure: {exception.Message}", exception);
        }

        return graph.StartEvents.Where(element => element.EventDefinitions.Count > 0).ToArray();
    }
}

/// <summary>Reads the <see cref="BpmnProcessActivity.CanStartWorkflow"/> literal off a published BPMN process node (spec 117).</summary>
internal static class BpmnStartTriggerNodeInputs
{
    private const string CanStartWorkflowInput = nameof(BpmnProcessActivity.CanStartWorkflow);

    public static bool? ReadCanStartWorkflow(ExecutableNode node)
    {
        var binding = node.InputBindings
            .FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Key, CanStartWorkflowInput))
            .Value;

        if (binding?.Source != RuntimeInputBindingSource.Literal || binding.LiteralValue is not { } literal)
            return null;

        return literal.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(literal.GetString(), out var parsed) => parsed,
            _ => null
        };
    }
}

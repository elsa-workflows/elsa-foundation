using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// The <see cref="IRecurringTriggerScheduleProvider"/> for BPMN <b>timer</b> start events (spec 117 D3). It
/// recognizes published <see cref="BpmnProcessActivity"/> nodes and emits one <see cref="RecurringScheduleDescriptor"/>
/// per timer start element, each with the same <c>(StimulusType, StimulusHash)</c> pair
/// <see cref="BpmnProcessTriggerStimulusProvider"/> emits for that element — so the recurring-trigger pump's
/// <c>StartOnly</c> dispatch of a due schedule matches the element's start binding. A process with no timer start
/// elements (or a nested process with <see cref="BpmnProcessActivity.CanStartWorkflow"/> = <c>false</c>) returns an
/// empty collection: no recurring schedule, no publish failure.
/// </summary>
public sealed class BpmnProcessRecurringScheduleProvider : IRecurringTriggerScheduleProvider
{
    public string ProviderId => "Elsa.Bpmn.Process";

    public IReadOnlyCollection<RecurringScheduleDescriptor> Describe(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!StringComparer.Ordinal.Equals(node.ActivityType, typeof(BpmnProcessActivity).FullName))
            return [];

        if (BpmnStartTriggerNodeInputs.ReadCanStartWorkflow(node) is false)
            return [];

        return BpmnProcessTriggerStimulusProvider.ReadEventDefinedStartElements(node)
            .Where(element => StringComparer.Ordinal.Equals(element.EventDefinitions.Single().Type, BpmnEventDefinitionTypes.Timer))
            .Select(BpmnTimerStartStimulus.DescribeSchedule)
            .ToArray();
    }
}

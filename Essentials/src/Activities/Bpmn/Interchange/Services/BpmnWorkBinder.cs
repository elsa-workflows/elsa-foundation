using Elsa.Activities.Bpmn.Interchange.Contracts;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>The default Foundation binding policy; no library semantics or service resolution lives here.</summary>
internal sealed class BpmnWorkBinder : IBpmnWorkBinder
{
    public BpmnWorkBindingResult Bind(BpmnWorkBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var activity = request.Kind switch
        {
            BpmnWorkBindingKind.TimerWait => Node(request, BpmnDocumentImporter.DefaultDelayActivityVersionId, Literal("Duration", request.Value)),
            BpmnWorkBindingKind.MessageWait or BpmnWorkBindingKind.SignalWait => Node(request, BpmnDocumentImporter.DefaultEventActivityVersionId,
                Literal("EventName", request.Value), Literal("CanStartWorkflow", false)),
            BpmnWorkBindingKind.MessagePublish or BpmnWorkBindingKind.SignalPublish => Node(request, BpmnDocumentImporter.DefaultPublishEventActivityVersionId,
                Literal("EventName", request.Value)),
            BpmnWorkBindingKind.CallProcess when !string.IsNullOrWhiteSpace(request.WorkflowDefinitionId) => Node(request,
                BpmnDocumentImporter.DefaultDispatchWorkflowActivityVersionId, Literal("WorkflowDefinitionId", request.WorkflowDefinitionId),
                Literal("WaitForCompletion", request.WaitForCompletion)),
            BpmnWorkBindingKind.NestedProcess when request.NestedProcess is not null => request.NestedProcess with
                { Inputs = [Literal(nameof(BpmnProcessActivity.CanStartWorkflow), false)] },
            _ => null
        };
        if (activity is not null)
            return new(activity, BpmnImportCapabilityAvailability.Available, "Foundation supplied the required activity binding.");
        if (request.Kind is BpmnWorkBindingKind.UnboundTask or BpmnWorkBindingKind.CallProcess)
            return new(null, BpmnImportCapabilityAvailability.RequiresBinding, request.Kind == BpmnWorkBindingKind.CallProcess
                ? $"Call activity '{request.ElementId}' imported unbound; bind a DispatchWorkflow activity to execute it."
                : "An author must supply an activity binding before execution.");
        return new(null, BpmnImportCapabilityAvailability.Unsupported,
            "Foundation has no activity binding for this retained BPMN descriptor.");
    }

    private static ActivityNode Node(BpmnWorkBindingRequest request, string activityVersionId, params ArgumentState[] inputs) =>
        new(request.BindingRef, activityVersionId, inputs, []);

    private static ArgumentState Literal(string key, object? value) => new(key, new ArgumentValue(value, "Literal"), null, null, null, null);
}

using Elsa.Activities.Bpmn.Interchange.Contracts;
using System.Globalization;
using System.Xml;
using Elsa.Activities.Bpmn.Interchange.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Core.BpmnActivityIdentities;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>The default Foundation binding policy; no library semantics or service resolution lives here.</summary>
internal sealed class BpmnWorkBinder : IBpmnWorkBinder
{
    public BpmnWorkBindingResult Bind(BpmnWorkBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind == BpmnWorkBindingKind.TimerWait)
        {
            try
            {
                // XML retains the BPMN descriptor separately. Delay's literal is a Foundation
                // TimeSpan, whose publisher accepts the invariant constant representation.
                var duration = XmlConvert.ToTimeSpan(request.Value!);
                return new(Node(request, BpmnDocumentImporter.DefaultDelayActivityVersionId,
                    Literal("Duration", duration.ToString("c", CultureInfo.InvariantCulture))),
                    BpmnImportCapabilityAvailability.Available, "Foundation supplied the duration wait binding.");
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentNullException)
            {
                return new(null, BpmnImportCapabilityAvailability.Unsupported,
                    $"Timer '{request.ElementId}' has no representable Foundation duration wait binding.");
            }
        }
        var activity = request.Kind switch
        {
            BpmnWorkBindingKind.MessageWait or BpmnWorkBindingKind.SignalWait => Node(request, BpmnDocumentImporter.DefaultEventActivityVersionId,
                Literal("EventName", request.Value), Literal("CanStartWorkflow", false)),
            BpmnWorkBindingKind.MessagePublish or BpmnWorkBindingKind.SignalPublish => Node(request, BpmnDocumentImporter.DefaultPublishEventActivityVersionId,
                Literal("EventName", request.Value)),
            BpmnWorkBindingKind.CallProcess when !string.IsNullOrWhiteSpace(request.WorkflowDefinitionId) => Node(request,
                BpmnDocumentImporter.DefaultDispatchWorkflowActivityVersionId, Literal("WorkflowDefinitionId", request.WorkflowDefinitionId),
                Literal("WaitForCompletion", request.WaitForCompletion)),
            BpmnWorkBindingKind.NestedProcess when request.NestedProcess is not null => request.NestedProcess with
                { Inputs = [Literal(BpmnProcessActivity.CanStartWorkflowInput, false)] },
            _ => null
        };
        if (activity is not null)
            return new(activity, BpmnImportCapabilityAvailability.Available, "Foundation supplied the required activity binding.");
        // An exported exact binding is rebound as written; the catalog session then verifies that version is visible.
        if (request.Kind == BpmnWorkBindingKind.UnboundTask && request.RetainedActivity is { } retained)
            return retained.Activity is { } bound
                ? new(bound, BpmnImportCapabilityAvailability.Available, "The document carries an exact Elsa activity binding.")
                : new(null, BpmnImportCapabilityAvailability.Unsupported, $"{retained.Problem} Bind the task's activity explicitly.");
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

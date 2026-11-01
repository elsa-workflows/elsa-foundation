using System.Text.Json;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed class BpmnModelActivityRuntime(BpmnEmbeddingBoundary boundary) : IBpmnModelActivityRuntime
{
    public async ValueTask<ActivityTransition<RunBpmnResult, RunBpmnState>> ExecuteAsync(ActivityExecutionContext context, string model)
    {
        var invocation = await boundary.PrepareAsync(context, model);
        return ActivityTransition<RunBpmnResult, RunBpmnState>.Suspend(new(1, BpmnEmbeddingJson.Write(invocation)),
            [new ActivityTriggerRegistration<RunBpmnCompletion>(BpmnEmbeddingBoundary.ResumeKey, BpmnEmbeddingBoundary.StimulusType, invocation.ExecutionId)], 1);
    }

    public async ValueTask<ActivityTransition<RunBpmnResult, RunBpmnState>> ResumeAsync(ActivityResumeContext<RunBpmnState, RunBpmnCompletion> context)
    {
        var invocation = BpmnEmbeddingBoundary.Read(context.State);
        if (invocation.RegistrationId != context.RegistrationId || invocation.NativeExecutionId != context.WorkflowExecutionId ||
            invocation.NativeInvocationId != context.InvocationId || invocation.NativeNodeId != context.ExecutableNodeId || context.Trigger.FormatVersion != 1 ||
            !JsonElement.DeepEquals(context.State.Invocation, context.Trigger.Invocation)) throw BpmnEmbeddingJson.Invalid();
        var model = await boundary.RequireModelAsync(invocation, context.CancellationToken);
        if (model.TerminalResult is not { } result || !JsonElement.DeepEquals(result, context.Trigger.Result)) throw BpmnEmbeddingJson.Invalid();
        return result.GetProperty("status").GetString() switch
        {
            "Completed" => ActivityTransition<RunBpmnResult, RunBpmnState>.Complete(new(invocation.ExecutionId, result), "Done"),
            "Faulted" => ActivityTransition<RunBpmnResult, RunBpmnState>.Fault(new("bpmn.model-faulted", "The embedded BPMN model faulted.")),
            "Cancelled" => ActivityTransition<RunBpmnResult, RunBpmnState>.Cancel("The embedded BPMN model was cancelled."),
            _ => throw BpmnEmbeddingJson.Invalid()
        };
    }
}

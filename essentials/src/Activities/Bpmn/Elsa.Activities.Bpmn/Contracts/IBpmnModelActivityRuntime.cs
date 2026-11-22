using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Contracts;

/// <summary>Runs the native activity boundary around one pinned model invocation.</summary>
public interface IBpmnModelActivityRuntime
{
    ValueTask<ActivityTransition<RunBpmnResult, RunBpmnState>> ExecuteAsync(ActivityExecutionContext context, string model);
    ValueTask<ActivityTransition<RunBpmnResult, RunBpmnState>> ResumeAsync(ActivityResumeContext<RunBpmnState, RunBpmnCompletion> context);
}

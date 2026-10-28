using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Models;

/// <summary>The one atomic result returned by a DispatchWorkflow activity attempt.</summary>
public sealed record DispatchWorkflowActivityResult(
    [property: Output(Key = "ChildWorkflowExecutionId", Path = "childWorkflowExecutionId")]
    string ChildWorkflowExecutionId,
    [property: Output(Key = "Result", Path = "result", IsRequired = false)]
    DispatchWorkflowResult? Result) : IRuntimeChildControlCompletion
{
    RuntimeChildControlTransport? IRuntimeChildControlCompletion.ChildControl => Result?.Control;
}

/// <summary>The complete private state needed to validate a waited child completion.</summary>
public sealed record DispatchWorkflowState(string ChildWorkflowExecutionId);

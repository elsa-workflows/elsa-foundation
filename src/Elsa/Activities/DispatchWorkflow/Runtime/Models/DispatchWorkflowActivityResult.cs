using Elsa.Activities.Runtime.Core.Attributes;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Models;

/// <summary>The one atomic result returned by a DispatchWorkflow activity attempt.</summary>
public sealed record DispatchWorkflowActivityResult(
    [property: Output(Key = "ChildWorkflowExecutionId", Path = "childWorkflowExecutionId")]
    string ChildWorkflowExecutionId,
    [property: Output(Key = "Result", Path = "result", IsRequired = false)]
    DispatchWorkflowResult? Result);

/// <summary>The complete private state needed to validate a waited child completion.</summary>
public sealed record DispatchWorkflowState(string ChildWorkflowExecutionId);

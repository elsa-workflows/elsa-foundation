using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Models;

/// <summary>The one atomic result returned by a DispatchWorkflow activity attempt.</summary>
public sealed record DispatchWorkflowActivityResult(
    [property: Output(Key = "ChildWorkflowExecutionId", Path = "childWorkflowExecutionId")]
    string ChildWorkflowExecutionId,
    [property: Output(Key = "Status", Path = "status",
        Description = "The outcome the dispatch selected: Dispatched, Completed, Faulted, Cancelled or DispatchFailed.")]
    string Status,
    [property: Output(Key = "Output", Path = "output", IsRequired = false,
        Description = DispatchWorkflowActivityResult.OutputDescription)]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JsonElement? Output,
    // The stored terminal result: status, allow-listed diagnostics and child control. Not an output (spec 1245 FR-004);
    // the whole-result read still reaches it.
    DispatchWorkflowResult? Terminal) : IRuntimeChildControlCompletion
{
    /// <summary>The <c>Output</c> contract description; it declares what is left out (ADR 0046 V6).</summary>
    public const string OutputDescription =
        "The completed child's named outputs, output name to value, unchanged. Absent when the dispatch does not wait, " +
        "and {} when the child faulted, was cancelled or failed to start. Outputs that are sensitive, require " +
        "encryption, carry a redaction mode or a retention policy, or are not stored inline are left out; each run that " +
        "leaves one out reports the dispatch.child-output-withheld warning naming them.";

    RuntimeChildControlTransport? IRuntimeChildControlCompletion.ChildControl => Terminal?.Control;
}

/// <summary>The complete private state needed to validate a waited child completion.</summary>
public sealed record DispatchWorkflowState(string ChildWorkflowExecutionId);

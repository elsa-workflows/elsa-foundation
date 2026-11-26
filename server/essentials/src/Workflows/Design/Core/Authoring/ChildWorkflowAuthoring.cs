namespace Elsa.Workflows.Design.Core.Authoring;

public sealed class ChildWorkflowCall<TResult>
{
    internal ChildWorkflowCall(ActivityCall<TResult> activityCall)
    {
        Node = activityCall.Node;
        Result = activityCall.Result;
    }

    public ActivityNodeHandle Node { get; }
    public ActivityResultSource<TResult> Result { get; }
}

public static class ChildWorkflowAuthoring
{
    public static ChildWorkflowCall<TChildResult> Invoke<TChild, TChildRequest, TChildResult>(
        this ISequenceBuilder sequence,
        string activityVersionId,
        WorkflowValue<TChildRequest> request,
        string? nodeId = null)
        where TChild : WorkflowDefinition<TChildRequest, TChildResult>
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(request);
        var call = sequence.Add<TChild, TChildResult>(
            activityVersionId,
            inputs => inputs.From("request", request),
            nodeId);
        return new ChildWorkflowCall<TChildResult>(call);
    }
}

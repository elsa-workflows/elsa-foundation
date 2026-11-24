using System.Text.Json;

namespace Kernel.Native.Preview;

/// <summary>An approval that waits in its own frame for two direct, creation-bound messages.</summary>
internal sealed class DirectApprovalWorkflow : CompositeActivity, ISignalledComposite
{
    internal const string Definition = "direct-approval";
    internal const string JobDefinition = "direct-approval-with-job";
    internal const string JobNode = "approval-job";
    internal const string JobResult = "approval processed";
    internal const string WaitEntry = "awaiting-approval:";
    public DirectApprovalWorkflow() { }

    internal static Workflow Build() => Wf.Workflow(Definition, Wf.Node<DirectApprovalWorkflow>("approve"));

    internal static Workflow BuildWithJob() => Wf.Workflow(JobDefinition, Wf.Sequence("main",
        Wf.Node<DirectApprovalWorkflow>("approve"), Wf.Node<ProcessApproval>(JobNode)));

    [Background]
    public sealed class ProcessApproval : Activity<string>
    {
        protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) => ValueTask.FromResult(Complete(JobResult));
    }

    protected override void Execute(CompositeContext context)
    {
        context.SetState(Array.Empty<string>());
        context.Record(WaitEntry + context.FrameId);
    }

    public bool CanAccept(CompositeContext context) => true;

    public void Accept(CompositeContext context, string messageId, CompositeSignal signal)
    {
        if (signal.Name != "approve" || string.IsNullOrEmpty(signal.Value))
        {
            context.Record("ignored:" + messageId);
            return;
        }

        var approvals = context.ReadState<List<string>>()!;
        approvals.Add(signal.Value);
        context.Record("approved:" + messageId);
        if (approvals.Count == 2)
            context.Complete(result: JsonSerializer.SerializeToNode(approvals));
        else
            context.SetState(approvals);
    }

    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
        throw new InvalidOperationException("The direct approval activity has no children.");
}

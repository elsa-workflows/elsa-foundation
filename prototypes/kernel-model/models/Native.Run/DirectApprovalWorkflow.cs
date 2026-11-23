using System.Text.Json;

namespace Kernel.Native.Preview;

/// <summary>An approval that waits in its own frame for two direct, creation-bound messages.</summary>
internal sealed class DirectApprovalWorkflow : CompositeActivity, ISignalledComposite
{
    internal const string Definition = "direct-approval";
    internal const string WaitEntry = "awaiting-approval:";
    public DirectApprovalWorkflow() { }

    internal static Workflow Build() => Wf.Workflow(Definition, Wf.Node<DirectApprovalWorkflow>("approve"));

    internal static Workflow BuildFlowchart() => Wf.Workflow("flowchart-approval",
        Wf.Flowchart("flow",
            [Wf.If("needs-approval", Binding.Expr("amount > 500")),
                Wf.Node<DirectApprovalWorkflow>("approve"),
                Wf.Set("automatic", "automaticPath", true),
                Wf.Set("joined", "joinCount", Binding.Expr("joinCount + 1"))],
            [new("needs-approval", "approve", "True"), new("needs-approval", "automatic", "False"),
                new("approve", "joined"), new("automatic", "joined")],
            ("needs-approval", Gateway.Decision), ("joined", Gateway.ParallelJoin)),
        ("amount", 750), ("automaticPath", false), ("joinCount", 0));

    // Sim compiles this same activity as a friend of Native. Its overrides retain internal accessibility there.
#if NATIVE_FRIEND
    protected internal override void Execute(CompositeContext context)
#else
    protected override void Execute(CompositeContext context)
#endif
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

#if NATIVE_FRIEND
    protected internal override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
#else
    protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) =>
#endif
        throw new InvalidOperationException("The direct approval activity has no children.");
}

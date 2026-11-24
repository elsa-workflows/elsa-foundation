using System.Text.Json;

namespace Kernel.Native.Preview;

/// <summary>An approval that waits in its own frame for two direct, creation-bound messages.</summary>
internal sealed class DirectApprovalWorkflow : CompositeActivity, ISignalledComposite
{
    internal const string Definition = "direct-approval";
    internal const string JobDefinition = "direct-approval-with-job";
    internal const string TimedDefinition = "direct-approval-with-timed-job";
    internal const string TimeoutDefinition = "direct-approval-with-timeout";
    internal const string TakeoverDefinition = "direct-approval-with-timed-takeover";
    internal const string ActiveJobEntry = "core-preview-timed-job-entered: ";
    internal const string TimeoutBoundary = "catch-timeout";
    internal const string JobNode = "approval-job";
    internal const string SuccessorJobNode = "approval-follow-up";
    internal const string JobResult = "approval processed";
    internal const string WaitEntry = "awaiting-approval:";
    public DirectApprovalWorkflow() { }

    internal static Workflow Build() => Wf.Workflow(Definition, Wf.Node<DirectApprovalWorkflow>("approve"));

    internal static Workflow BuildWithJob() => Wf.Workflow(JobDefinition, Wf.Sequence("main",
        Wf.Node<DirectApprovalWorkflow>("approve"), Wf.Node<ProcessApproval>(JobNode)));

    internal static Workflow BuildWithTimedJob(bool timesOut = false)
    {
        var work = new Node(JobNode, timesOut ? typeof(WaitForCancellation) : typeof(ProcessApproval))
        {
            JobTimeout = TimeSpan.FromSeconds(timesOut ? 2 : 20)
        };
        var approve = Wf.Node<DirectApprovalWorkflow>("approve");
        if (timesOut)
            return Wf.Workflow(TimeoutDefinition, Wf.Sequence("main", approve, Wf.Node<CatchTimeout>(TimeoutBoundary).With(work)));

        var followUp = new Node(SuccessorJobNode, typeof(ProcessApproval)) { JobTimeout = TimeSpan.FromSeconds(20) };
        return Wf.Workflow(TimedDefinition, Wf.Sequence("main", approve, work, followUp));
    }

    internal static Workflow BuildWithTimedTakeover()
    {
        var work = new Node(JobNode, typeof(WaitForTakeover)) { JobTimeout = TimeSpan.FromSeconds(15) };
        return Wf.Workflow(TakeoverDefinition, Wf.Sequence("main", Wf.Node<DirectApprovalWorkflow>("approve"),
            Wf.Node<CatchTimeout>(TimeoutBoundary).With(work)));
    }

    [Background]
    public sealed class WaitForTakeover : Activity<string>
    {
        protected override async ValueTask<Transition> ExecuteAsync(ActivityContext context)
        {
            Console.WriteLine(ActiveJobEntry + context.Instance.Id);
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            return Complete(JobResult);
        }
    }

    [Background]
    public sealed class WaitForCancellation : Activity<string>
    {
        protected override async ValueTask<Transition> ExecuteAsync(ActivityContext context)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            return Complete(JobResult);
        }
    }

    public sealed class CatchTimeout : CompositeActivity
    {
        protected override void Execute(CompositeContext context) => context.Schedule(context.Children.Single());
        protected override void OnChildCompleted(CompositeContext context, ChildCompleted child) => context.Complete(result: child.Result);
        protected override void OnChildFaulted(CompositeContext context, ChildFaulted child) => context.Complete(result: Values.From(child.Fault.Message));
    }

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

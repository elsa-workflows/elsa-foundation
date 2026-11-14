using Kernel.Durable;
using Kernel.Runtime;
using Kernel.Sim;
using Kernel.Workflows;

internal static partial class ShortWaitRaces
{
    private sealed class DedicatedJobs : TaskScheduler
    {
        protected override void QueueTask(Task task) =>
            Task.Factory.StartNew(() => TryExecuteTask(task), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        protected override bool TryExecuteTaskInline(Task task, bool previouslyQueued) => false;
        protected override IEnumerable<Task> GetScheduledTasks() => [];
    }

    private sealed class BlockingJob : Model<List<string>>
    {
        public override string Key => "blocking-job";
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Sibling = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Escape = new();
        public int RightResults;

        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token)
        {
            if (job.Key == "left")
            {
                Entered.SetResult();
                // The runner can release a failing control without hanging the process.
                if (WaitHandle.WaitAny([token.WaitHandle, Escape.WaitHandle]) == 0) Cancelled.SetResult();
            }
            else Sibling.SetResult();
            return ValueTask.FromResult<object?>(job.Key);
        }

        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Kinds.Start) turn.StartJob("left");
                if (message.Kind == "start-right") turn.StartJob("right");
                if (message.Kind == "cancel-left") turn.CancelJob("left");
                if (message.Kind == Kinds.JobFinished)
                {
                    var key = message.Read<JobFinished>().Key;
                    if (key == "right") RightResults++;
                    state.Add(key);
                }
            }
        }
    }

    private static async Task CancelBlockingJob()
    {
        var model = new BlockingJob();
        var store = new InMemoryStore(1, () => 100);
        await using var owner = new Worker("owner", 0, store, new WorkflowInstance([model]),
            new() { Monotonic = () => 100, JobScheduler = new DedicatedJobs() });
        await new DurableHost([owner]).ActivateAsync(0);
        await owner.PollAsync();
        var actor = InstanceRef.In(0, "blocking");
        var timeout = TimeSpan.FromSeconds(5);
        owner.Post(new("start", actor, Kinds.Start, Start: new(model.Key, "def")), fresh: true);
        await owner.RunAsync();
        await model.Entered.Task.WaitAsync(timeout);
        var left = owner.JobCompletion(actor.Id, "left")!;
        async Task ApplyCommands()
        {
            owner.Post(new("right", actor, "start-right"));
            await owner.RunAsync();
            var right = owner.JobCompletion(actor.Id, "right");
            await model.Sibling.Task;
            if (right is not null) await right;
            owner.Post(new("cancel", actor, "cancel-left"));
            owner.RunTurn(actor.Id);
            await owner.FlushAsync();
        }
        var commands = Task.Run(ApplyCommands);
        try { await Task.WhenAll(commands, model.Cancelled.Task, left).WaitAsync(timeout); }
        finally { model.Escape.Set(); await commands.WaitAsync(timeout); }
        for (var i = 0; i < 4; i++) await owner.RunAsync();
        var row = store.Instances[actor.Id];
        Need(row.Jobs is null && model.RightResults == 1 && Json.Read<List<string>>(row.State) is ["right"], "sibling result or exact cancellation lost");
        Console.WriteLine("PASS synchronous job received cancellation while its same-actor sibling ran");
    }

    private sealed class CompletingJob : Model<List<string>>
    {
        public override string Key => "completing-job";
        public Action? OnVersionRead;
        public override int StateVersion { get { OnVersionRead?.Invoke(); return 1; } }
        public readonly TaskCompletionSource<object?> Result = new();
        public override ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken token) => new(Result.Task);
        protected override void Turn(ITurn turn, List<string> state)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Kinds.Start) turn.StartJob("work");
                if (message.Kind == Kinds.JobFinished) turn.SetStatus(InstanceStatus.Finished);
            }
        }
    }

    private static async Task ReadyBeforeSleep(bool ephemeral, bool duringPass)
    {
        long now = 100;
        var model = new CompletingJob();
        var store = new InMemoryStore(1, () => now);
        var commits = 0;
        var counted = new CountingStore(store, () => commits++);
        bool finishedInMemory = false;
        await using var owner = new Worker("owner", 0, counted, new WorkflowInstance([model]), new()
        {
            Monotonic = () => now,
            OnLeftMemory = (_, status) => finishedInMemory = status == InstanceStatus.Finished
        });
        await new DurableHost([owner]).ActivateAsync(0);
        await owner.PollAsync();
        var actor = InstanceRef.In(0, "ready");
        owner.Post(new("start", actor, Kinds.Start, Start: new(model.Key, "def", Ephemeral: ephemeral,
            ShortWait: TimeSpan.FromMilliseconds(200))), fresh: true);
        for (var i = 0; i < 4; i++) await owner.RunAsync();
        var driver = owner.JobCompletion(actor.Id, "work")!;
        if (duringPass)
        {
            // On this idle actor, the two Ready scans each ask the model whether its checkpoint is readable.
            var reads = 0;
            model.OnVersionRead = () =>
            {
                if (++reads != 2) return;
                model.OnVersionRead = null;
                model.Result.SetResult("done");
                Need(driver.IsCompleted, "driver did not publish during the last ready scan");
            };
        }
        else model.Result.SetResult("done");
        var delay = await owner.RunAsync();
        Need(driver.IsCompleted && delay == TimeSpan.Zero, $"completed job advised a delay of {delay}");
        await owner.RunAsync();
        Need(ephemeral ? finishedInMemory && store.Instances.Count == 0 && store.Journal.Count == 0 :
            store.Instances[actor.Id].Status == InstanceStatus.Finished, "owed finish missed its prompt owner pass");
        Need(!ephemeral || commits == 0, $"short-wait completion issued {commits} store commits");
        Need(store.Messages.Count == 0, "job ticket remained");
        Console.WriteLine($"PASS ready job before idle advice ephemeral={ephemeral} duringPass={duringPass}");
    }

    public static async Task JobProgress()
    {
        await CancelBlockingJob();
        foreach (var ephemeral in new[] { false, true })
            foreach (var duringPass in new[] { false, true }) await ReadyBeforeSleep(ephemeral, duringPass);
    }
}

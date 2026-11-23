using System.Diagnostics;
using Kernel.Foundation.InMemory;
using Kernel.Runtime.Hosting;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class HostJobDrainChecks
{
    internal static async Task<int> RunAsync()
    {
        var timeout = TimeSpan.FromSeconds(10);
        var clock = Stopwatch.StartNew();
        var store = new InMemoryStore(1, () => 1_701_000_000_000 + clock.ElapsedMilliseconds);
        var type = new JobProbe();
        await using var node = new RuntimeNode("job-drain", store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(20)
        });
        await node.StartAsync(DurableHost.ForNode(node.Node, store, new ActorAdapter(store, type), 1,
            new WorkerOptions { Partitions = 1, Lease = timeout, Monotonic = () => clock.ElapsedMilliseconds }));
        var scope = new Contract.BoundScope("job-drain");
        var ingress = new InMemoryIngress(store, new Requests("job-drain"), node.Node);
        var receipt = await ingress.SendAsync(scope, new Contract.Target.Start(new(type.Type, "drain", History: Contract.History.Full)),
            "start", Contract.Kinds.Start);
        if (receipt is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } actor })
            throw new InvalidOperationException("The test actor must start normally.");

        await type.Entered.Task.WaitAsync(timeout);
        var before = store.ReadFoundationActor(scope.Tenant, actor.Id).Actor?.Row;
        if (before is not { Revision: 1, Status: InstanceStatus.Running } || store.Partitions[0].Owner != node.Node)
            throw new InvalidOperationException("The admitted off-turn job must have a waiting checkpoint and current fence.");

        var stopped = new HostOwnershipAdministration(node, () => true).DrainAsync().AsTask();
        try
        {
            await type.Canceled.Task.WaitAsync(timeout);
            if (await Task.WhenAny(stopped, Task.Delay(TimeSpan.FromMilliseconds(250))) == stopped ||
                store.Partitions[0].Owner != node.Node)
            {
                Console.Error.WriteLine("Graceful stop returned or released the fence while the admitted job was still cleaning up.");
                return 1;
            }
        }
        finally
        {
            type.Finish.TrySetResult();
            await stopped.WaitAsync(timeout);
        }

        if (!type.Exited.Task.IsCompleted || !await stopped || store.Partitions[0].Owner is not null ||
            store.ReadFoundationActor(scope.Tenant, actor.Id).Actor?.Row != before)
            throw new InvalidOperationException("Graceful stop must follow job cleanup and leave the waiting checkpoint available to a successor.");
        Console.WriteLine("ok graceful stop waited for entered job cleanup before release and preserved the durable checkpoint");
        return 0;
    }

    private sealed class JobProbe : Contract.IActorType
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Type => "host-drain-job-probe";
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Any(message => message.Kind == Contract.Kinds.Start))
            {
                turn.State = Json.Write("waiting");
                turn.StartJob("cleanup");
                turn.Record("issued");
            }
            return default;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => Canceled.TrySetResult());
            Entered.TrySetResult();
            try { await Finish.Task; }
            finally { Exited.TrySetResult(); }
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }
}

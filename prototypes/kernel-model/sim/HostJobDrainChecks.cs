using System.Diagnostics;
using Kernel.Foundation.InMemory;
using Kernel.Runtime.Hosting;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class HostJobDrainChecks
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Contract.BoundScope Scope = new("job-drain");

    internal static async Task<int> RunAsync()
    {
        await GracefulAsync(ownedSet: true);
        await GracefulAsync(ownedSet: false);
        await DisposedSourceAsync();
        await BeforeEntryAsync();
        await AbruptAsync(duringStop: false);
        await AbruptAsync(duringStop: true);
        return 0;
    }

    private static async Task GracefulAsync(bool ownedSet)
    {
        var fixture = new Fixture();
        var type = new JobProbe(pauseRestart: true);
        var exitsBeforeRelease = false;
        var store = new CountingStore(fixture.Store, static () => { }, beforeRelease: () => exitsBeforeRelease = type.Exited.Task.IsCompleted);
        await using var node = fixture.Node("job-drain");
        var host = fixture.Host(node, type, store, ownedSet);
        await node.StartAsync(host);
        var actor = await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var before = fixture.Checkpoint(actor);
        var ticket = fixture.JobTicket();
        var beforeTicket = TicketImage(ticket);
        var beforeJournal = fixture.Store.Journal.Values.ToArray();
        var beforeEpoch = fixture.Fence.Epoch;
        Check(before is { Revision: 1, Status: InstanceStatus.Running } && fixture.Fence.Owner == node.Node &&
            !ticket.Done && ticket.Attempts == 0, "the entered job has a durable checkpoint and an outstanding ticket");

        var command = new HostOwnershipAdministration(node, () => true);
        using var disconnected = new CancellationTokenSource();
        var caller = command.DrainAsync(disconnected.Token).AsTask();
        var stopped = command.DrainAsync().AsTask();
        try
        {
            await type.Canceled.Task.WaitAsync(Timeout);
            disconnected.Cancel();
            await ExpectCanceled(caller, disconnected.Token);
            Check(!stopped.IsCompleted && fixture.Fence.Owner == node.Node && fixture.Fence.Epoch == beforeEpoch,
                "disconnecting one caller leaves the shared stop waiting for the admitted body at the same fence");
        }
        finally
        {
            type.Finish.TrySetResult();
            await stopped.WaitAsync(Timeout);
        }

        Check(await stopped && exitsBeforeRelease && fixture.Fence.Owner is null && fixture.Fence.Epoch == beforeEpoch &&
            fixture.Checkpoint(actor) == before && TicketImage(ticket) == beforeTicket && fixture.Store.Journal.Values.SequenceEqual(beforeJournal),
            $"graceful stop waits for body exit before release and preserves the checkpoint, ticket and journal ({(ownedSet ? "owned set" : "fixed worker")})");

        // The same durable host may be used by a fresh runtime. Its first job recovers the existing ticket.
        await using var successor = fixture.Node(node.Node);
        await successor.StartAsync(host);
        try
        {
            await type.Restarted.Task.WaitAsync(Timeout);
            var repeatedStop = node.Runtime.StopAsync().AsTask();
            var completed = await Task.WhenAny(repeatedStop, type.RestartCanceled.Task).WaitAsync(Timeout);
            Check(completed == repeatedStop && !type.RestartCanceled.Task.IsCompleted && successor.Runtime.Owns(0),
                "a repeated stop on the old runtime leaves the reused host's new job and fence intact");
        }
        finally { type.FinishRestart.TrySetResult(); }
        await WaitFor(() => fixture.Checkpoint(actor) is { Revision: 2, Status: InstanceStatus.Finished });
        Check(type.Starts == 2 && !fixture.Store.Messages.ContainsKey(ticket.Message.InboxKey) && ticket.Attempts == 0 &&
            fixture.Checkpoint(actor)!.State == Json.Write("completed") &&
            fixture.Store.Journal.Values.Count(row => row.Entries.Contains("completed", StringComparison.Ordinal)) == 1,
            "a fresh runtime reusing the host completes the durable job exactly once without a death attempt");
        await successor.StopAsync();

        await node.Runtime.StartAsync(host);
        Check(await node.Runtime.ActivateAsync(0) is { Active: true } && node.Runtime.Owns(0),
            "the same partition runtime can explicitly start and take its scope again");
        await node.Runtime.StopAsync();
        Check(fixture.Fence.Owner is null, "the next start has its own orderly stop");
    }

    private static async Task DisposedSourceAsync()
    {
        var fixture = new Fixture();
        var type = new JobProbe(holdResult: true);
        using var finishPublisher = new ManualResetEventSlim();
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = fixture.Options with
        {
            AfterJobPublication = () =>
            {
                published.TrySetResult();
                if (!finishPublisher.Wait(Timeout)) throw new TimeoutException("The publisher was never released.");
            }
        };
        await using var node = fixture.Node("held-result");
        await node.StartAsync(fixture.Host(node, type, fixture.Store, ownedSet: true, options));
        try
        {
            var actor = await fixture.StartActorAsync(node);
            await type.Entered.Task.WaitAsync(Timeout);
            type.Finish.TrySetResult();
            await published.Task.WaitAsync(Timeout);
            await WaitFor(() => JobSourceDisposed(type.JobToken));
            var held = fixture.Store.Messages.Values.Single(delivery => delivery.Message.FoundationHeldJobResult);
            var checkpoint = fixture.Checkpoint(actor);
            Check(held.Held && checkpoint is { Revision: 2, Status: InstanceStatus.Running },
                "the owner has committed and disposed the held result while its publisher is paused");

            var stopped = node.StopAsync().AsTask();
            try
            {
                Check(await Task.WhenAny(stopped, Task.Delay(250)) != stopped && node.Runtime.Owns(0),
                    "graceful stop waits for the paused publisher with a disposed cancellation source");
            }
            finally { finishPublisher.Set(); }
            await stopped.WaitAsync(Timeout);
            Check(fixture.Fence.Owner is null && fixture.Checkpoint(actor) == checkpoint &&
                fixture.Store.Messages.GetValueOrDefault(held.Message.InboxKey) == held,
                "publisher exit lets graceful stop release and preserve the committed held result");
        }
        finally
        {
            type.Finish.TrySetResult();
            finishPublisher.Set();
        }

        static bool JobSourceDisposed(CancellationToken token)
        {
            try { _ = token.WaitHandle; return false; }
            catch (ObjectDisposedException) { return true; }
        }
    }

    private static async Task BeforeEntryAsync()
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        using var resumeGuard = new ManualResetEventSlim();
        var reachedGuard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var node = fixture.Node("queued-job");
        var schedulerThread = 0;
        var options = fixture.Options with
        {
            BeforeScheduledJobCheck = () => Volatile.Write(ref schedulerThread, Environment.CurrentManagedThreadId),
            Monotonic = () =>
            {
                if (Environment.CurrentManagedThreadId == Volatile.Read(ref schedulerThread))
                {
                    reachedGuard.TrySetResult();
                    if (!resumeGuard.Wait(Timeout)) throw new TimeoutException("The scheduler guard was never released.");
                }
                return fixture.Options.Monotonic();
            }
        };
        var host = fixture.Host(node, type, fixture.Store, ownedSet: true, options);
        await node.StartAsync(host);
        await fixture.StartActorAsync(node);
        Task? callback = null;
        try
        {
            await reachedGuard.Task.WaitAsync(Timeout);
            var ticket = fixture.JobTicket().Message;
            await WaitFor(() => (callback = host.NodeWorker!.JobCompletion(ticket.Target.Id, "cleanup", ticket.TargetCreation ?? 0)) is not null);
            await node.StopAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Check(!resumeGuard.IsSet && !type.Entered.Task.IsCompleted && fixture.Fence.Owner is null,
                "graceful stop releases while a scheduler callback is paused before admission");
        }
        finally { resumeGuard.Set(); }
        await callback!.WaitAsync(Timeout);
        Check(type.Starts == 0, "the resumed scheduler callback cannot enter after release");
    }

    private static async Task AbruptAsync(bool duringStop)
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        await using var node = fixture.Node("abrupt-job");
        await node.StartAsync(fixture.Host(node, type, fixture.Store, ownedSet: true));
        var actor = await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var before = fixture.Checkpoint(actor);
        var ticket = fixture.JobTicket();
        using var limit = new CancellationTokenSource();
        try
        {
            if (duringStop)
            {
                var stop = node.StopAsync(limit.Token).AsTask();
                await type.Canceled.Task.WaitAsync(Timeout);
                limit.Cancel();
                await stop.WaitAsync(Timeout);
            }
            else await node.KillAsync().AsTask().WaitAsync(Timeout);
            Check(!type.Exited.Task.IsCompleted && type.Canceled.Task.IsCompleted == duringStop && fixture.Fence.Owner == node.Node &&
                fixture.Checkpoint(actor) == before && !ticket.Done,
                duringStop ? "a stop deadline abandons cleanup and leaves the fence for expiry" :
                    "abrupt kill leaves the entered body running and leaves the fence for expiry");
        }
        finally
        {
            type.Finish.TrySetResult();
            await type.Exited.Task.WaitAsync(Timeout);
        }
    }

    private static (Message Message, long Seq, long Seen, long Trial, int Attempts, bool Done) TicketImage(InMemoryStore.MessageRow ticket) =>
        (ticket.Message, ticket.Seq, ticket.Seen, ticket.Trial, ticket.Attempts, ticket.Done);

    private static async Task WaitFor(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < Timeout) await Task.Delay(10);
        if (!condition()) throw new TimeoutException("The expected job state did not arrive.");
    }

    private static async Task ExpectCanceled(Task call, CancellationToken token)
    {
        try { await call.WaitAsync(Timeout); }
        catch (OperationCanceledException error) when (error.CancellationToken == token) { return; }
        throw new InvalidOperationException("The disconnected caller must observe cancellation.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class Fixture
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        internal InMemoryStore Store { get; }
        internal WorkerOptions Options { get; }
        internal InMemoryStore.PartitionRow Fence => Store.Partitions[0];

        internal Fixture()
        {
            Store = new(1, () => 1_701_000_000_000 + _clock.ElapsedMilliseconds);
            Options = new() { Partitions = 1, Lease = Timeout, Monotonic = () => _clock.ElapsedMilliseconds,
                JobScheduler = TaskScheduler.Default };
        }

        internal RuntimeNode Node(string name) => new(name, Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(20)
        });

        internal DurableHost Host(RuntimeNode node, JobProbe model, IKernelStore store, bool ownedSet, WorkerOptions? options = null)
        {
            var adapter = new ActorAdapter(Store, model);
            return ownedSet ? DurableHost.ForNode(node.Node, store, adapter, 1, options ?? Options) :
                new DurableHost([new Worker(node.Node, 0, store, adapter, options ?? Options)]);
        }

        internal async Task<Contract.ActorRef> StartActorAsync(RuntimeNode node)
        {
            var ingress = new InMemoryIngress(Store, new Requests("job-drain"), node.Node);
            var receipt = await ingress.SendAsync(Scope, new Contract.Target.Start(new(JobProbe.TypeKey, "drain", History: Contract.History.Full)),
                "start", Contract.Kinds.Start);
            if (receipt is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } actor })
                throw new InvalidOperationException("The test actor must start normally.");
            return actor;
        }

        internal InstanceRow? Checkpoint(Contract.ActorRef actor) => Store.ReadFoundationActor(Scope.Tenant, actor.Id).Actor?.Row;
        internal InMemoryStore.MessageRow JobTicket() => Store.Messages.Values.Single(row => row.Message.Kind == Kinds.Job);
    }

    private sealed class JobProbe(bool holdResult = false, bool pauseRestart = false) : Contract.IActorType
    {
        internal const string TypeKey = "host-drain-job-probe";
        private int _starts;
        internal int Starts => Volatile.Read(ref _starts);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Restarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource FinishRestart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource RestartCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken JobToken { get; private set; }

        public string Type => TypeKey;
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
            foreach (var message in turn.Inbox.Where(message => message.Kind == Contract.Kinds.JobFinished))
            {
                if (holdResult)
                {
                    turn.Hold(message);
                    turn.State = Json.Write("held");
                    turn.Record("held");
                    continue;
                }
                var result = Json.Read<Contract.JobFinished>(message.Payload!);
                if (result.Result != Json.Write("completed")) throw new InvalidOperationException("The successor must complete the job.");
                turn.State = result.Result;
                turn.Record("completed");
                turn.End(Contract.ActorStatus.Finished);
            }
            return default;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _starts) > 1)
            {
                if (pauseRestart)
                {
                    using var restarted = cancellationToken.Register(() => RestartCanceled.TrySetResult());
                    Restarted.TrySetResult();
                    await FinishRestart.Task;
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return "completed";
            }
            JobToken = cancellationToken;
            using var registration = cancellationToken.Register(() => Canceled.TrySetResult());
            Entered.TrySetResult();
            try { await Finish.Task; }
            finally { Exited.TrySetResult(); }
            cancellationToken.ThrowIfCancellationRequested();
            return "completed";
        }
    }
}

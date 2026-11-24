using System.Diagnostics;
using Kernel.Foundation.InMemory;
using Kernel.Runtime;
using Kernel.Runtime.Hosting;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Sim;

internal static class HostJobDrainChecks
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly Contract.BoundScope Scope = ContractScopes.Bind("job-drain");

    internal static async Task<int> RunAsync()
    {
        await GracefulAsync(ownedSet: true);
        await GracefulAsync(ownedSet: false);
        await ActiveHostAsync();
        await ConcurrentStopAsync();
        await CanceledStopAsync();
        await CanceledReleaseAsync();
        await StaleHostValidationAsync();
        await FaultedStopAsync();
        await DisposedSourceAsync();
        await BeforeEntryAsync();
        await AbruptAsync(duringStop: false);
        await AbruptAsync(duringStop: true);
        await LongDrainRenewsAsync(deadline: false);
        await LongDrainRenewsAsync(deadline: true);
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

        var contender = new PartitionRuntime(node.Node, store, 1, Timeout);
        try
        {
            await contender.StartAsync(host);
            throw new InvalidOperationException("Two runtimes cannot attach the same active host.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("already attached", StringComparison.Ordinal)) { }
        Check(!type.Canceled.Task.IsCompleted && fixture.Fence.Owner == node.Node,
            "another runtime cannot attach the live durable host");

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

    private static async Task ActiveHostAsync()
    {
        var fixture = new Fixture();
        var runtime = new PartitionRuntime("active-host", fixture.Store, 1, Timeout);
        IActorRuntime lifecycle = runtime;
        var type = new JobProbe();
        var worker = new Worker(runtime.Node, 0, fixture.Store, new ActorAdapter(fixture.Store, type), fixture.Options);
        var other = new Worker(runtime.Node, 0, fixture.Store, new ActorAdapter(fixture.Store, new JobProbe()), fixture.Options);
        var host = new DurableHost([worker]);
        var replacement = new DurableHost([other]);
        await lifecycle.StartAsync(host);
        var active = (await runtime.ActivateAsync(0))!;
        await fixture.StartActorAsync(runtime.Node);

        try
        {
            for (var pass = 0; pass < 16 && !type.Entered.Task.IsCompleted; pass++) await runtime.PassAsync(0);
            await type.Entered.Task.WaitAsync(Timeout);
            Check(!await StartSucceededAsync(lifecycle.StartAsync(replacement).AsTask()) &&
                !await StartSucceededAsync(lifecycle.StartAsync(host).AsTask()),
                "the running runtime refuses both a different host and a duplicate start of its own host");

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await lifecycle.StopAsync(canceled.Token);
            Check(!await StartSucceededAsync(lifecycle.StartAsync(replacement).AsTask()) &&
                runtime.Activation(0) == active && fixture.Fence.Owner == runtime.Node && !type.Canceled.Task.IsCompleted,
                "a pre-canceled direct stop leaves the original run active and still refuses replacement");

            var stop = lifecycle.StopAsync().AsTask();
            await type.Canceled.Task.WaitAsync(Timeout);
            Check(!stop.IsCompleted && fixture.Fence.Owner == runtime.Node,
                "the later orderly stop drains the original host's admitted job");
            type.Finish.TrySetResult();
            await stop.WaitAsync(Timeout);
            Check(type.Exited.Task.IsCompleted && fixture.Fence.Owner is null,
                "the original worker exits before its fence is released");
        }
        finally
        {
            type.Finish.TrySetResult();
            if (type.Entered.Task.IsCompleted) await type.Exited.Task.WaitAsync(Timeout);
        }
    }

    private static async Task<bool> StartSucceededAsync(Task start)
    {
        try
        {
            await start.WaitAsync(Timeout);
            return true;
        }
        catch (InvalidOperationException error) when (error.Message.Contains("already running", StringComparison.Ordinal))
        {
            return false;
        }
    }

    private static async Task ConcurrentStopAsync()
    {
        var fixture = new Fixture();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releases = 0;
        var store = new CountingStore(fixture.Store, static () => { }, afterRelease: async () =>
        {
            if (Interlocked.Increment(ref releases) != 1) return;
            released.TrySetResult();
            await finishRelease.Task.WaitAsync(Timeout);
        });
        var runtime = new PartitionRuntime("overlapping-stops", store, 1, Timeout);
        IActorRuntime lifecycle = runtime;
        var worker = new Worker(runtime.Node, 0, store, new ActorAdapter(fixture.Store, new JobProbe()), fixture.Options);
        var other = new Worker(runtime.Node, 0, store, new ActorAdapter(fixture.Store, new JobProbe()), fixture.Options);
        var host = new DurableHost([worker]);
        var replacement = new DurableHost([other]);
        await lifecycle.StartAsync(host);
        Check(await runtime.ActivateAsync(0) is { Active: true }, "the direct runtime starts with a real fence");
        var oldEpoch = fixture.Fence.Epoch;

        var first = lifecycle.StopAsync().AsTask();
        Task second;
        Task firstRestart;
        Task secondRestart;
        try
        {
            await released.Task.WaitAsync(Timeout);
            Check(fixture.Fence.Owner is null && !first.IsCompleted, "the store released the old fence while the first stop still awaits its answer");
            second = lifecycle.StopAsync().AsTask();
            Check(!second.IsCompleted, "a concurrent stop waits for the same pending release");

            using var disconnected = new CancellationTokenSource();
            var caller = lifecycle.StopAsync(disconnected.Token).AsTask();
            disconnected.Cancel();
            await caller.WaitAsync(Timeout);
            Check(!first.IsCompleted && !second.IsCompleted, "canceling a joined stop wait leaves the shared release pending");

            firstRestart = lifecycle.StartAsync(host).AsTask();
            secondRestart = lifecycle.StartAsync(replacement).AsTask();
            Check(!firstRestart.IsCompleted && !secondRestart.IsCompleted, "both concurrent starts wait until the old shutdown completes");
        }
        finally { finishRelease.TrySetResult(); }

        await Task.WhenAll(first, second).WaitAsync(Timeout);
        var starts = await Task.WhenAll(StartSucceededAsync(firstRestart), StartSucceededAsync(secondRestart));
        Check(starts[0] != starts[1], "exactly one queued start binds its host after the common shutdown");
        var winner = starts[0] ? worker : other;
        Check(releases == 1 && await runtime.ActivateAsync(0) == winner && runtime.Owns(0) &&
            fixture.Fence.Owner == runtime.Node && fixture.Fence.Epoch > oldEpoch,
            "one release completes both stops and the winning host alone owns the fresh fence");
        await lifecycle.StopAsync().AsTask().WaitAsync(Timeout);
        Check(releases == 2 && fixture.Fence.Owner is null, "the restarted runtime can stop and release its own new fence");
    }

    private static async Task CanceledStopAsync()
    {
        var fixture = new Fixture();
        var runtime = new PartitionRuntime("abandoned-stop", fixture.Store, 1, Timeout);
        IActorRuntime lifecycle = runtime;
        var type = new JobProbe();
        var host = new DurableHost([new Worker(runtime.Node, 0, fixture.Store, new ActorAdapter(fixture.Store, type), fixture.Options)]);
        var replacement = new DurableHost([new Worker(runtime.Node, 0, fixture.Store, new ActorAdapter(fixture.Store, new JobProbe()), fixture.Options)]);
        await lifecycle.StartAsync(host);
        var active = (await runtime.ActivateAsync(0))!;
        var actor = await fixture.StartActorAsync(runtime.Node);
        try
        {
            for (var pass = 0; pass < 16 && !type.Entered.Task.IsCompleted; pass++) await runtime.PassAsync(0);
            await type.Entered.Task.WaitAsync(Timeout);
            var checkpoint = fixture.Checkpoint(actor);
            using var deadline = new CancellationTokenSource();
            var stop = lifecycle.StopAsync(deadline.Token).AsTask();
            await type.Canceled.Task.WaitAsync(Timeout);

            var restart = lifecycle.StartAsync(replacement).AsTask();
            Check(!restart.IsCompleted, "a replacement host cannot attach while the direct stop is draining the old job");
            deadline.Cancel();
            await stop.WaitAsync(Timeout);
            await RejectAbandonedRestartAsync(restart);
            Check(!runtime.Owns(0) && runtime.Activation(0) is null && active.Active && fixture.Fence.Owner == runtime.Node &&
                fixture.Checkpoint(actor) == checkpoint && !type.Exited.Task.IsCompleted,
                "an abandoned stop detaches its old activations and leaves the physical fence for expiry");
            await RejectAbandonedRestartAsync(lifecycle.StartAsync(replacement).AsTask());
        }
        finally
        {
            type.Finish.TrySetResult();
            if (type.Entered.Task.IsCompleted) await type.Exited.Task.WaitAsync(Timeout);
        }
    }

    private static async Task CanceledReleaseAsync()
    {
        const int partitions = 2;
        var fixture = new Fixture(partitions);
        var issued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releases = 0;
        var store = new CountingStore(fixture.Store, static () => { }, afterRelease: async () =>
        {
            if (Interlocked.Increment(ref releases) != 1) return;
            issued.TrySetResult();
            await finishRelease.Task.WaitAsync(Timeout);
            answered.TrySetResult();
        });
        var runtime = new PartitionRuntime("release-deadline", store, partitions, Timeout);
        var successor = new PartitionRuntime("after-release-deadline", store, partitions, Timeout);
        DurableHost Host(string node) => new(Enumerable.Range(0, partitions).Select(partition =>
            new Worker(node, partition, store, new ActorAdapter(fixture.Store, new JobProbe()), fixture.Options)));

        var host = Host(runtime.Node);
        await runtime.StartAsync(host);
        Check(await runtime.ActivateAsync(0) is { Active: true } && await runtime.ActivateAsync(1) is { Active: true },
            "the release deadline fixture owns two real fences");
        var oldEpoch = fixture.Fence.Epoch;
        using var deadline = new CancellationTokenSource();
        var stop = runtime.StopAsync(deadline.Token).AsTask();
        try
        {
            await issued.Task.WaitAsync(Timeout);
            var queuedStart = runtime.StartAsync(Host(runtime.Node)).AsTask();
            Check(!queuedStart.IsCompleted, "a second start is waiting for the first issued fence release");
            deadline.Cancel();
            Check(await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(2))) == stop && !answered.Task.IsCompleted,
                "the initiating deadline ends stop while the issued disposal is still pending");
            await stop;
            await RejectAbandonedRestartAsync(queuedStart);
            await RejectAbandonedRestartAsync(successor.StartAsync(host).AsTask());
            Check(releases == 1 && runtime.Activation(0) is null && runtime.Activation(1) is null &&
                fixture.Fence.Owner is null && fixture.Store.Partitions[1].Owner == runtime.Node,
                "abandoning the first release detaches the old run and leaves the second fence for expiry");

            await successor.StartAsync(Host(successor.Node));
            Check(await successor.ActivateAsync(0) is { Active: true } && fixture.Fence.Epoch > oldEpoch,
                "an independent runtime can take the fence already released by the old run");
        }
        finally { finishRelease.TrySetResult(); }

        await answered.Task.WaitAsync(Timeout);
        Check(successor.Owns(0) && fixture.Fence.Owner == successor.Node && fixture.Store.Partitions[1].Owner == runtime.Node,
            "the late release answer leaves the independent owner's fence and old unreleased fence intact");
        await successor.StopAsync().AsTask().WaitAsync(Timeout);
    }

    private static async Task StaleHostValidationAsync()
    {
        const int partitions = 2;
        var fixture = new Fixture(partitions);
        var checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = 0;
        var releases = 0;
        var store = new CountingStore(fixture.Store, static () => { }, afterRelease: async () =>
        {
            if (Interlocked.Increment(ref releases) != 1) return;
            released.TrySetResult();
            await finishRelease.Task.WaitAsync(Timeout);
            answered.TrySetResult();
        }, beforePartitionCount: async () =>
        {
            if (Interlocked.Increment(ref checks) != 2) return;
            checking.TrySetResult();
            await finishCheck.Task.WaitAsync(Timeout);
        });
        var owner = new PartitionRuntime("checked-host", store, partitions, Timeout);
        var contender = new PartitionRuntime(owner.Node, store, partitions, Timeout);
        var later = new PartitionRuntime(owner.Node, store, partitions, Timeout);
        var host = DurableHost.ForNode(owner.Node, store, new ActorAdapter(fixture.Store, new JobProbe()), partitions, fixture.Options);
        await owner.StartAsync(host);
        Check(await owner.ActivateAsync(0) is { Active: true } && await owner.ActivateAsync(1) is { Active: true },
            "the counted durable host owns two real fences before a competing start");
        var oldEpoch = fixture.Store.Partitions[1].Epoch;

        try
        {
            var staleStart = contender.StartAsync(host).AsTask();
            await checking.Task.WaitAsync(Timeout);
            Check(!staleStart.IsCompleted, "the contender passed host preflight while the original runtime was active");
            using var deadline = new CancellationTokenSource();
            var shuttingDown = owner.StopAsync(deadline.Token).AsTask();
            await released.Task.WaitAsync(Timeout);
            deadline.Cancel();
            await shuttingDown.WaitAsync(Timeout);

            finishCheck.TrySetResult();
            await RejectAbandonedRestartAsync(staleStart);
            await contender.StopAsync();
            await RejectAbandonedRestartAsync(later.StartAsync(host).AsTask());
            // The counted host releases its owned set in one store call, so both fences go in the one issued release.
            Check(fixture.Fence.Owner is null && releases == 1, "one physical set release was issued before the deadline");
        }
        finally
        {
            finishCheck.TrySetResult();
            finishRelease.TrySetResult();
        }

        await answered.Task.WaitAsync(Timeout);
        Check(await host.ActivateAsync(1) is null && releases == 1 && fixture.Store.Partitions[1].Owner is null &&
            fixture.Store.Partitions[1].Epoch == oldEpoch,
            "an unbound contender cannot reopen the abandoned host or adopt the old fence after the late set release answer");
    }

    private static async Task FaultedStopAsync()
    {
        var fixture = new Fixture();
        var runtime = new PartitionRuntime("failed-stop", fixture.Store, 1, Timeout);
        IActorRuntime lifecycle = runtime;
        var worker = new Worker(runtime.Node, 0, fixture.Store, new ActorAdapter(fixture.Store, new JobProbe()), fixture.Options);
        var durable = new DurableHost([worker]);
        var failing = new FaultedStopHost(durable);
        await lifecycle.StartAsync(failing);
        var active = (await runtime.ActivateAsync(0))!;
        var first = lifecycle.StopAsync().AsTask();
        await failing.Entered.Task.WaitAsync(Timeout);
        var second = lifecycle.StopAsync().AsTask();
        var restart = lifecycle.StartAsync(durable).AsTask();
        failing.Release.TrySetResult();

        foreach (var observer in new[] { first, second, restart })
        {
            try
            {
                await observer.WaitAsync(Timeout);
                throw new InvalidOperationException("The observer must see the host's shutdown fault.");
            }
            catch (InvalidOperationException error) when (ReferenceEquals(error, failing.Error)) { }
        }
        Check(runtime.Activation(0) is null && active.Active && fixture.Fence.Owner == runtime.Node,
            "both stop callers and the queued start see the original fault, and the runtime detaches its old activation");
        await RejectAbandonedRestartAsync(lifecycle.StartAsync(durable).AsTask());
    }

    private static async Task RejectAbandonedRestartAsync(Task restart)
    {
        try { await restart.WaitAsync(Timeout); }
        catch (InvalidOperationException error) when (error.Message.Contains("previous shutdown", StringComparison.Ordinal))
        {
            Console.WriteLine("ok the runtime cannot be restarted after an incomplete shutdown");
            return;
        }
        throw new InvalidOperationException("An incomplete shutdown must prevent this runtime from starting again.");
    }

    private sealed class FaultedStopHost(IScopeHost inner) : IScopeHost, IScopeHostRun
    {
        internal InvalidOperationException Error { get; } = new("The test host could not finish shutdown.");
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default) =>
            inner.ActivateAsync(scope, cancellationToken);

        public IScopeHostRun AttachRuntime() => this;
        public void Stopped() => throw new InvalidOperationException("A faulted shutdown cannot complete.");

        public async ValueTask PrepareToStopAsync(CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            throw Error;
        }
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

    // The drain of an entered job can outlast the node lease. The test clock moves past one lease plus a margin while the
    // job runs: a graceful stop keeps renewing the node lease, so a sibling cannot take over, and releases after the
    // job exits at the same epoch. Once the stop deadline passes nothing renews, and the fence runs out by its lease.
    private static async Task LongDrainRenewsAsync(bool deadline)
    {
        var fixture = new Fixture(manualClock: true);
        var type = new JobProbe();
        var lease = fixture.Options.Lease;
        var step = TimeSpan.FromSeconds(1);
        var margin = TimeSpan.FromSeconds(2);
        long? releasedEpoch = null;
        var exitedBeforeRelease = false;
        var store = new CountingStore(fixture.Store, static () => { }, beforeRelease: () =>
        {
            exitedBeforeRelease = type.Exited.Task.IsCompleted;
            releasedEpoch ??= fixture.Fence.Epoch;
        });
        await using var node = fixture.Node("long-drain");
        await node.StartAsync(fixture.Host(node, type, store, ownedSet: true));
        var actor = await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var before = fixture.Checkpoint(actor);
        var epoch = fixture.Fence.Epoch;
        using var limit = new CancellationTokenSource();
        var stop = node.StopAsync(limit.Token).AsTask();
        try
        {
            await type.Canceled.Task.WaitAsync(Timeout);
            async Task<bool> AdvanceAsync(TimeSpan span, bool renews)
            {
                var every = true;
                for (var moved = TimeSpan.Zero; moved < span; moved += step)
                {
                    var renewed = node.Heartbeats.Renewed;
                    fixture.Advance(step);
                    if (renews) every &= await RenewedAsync(node, renewed);
                }
                return every;
            }
            if (!deadline)
            {
                var renewedThroughDrain = await AdvanceAsync(lease + margin, renews: true);
                var sibling = await fixture.Store.TakeAsync(0, "sibling", lease);
                Console.WriteLine($"  long drain: renewed every step {renewedThroughDrain}, sibling took epoch {sibling?.Epoch.ToString() ?? "none"}, " +
                    $"owner {fixture.Fence.Owner ?? "none"} at epoch {fixture.Fence.Epoch} (entered at {epoch}), renewals {node.Heartbeats.Renewed}");
                Check(renewedThroughDrain && sibling is null && fixture.Fence.Owner == node.Node && fixture.Fence.Epoch == epoch &&
                    !type.Exited.Task.IsCompleted && !stop.IsCompleted,
                    "a graceful stop renews the node lease while an entered job drains past one lease, and no sibling can take over");
                type.Finish.TrySetResult();
                await stop.WaitAsync(Timeout);
                Check(exitedBeforeRelease && releasedEpoch == epoch && fixture.Fence.Owner is null && fixture.Fence.Epoch == epoch &&
                    fixture.Checkpoint(actor) == before,
                    "the long-drained job exits before release, and the release happens at the original epoch");
            }
            else
            {
                var renewedBeforeDeadline = await AdvanceAsync(TimeSpan.FromSeconds(3), renews: true);
                limit.Cancel();
                await stop.WaitAsync(Timeout);
                var beats = node.Heartbeats.Renewed;
                await AdvanceAsync(lease + margin, renews: false);
                await Task.Delay(TimeSpan.FromMilliseconds(300));
                var sibling = await fixture.Store.TakeAsync(0, "sibling", lease);
                Check(renewedBeforeDeadline && node.Heartbeats.Renewed == beats && sibling is { } taken && taken.Epoch == epoch + 1 &&
                    releasedEpoch is null && !type.Exited.Task.IsCompleted,
                    "after the stop deadline nothing renews, and the fence runs out by its lease to a sibling");
            }
        }
        finally
        {
            type.Finish.TrySetResult();
            await type.Exited.Task.WaitAsync(Timeout);
        }
    }

    private static async Task<bool> RenewedAsync(RuntimeNode node, long renewed)
    {
        var elapsed = Stopwatch.StartNew();
        while (node.Heartbeats.Renewed <= renewed && elapsed.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(5);
        return node.Heartbeats.Renewed > renewed;
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
        private readonly bool _manual;
        private long _manualNow;
        internal InMemoryStore Store { get; }
        internal WorkerOptions Options { get; }
        internal InMemoryStore.PartitionRow Fence => Store.Partitions[0];

        internal Fixture(int partitions = 1, bool manualClock = false)
        {
            _manual = manualClock;
            Store = new(partitions, () => 1_701_000_000_000 + Elapsed);
            Options = new() { Partitions = partitions, Lease = Timeout, Monotonic = () => Elapsed,
                JobScheduler = TaskScheduler.Default };
        }

        // The test clock: the store's and the Workers' time, moved only by Advance when manual.
        private long Elapsed => _manual ? Interlocked.Read(ref _manualNow) : _clock.ElapsedMilliseconds;
        internal void Advance(TimeSpan span) => Interlocked.Add(ref _manualNow, (long)span.TotalMilliseconds);

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

        internal Task<Contract.ActorRef> StartActorAsync(RuntimeNode node) => StartActorAsync(node.Node);

        internal async Task<Contract.ActorRef> StartActorAsync(string node)
        {
            var ingress = new InMemoryIngress(Store, new Requests("job-drain"), node);
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

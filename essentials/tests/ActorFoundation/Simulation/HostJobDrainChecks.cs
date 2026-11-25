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
        (string Name, Func<Task> Run)[] cases =
        [
            ("graceful-set", () => GracefulAsync(ownedSet: true)),
            ("graceful-fixed", () => GracefulAsync(ownedSet: false)),
            ("active-host", ActiveHostAsync),
            ("concurrent-stop", ConcurrentStopAsync),
            ("canceled-stop", CanceledStopAsync),
            ("canceled-release", CanceledReleaseAsync),
            ("stale-host", StaleHostValidationAsync),
            ("faulted-stop", FaultedStopAsync),
            ("disposed-source", DisposedSourceAsync),
            ("before-entry", BeforeEntryAsync),
            ("abrupt-kill", () => AbruptAsync(duringStop: false)),
            ("abrupt-deadline", () => AbruptAsync(duringStop: true)),
            ("long-drain-set", () => LongDrainRenewsAsync(deadline: false)),
            ("long-drain-deadline", () => LongDrainRenewsAsync(deadline: true)),
            ("long-drain-fixed", () => LongDrainRenewsAsync(deadline: false, ownedSet: false)),
            ("stand-down-mid-drain", StandDownMidDrainAsync),
            ("administration-deadline", AdministrationDeadlineAsync),
            ("stop-after-abandoned", StopAfterAbandonedAsync),
            ("stop-during-start", StopDuringStartAsync),
            ("pass-in-flight", PassInFlightAsync),
            ("paused-entry-while-draining", PausedEntryWhileDrainingAsync),
            ("long-drain-workers", LongDrainWorkersAsync),
            ("fault-masked-by-loss", FaultMaskedByLossAsync),
            ("stop-during-set-gain", StopDuringSetGainAsync),
            ("dispose-deadline", DisposeDeadlineAsync),
            ("transient-answer-error", () => TransientAnswerErrorAsync(Failure.Transient)),
            ("queued-start-after-loss", QueuedStartAfterLossAsync),
            ("joined-administration", JoinedAdministrationAsync),
            ("dispose-joins-stop", DisposeJoinsStopAsync),
            ("turn-drain-fault", TurnDrainFaultAsync),
            ("drain-outcomes", DrainOutcomesAsync),
            ("pending-worker", PendingWorkerAsync),
            ("answer-fault", () => AnswerFaultAsync(new InvalidOperationException("injected answer fault"))),
            ("answer-definite-store-error", () => AnswerFaultAsync(new StoreException(Failure.Definite, "injected definite store failure"))),
            ("turn-drain-pending", TurnDrainPendingAsync),
            ("dispose-infinite-deadline", DisposeInfiniteDeadlineAsync),
            ("dispose-bound-expiry", DisposeBoundExpiryAsync),
            ("concurrent-stop-dispose", ConcurrentStopDisposeAsync),
            ("unknown-answer-error", () => TransientAnswerErrorAsync(Failure.Unknown)),
            ("abandon-waits-turn-drains", AbandonWaitsTurnDrainsAsync),
            ("faulted-drain-pass", FaultedDrainPassAsync),
            ("negative-dispose-deadline", NegativeDisposeDeadlineAsync),
            ("abandon-fault-after-kill", AbandonFaultAfterKillAsync),
            ("bound-expiry-fault", BoundExpiryFaultAsync),
            ("loss-in-both-drains", LossInBothDrainsAsync),
            ("answer-put-back", () => AnswerPutBackAsync(newerAfterConsumption: false)),
            ("dispose-max-deadline", DisposeMaxDeadlineAsync),
            ("canceled-halt", CanceledHaltAsync),
            ("kill-cancel-not-recorded", KillCancelNotRecordedAsync),
            ("own-cancel-then-kill", OwnCancelThenKillAsync),
            ("loop-errors-logged", LoopErrorsLoggedAsync),
            ("answer-put-back-newest", () => AnswerPutBackAsync(newerAfterConsumption: true)),
            ("dispose-deadline-boundary", DisposeDeadlineBoundaryAsync),
        ];
        // HOST_JOB_DRAIN_ONLY=name[,name]: run only these cases (fail-before witnesses run them one at a time).
        var only = Environment.GetEnvironmentVariable("HOST_JOB_DRAIN_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (only is { Length: 0 } || only?.FirstOrDefault(name => !cases.Any(known => known.Name == name)) is { } unknown)
            throw new InvalidOperationException($"HOST_JOB_DRAIN_ONLY names no known case: {Environment.GetEnvironmentVariable("HOST_JOB_DRAIN_ONLY")}");
        foreach (var (name, run) in cases)
            if (only is null || only.Contains(name, StringComparer.Ordinal))
                await run().WaitAsync(TimeSpan.FromMinutes(2));     // a hung case fails instead of holding the run
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
    private static async Task LongDrainRenewsAsync(bool deadline, bool ownedSet = true)
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
        await node.StartAsync(fixture.Host(node, type, store, ownedSet));
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
                    $"a graceful stop renews the node lease while an entered job drains past one lease, and no sibling can take over ({(ownedSet ? "node" : "per-partition")} host)");
                type.Finish.TrySetResult();
                await stop.WaitAsync(Timeout);
                Check(exitedBeforeRelease && releasedEpoch == epoch && fixture.Fence.Owner is null && fixture.Fence.Epoch == epoch &&
                    fixture.Checkpoint(actor) == before,
                    $"the long-drained job exits before release, and the release happens at the original epoch ({(ownedSet ? "node" : "per-partition")} host)");
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

    // A heartbeat answer stands down the partition of the entered job while the node drains it: the Worker resets, the
    // drain records the loss and still waits for the job, then the stop releases what the node still holds, frees the host
    // for a new run and reports the loss.
    private static async Task StandDownMidDrainAsync()
    {
        const int partitions = 2;
        var fixture = new Fixture(partitions);
        var type = new JobProbe();
        var jobPartition = -1;     // read from the commit that issued the job: the store's rows are not safe to scan while the node runs
        var store = new CountingStore(fixture.Store, static () => { }, observeCommit: (batch, landed) =>
        {
            if (!landed) return;
            foreach (var turn in batch.Turns)
                if (turn.Sent.Any(message => message.Kind == Kinds.Job)) Volatile.Write(ref jobPartition, batch.PartitionOf(turn));
        });
        var node = fixture.Node("stand-down", partitions);
        var host = fixture.Host(node, type, store, ownedSet: true);
        await node.StartAsync(host);
        await WaitFor(() => fixture.Store.Partitions.All(row => row.Owner == node.Node));
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var lost = Volatile.Read(ref jobPartition);
        var kept = 1 - lost;
        var stop = node.StopAsync().AsTask();
        Exception? reported = null;
        List<string> failed = [];     // every assertion is reported, so a fail-before shows each one
        try
        {
            await type.Canceled.Task.WaitAsync(Timeout);
            var row = fixture.Store.Partitions[lost];
            (row.Owner, row.Epoch) = ("sibling", row.Epoch + 1);     // the sibling took it: the next answer omits it
            var renewed = node.Heartbeats.Renewed;
            await RenewedAsync(node, renewed);
            await Task.Delay(200);
            Soft(failed, !stop.IsCompleted && !type.Exited.Task.IsCompleted && fixture.Store.Partitions[kept].Owner == node.Node,
                "the stood-down drain keeps waiting for the entered job, and the node still holds the other partition");
        }
        finally { type.Finish.TrySetResult(); }
        try { await stop.WaitAsync(Timeout); }
        catch (Exception error) when (error is not TimeoutException) { reported = error; }
        var reattached = new PartitionRuntime(node.Node, store, partitions, Timeout);
        var attached = true;
        try { await reattached.StartAsync(host); }
        catch (InvalidOperationException) { attached = false; }
        Console.WriteLine($"  stand-down: reported {reported?.GetType().Name ?? "nothing"}; kept owner {fixture.Store.Partitions[kept].Owner ?? "none"}, " +
            $"lost owner {fixture.Store.Partitions[lost].Owner ?? "none"}, reattached {attached}");
        Soft(failed, reported is ScopeOwnershipLostException, "the stop reports the lost fence as ScopeOwnershipLostException");
        Soft(failed, fixture.Store.Partitions[kept].Owner is null && fixture.Store.Partitions[lost].Owner == "sibling",
            "the stop releases the partition the node still held and leaves the sibling's alone");
        Soft(failed, attached, "the stop completes the run, so the host attaches to a new runtime");
        if (attached) await reattached.StopAsync();
        var disposed = true;
        try { await node.DisposeAsync(); }
        catch (Exception) { disposed = false; }
        Soft(failed, disposed, "disposing the node after its stop reported the loss does not throw it again");
        if (failed.Count > 0) throw new InvalidOperationException(string.Join("; ", failed));
    }

    private static void Soft(List<string> failed, bool condition, string description)
    {
        Console.WriteLine((condition ? "ok " : "FAIL ") + description);
        if (!condition) failed.Add(description);
    }

    // The administration drain is bounded: at its deadline the stop becomes a kill, nothing renews, and the fence runs
    // out by its lease while the job that ignores cancellation is still inside.
    private static async Task AdministrationDeadlineAsync()
    {
        var fixture = new Fixture(manualClock: true);
        var type = new JobProbe();
        var lease = fixture.Options.Lease;
        await using var node = fixture.Node("administered");
        await node.StartAsync(fixture.Host(node, type, fixture.Store, ownedSet: true));
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var epoch = fixture.Fence.Epoch;
        var administration = new HostOwnershipAdministration(node, () => true, TimeSpan.FromMilliseconds(300));
        var drain = administration.DrainAsync().AsTask();
        try
        {
            var returned = await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(5))) == drain;
            var beats = node.Heartbeats.Renewed;
            for (var moved = TimeSpan.Zero; moved < lease + TimeSpan.FromSeconds(2); moved += TimeSpan.FromSeconds(1))
                fixture.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(300);
            var sibling = await fixture.Store.TakeAsync(0, "sibling", lease);
            Check(returned && node.Heartbeats.Renewed == beats && sibling is { } taken && taken.Epoch == epoch + 1 && !type.Exited.Task.IsCompleted,
                "an administration drain ends at its deadline, stops renewing, and the fence runs out by its lease");
        }
        finally
        {
            type.Finish.TrySetResult();
            await type.Exited.Task.WaitAsync(Timeout);
            await drain.WaitAsync(Timeout);
        }
    }

    // A stop that arrives after an abandoned stop waits for the interrupted release, then reports the abandonment.
    private static async Task StopAfterAbandonedAsync()
    {
        var fixture = new Fixture();
        var issued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releases = 0;
        var store = new CountingStore(fixture.Store, static () => { }, afterRelease: async () =>
        {
            if (Interlocked.Increment(ref releases) != 1) return;
            issued.TrySetResult();
            await finishRelease.Task.WaitAsync(Timeout);
        });
        var runtime = new PartitionRuntime("late-stop", store, 1, Timeout);
        await runtime.StartAsync(new DurableHost([new Worker(runtime.Node, 0, store, new ActorAdapter(fixture.Store, new JobProbe()), fixture.Options)]));
        Check(await runtime.ActivateAsync(0) is { Active: true }, "the late-stop fixture owns a real fence");
        using var deadline = new CancellationTokenSource();
        var stop = runtime.StopAsync(deadline.Token).AsTask();
        string outcome;
        try
        {
            await issued.Task.WaitAsync(Timeout);
            deadline.Cancel();
            await stop.WaitAsync(Timeout);
            var late = runtime.StopAsync().AsTask();
            await Task.Delay(200);
            Check(!late.IsCompleted, "a stop after an abandoned stop waits while the interrupted release is in flight");
            finishRelease.TrySetResult();
            try
            {
                await late.WaitAsync(Timeout);
                outcome = "success";
            }
            catch (OperationCanceledException) { outcome = "abandoned"; }
        }
        finally { finishRelease.TrySetResult(); }
        Check(outcome == "abandoned", "once the interrupted release returns, the late stop reports the abandonment");
    }

    // A stop that lands while the start validates its scopes finds no host yet. The start must not leave the host attached.
    private static async Task StopDuringStartAsync()
    {
        var fixture = new Fixture();
        var checking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCheck = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checks = 0;
        var store = new CountingStore(fixture.Store, static () => { }, beforePartitionCount: async () =>
        {
            if (Interlocked.Increment(ref checks) != 1) return;
            checking.TrySetResult();
            await finishCheck.Task.WaitAsync(Timeout);
        });
        await using var node = fixture.Node("stop-during-start");
        var host = fixture.Host(node, new JobProbe(), store, ownedSet: true);
        var start = node.StartAsync(host).AsTask();
        try
        {
            await checking.Task.WaitAsync(Timeout);
            await node.StopAsync().AsTask().WaitAsync(Timeout);
        }
        finally { finishCheck.TrySetResult(); }
        await start.WaitAsync(Timeout);
        var next = new PartitionRuntime(node.Node, store, 1, Timeout);
        var attached = true;
        try { await next.StartAsync(host); }
        catch (InvalidOperationException) { attached = false; }
        Check(attached && fixture.Fence.Owner is null, "a stop during the start's scope check leaves the host free for a new run");
        if (attached) await next.StopAsync();
    }

    // A direct stop with a pass under way joins that pass before its drain starts (and judges answers or the fence).
    // The host refuses activations once the drain has started, so a refused activation shows the drain began early.
    private static async Task PassInFlightAsync()
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        var stall = 1;
        var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new CountingStore(fixture.Store, static () => { }, beforeCommit: async _ =>
        {
            if (Interlocked.Exchange(ref stall, 0) == 0) return;
            stalled.TrySetResult();
            await resume.Task.WaitAsync(Timeout);
        });
        var runtime = new PartitionRuntime("pass-in-flight", store, 1, Timeout);
        var host = new DurableHost([new Worker(runtime.Node, 0, store, new ActorAdapter(fixture.Store, type), fixture.Options)]);
        await runtime.StartAsync(host);
        await runtime.ActivateAsync(0);
        await fixture.StartActorAsync(runtime.Node);
        Task passing = Task.CompletedTask;
        Task? stop = null;
        try
        {
            for (var pass = 0; pass < 16 && !stalled.Task.IsCompleted; pass++)
            {
                passing = runtime.PassAsync(0).AsTask();     // the start's commit stalls inside this pass
                await Task.WhenAny(passing, stalled.Task);
            }
            await stalled.Task.WaitAsync(Timeout);
            stop = runtime.StopAsync().AsTask();
            await Task.Delay(300);
            var drainWaited = await host.ActivateAsync(0) is not null;
            Check(drainWaited && !stop.IsCompleted, "a direct stop joins the pass under way before its drain starts");
        }
        finally
        {
            resume.TrySetResult();
            type.Finish.TrySetResult();
        }
        await passing.WaitAsync(Timeout);
        await stop!.WaitAsync(Timeout);
        Check(fixture.Fence.Owner is null, "after the joined pass the stop drains and releases");
    }

    // Two jobs: one entered and draining, the other's scheduler callback paused before admission. The resumed callback
    // passes every earlier guard (same generation, nothing released yet); only the entry epoch check keeps it out.
    private static async Task PausedEntryWhileDrainingAsync()
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        using var resumeGuard = new ManualResetEventSlim();
        var reachedGuard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var schedulerThread = 0;
        var callbacks = 0;
        var options = fixture.Options with
        {
            BeforeScheduledJobCheck = () =>
            {
                if (Interlocked.Increment(ref callbacks) == 2) Volatile.Write(ref schedulerThread, Environment.CurrentManagedThreadId);
            },
            Monotonic = () =>
            {
                if (Environment.CurrentManagedThreadId == Volatile.Read(ref schedulerThread) && !resumeGuard.IsSet)
                {
                    reachedGuard.TrySetResult();
                    if (!resumeGuard.Wait(Timeout)) throw new TimeoutException("The scheduler guard was never released.");
                }
                return fixture.Options.Monotonic();
            }
        };
        var issued = new System.Collections.Concurrent.ConcurrentQueue<Message>();     // job tickets in commit order
        var store = new CountingStore(fixture.Store, static () => { }, observeCommit: (batch, landed) =>
        {
            if (!landed) return;
            foreach (var turn in batch.Turns)
                foreach (var message in turn.Sent.Where(message => message.Kind == Kinds.Job)) issued.Enqueue(message);
        });
        await using var node = fixture.Node("paused-entry");
        var host = fixture.Host(node, type, store, ownedSet: true, options);
        await node.StartAsync(host);
        await fixture.StartActorAsync(node, "first");
        await type.Entered.Task.WaitAsync(Timeout);
        await fixture.StartActorAsync(node, "second");
        Task? stop = null, callback = null;
        try
        {
            await reachedGuard.Task.WaitAsync(Timeout);
            await WaitFor(() => issued.Count >= 2);
            var ticket = issued.ElementAt(1);
            await WaitFor(() => (callback = host.NodeWorker!.JobCompletion(ticket.Target.Id, "cleanup", ticket.TargetCreation ?? 0)) is not null);
            stop = node.StopAsync().AsTask();
            await type.Canceled.Task.WaitAsync(Timeout);
            resumeGuard.Set();
            await callback!.WaitAsync(Timeout);
            await Task.Delay(100);
            Check(type.Starts == 1 && !stop.IsCompleted && fixture.Fence.Owner == node.Node,
                "a callback paused before admission cannot enter while another entered job is still draining");
        }
        finally
        {
            resumeGuard.Set();
            type.Finish.TrySetResult();
        }
        await stop!.WaitAsync(Timeout);
        Check(type.Starts == 1 && fixture.Fence.Owner is null, "the drained job exits and the stop releases without the refused job");
    }

    // Two per-partition Workers, one entered job: the Worker without a job must stay live too, or the gate (every holding
    // Worker passed within a lease) withholds the node's heartbeat. The test clock moves past two leases.
    private static async Task LongDrainWorkersAsync()
    {
        const int partitions = 2;
        var fixture = new Fixture(partitions, manualClock: true);
        var type = new JobProbe();
        var lease = fixture.Options.Lease;
        var node = fixture.Node("drain-workers", partitions);
        var host = new DurableHost(Enumerable.Range(0, partitions)
            .Select(partition => new Worker(node.Node, partition, fixture.Store, new ActorAdapter(fixture.Store, type), fixture.Options)));
        await node.StartAsync(host);
        await WaitFor(() => fixture.Store.Partitions.All(row => row.Owner == node.Node));
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var epochs = fixture.Store.Partitions.Select(row => row.Epoch).ToArray();
        var stop = node.StopAsync().AsTask();
        try
        {
            await type.Canceled.Task.WaitAsync(Timeout);
            var every = true;
            for (var moved = TimeSpan.Zero; moved < lease * 2 + TimeSpan.FromSeconds(5); moved += TimeSpan.FromSeconds(1))
            {
                var renewed = node.Heartbeats.Renewed;
                fixture.Advance(TimeSpan.FromSeconds(1));
                every &= await RenewedAsync(node, renewed);
            }
            var taken = 0;
            for (var partition = 0; partition < partitions; partition++)
                if (await fixture.Store.TakeAsync(partition, "sibling", lease) is not null) taken++;
            Console.WriteLine($"  drain workers: renewed every step {every}, sibling took {taken}, renewals {node.Heartbeats.Renewed}");
            Check(every && taken == 0 && fixture.Store.Partitions.All(row => row.Owner == node.Node) &&
                fixture.Store.Partitions.Select(row => row.Epoch).SequenceEqual(epochs) && !stop.IsCompleted,
                "every owning per-partition Worker stays live while another Worker's job drains past two leases");
        }
        finally { type.Finish.TrySetResult(); }
        var outcome = await OutcomeAsync(stop);
        await DisposeQuietlyAsync(node);
        Check(outcome is null && fixture.Store.Partitions.All(row => row.Owner is null) &&
            fixture.Store.Partitions.Select(row => row.Epoch).SequenceEqual(epochs),
            $"after the job exits, both partitions are released at their original epochs ({outcome?.GetType().Name ?? "no error"})");
    }

    // One Worker loses its fence while another Worker's drain faults with its job still inside. The fault must not be
    // masked by the loss: the run is abandoned, nothing is released, and the host is not freed.
    private static async Task FaultMaskedByLossAsync()
    {
        const int partitions = 2;
        var fixture = new Fixture(partitions);
        JobProbe[] types = [new(), new()];
        var fault = new InvalidOperationException("injected drain fault");
        var faulting = 0;
        var options = fixture.Options with
        {
            BeforeDrainRound = () =>
            {
                if (Volatile.Read(ref faulting) == 0) return;
                Volatile.Write(ref faulting, 2);
                throw fault;
            }
        };
        var node = fixture.Node("masked-fault", partitions);
        var workers = Enumerable.Range(0, partitions).Select(partition => new Worker(node.Node, partition, fixture.Store,
            new ActorAdapter(fixture.Store, types[partition]), partition == 1 ? options : fixture.Options)).ToArray();
        var host = new DurableHost(workers);
        await node.StartAsync(host);
        await WaitFor(() => fixture.Store.Partitions.All(row => row.Owner == node.Node));
        for (var key = 0; key < 32 && !types.All(type => type.Entered.Task.IsCompleted); key++)
        {
            await fixture.StartActorAsync(node, "masked-" + key);
            await Task.Delay(100);
        }
        Check(types.All(type => type.Entered.Task.IsCompleted), "each of the two Workers has an entered job");
        var stop = node.StopAsync().AsTask();
        Exception? reported = null;
        List<string> failed = [];
        try
        {
            await Task.WhenAll(types.Select(type => type.Canceled.Task)).WaitAsync(Timeout);
            var row = fixture.Store.Partitions[0];
            (row.Owner, row.Epoch) = ("sibling", row.Epoch + 1);     // Worker 0 loses its fence at the next answer
            await WaitFor(() => !workers[0].Owns);     // Worker 0 has seen its loss
            types[0].Finish.TrySetResult();            // and its job exits, so its drain ends first, with the loss
            await types[0].Exited.Task.WaitAsync(Timeout);
            await Task.Delay(300);
            Volatile.Write(ref faulting, 1);           // then Worker 1's drain faults while its job is still inside
            await WaitFor(() => stop.IsCompleted || Volatile.Read(ref faulting) == 2);
            await Task.Delay(500);
        }
        finally
        {
            foreach (var type in types) type.Finish.TrySetResult();
        }
        try { await stop.WaitAsync(Timeout); }
        catch (Exception error) when (error is not TimeoutException) { reported = error; }
        var restart = new PartitionRuntime(node.Node, fixture.Store, partitions, Timeout);
        var attached = true;
        try { await restart.StartAsync(host); }
        catch (InvalidOperationException) { attached = false; }
        Console.WriteLine($"  masked fault: reported {reported?.GetType().Name}: {reported?.Message}; partition 1 owner {fixture.Store.Partitions[1].Owner ?? "none"}");
        Soft(failed, ReferenceEquals(reported, fault), "the stop reports the other Worker's fault, not the lost fence");
        Soft(failed, fixture.Store.Partitions[1].Owner == node.Node, "the faulted Worker's partition is not released while its job may be inside");
        Soft(failed, !attached, "the abandoned run keeps the host from a new runtime");
        if (attached) await restart.StopAsync();
        Soft(failed, await OutcomeAsync(node.DisposeAsync().AsTask()) is null, "disposing the node after its stop reported the fault does not throw it again");
        if (failed.Count > 0) throw new InvalidOperationException(string.Join("; ", failed));
    }

    // A direct stop while a balance round's set gain is under way joins it, so nothing is gained after the release.
    private static async Task StopDuringSetGainAsync()
    {
        const int partitions = 4;
        var fixture = new Fixture(partitions);
        var armed = 0;
        var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new CountingStore(fixture.Store, static () => { }, beforeSetTake: async () =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            stalled.TrySetResult();
            await resume.Task.WaitAsync(Timeout);
        });
        var runtime = new PartitionRuntime("set-gain-stop", store, partitions, Timeout);
        await runtime.StartAsync(DurableHost.ForNode(runtime.Node, store, new ActorAdapter(fixture.Store, new JobProbe()), partitions, fixture.Options));
        Check(await runtime.HeartbeatAsync(), "the direct runtime's heartbeat sees the free partitions");
        Volatile.Write(ref armed, 1);
        var balance = runtime.BalanceAsync();
        Task? stop = null;
        bool waited;
        try
        {
            await stalled.Task.WaitAsync(Timeout);
            stop = runtime.StopAsync().AsTask();
            await Task.Delay(200);
            waited = !stop.IsCompleted;
        }
        finally { resume.TrySetResult(); }
        await balance.WaitAsync(Timeout);
        await stop.WaitAsync(Timeout);
        Check(waited && fixture.Store.Partitions.All(row => row.Owner is null) && !Enumerable.Range(0, partitions).Any(runtime.Owns),
            "a direct stop joins a balance round's set gain and releases what it gained");
    }

    // A node disposed without a stop (a later hosted service failed to start) is bounded by its dispose deadline.
    private static async Task DisposeDeadlineAsync()
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        var node = new RuntimeNode("dispose-bound", fixture.Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(20), DisposeDeadline = TimeSpan.FromMilliseconds(300)
        });
        await node.StartAsync(fixture.Host(node, type, fixture.Store, ownedSet: true));
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var dispose = node.DisposeAsync().AsTask();
        try
        {
            var returned = await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(5))) == dispose;
            Check(returned && !type.Exited.Task.IsCompleted && fixture.Fence.Owner == node.Node,
                "disposing a node whose job ignores cancellation ends at its dispose deadline and leaves the fence for expiry");
        }
        finally
        {
            type.Finish.TrySetResult();
            await type.Exited.Task.WaitAsync(Timeout);
            await dispose.WaitAsync(Timeout);
        }
    }

    // A store error while the drain judges a heartbeat answer is retried, as a pass retries it; the stop then completes.
    private static async Task TransientAnswerErrorAsync(Failure failure)
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        var failures = 3;
        var options = fixture.Options with
        {
            BeforeDrainAnswer = () =>
            {
                if (Interlocked.Decrement(ref failures) >= 0) throw new StoreException(failure, $"injected {failure} store error");
            }
        };
        var node = fixture.Node("transient-answer");
        var host = fixture.Host(node, type, fixture.Store, ownedSet: true, options);
        await node.StartAsync(host);
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var epoch = fixture.Fence.Epoch;
        var stop = node.StopAsync().AsTask();
        Exception? reported = null;
        try
        {
            await type.Canceled.Task.WaitAsync(Timeout);
            await WaitFor(() => Volatile.Read(ref failures) < 0 || stop.IsCompleted);
            await RenewedAsync(node, node.Heartbeats.Renewed);
        }
        finally { type.Finish.TrySetResult(); }
        reported = await OutcomeAsync(stop);
        await DisposeQuietlyAsync(node);
        var recorded = host.NodeWorker!.DrainStoreErrors;
        Check(reported is null && fixture.Fence.Owner is null && fixture.Fence.Epoch == epoch && recorded.Count == 3 && recorded.Last is not null,
            $"{failure} store errors while the drain judges answers are recorded and retried, and the stop releases at the original epoch ({reported?.Message ?? "no error"}, recorded {recorded.Count})");
    }

    // A start queued behind a stop that released and then reported a lost fence attaches without that loss.
    private static async Task QueuedStartAfterLossAsync()
    {
        var fixture = new Fixture(manualClock: true);
        var type = new JobProbe();
        var runtime = new PartitionRuntime("queued-after-loss", fixture.Store, 1, Timeout);
        var worker = new Worker(runtime.Node, 0, fixture.Store, new ActorAdapter(fixture.Store, type), fixture.Options);
        var host = new DurableHost([worker]);
        await runtime.StartAsync(host);
        await runtime.ActivateAsync(0);
        await fixture.StartActorAsync(runtime.Node);
        Task stop = Task.CompletedTask, queued = Task.CompletedTask;
        try
        {
            for (var pass = 0; pass < 16 && !type.Entered.Task.IsCompleted; pass++) await runtime.PassAsync(0);
            await type.Entered.Task.WaitAsync(Timeout);
            stop = runtime.StopAsync().AsTask();
            await type.Canceled.Task.WaitAsync(Timeout);
            queued = runtime.StartAsync(host).AsTask();
            fixture.Advance(Timeout + TimeSpan.FromSeconds(2));     // no heartbeat renews this direct runtime: its fence runs out
            await WaitFor(() => !worker.Owns);
        }
        finally { type.Finish.TrySetResult(); }
        Exception? reported = null;
        try { await stop.WaitAsync(Timeout); }
        catch (Exception error) when (error is not TimeoutException) { reported = error; }
        var attached = true;
        try { await queued.WaitAsync(Timeout); }
        catch (Exception error) when (error is not TimeoutException) { attached = false; }
        Check(reported is ScopeOwnershipLostException && attached,
            $"a start queued behind a stop that reported a lost fence attaches without inheriting the loss ({reported?.GetType().Name ?? "no error"}, attached {attached})");
        if (attached) await runtime.StopAsync();
    }

    // A stop started without a deadline is joined by an administration drain with a short one: the joined deadline ends
    // only the administration's wait, and the original stop drains on and releases.
    private static async Task JoinedAdministrationAsync()
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        var node = fixture.Node("joined-administration");
        await node.StartAsync(fixture.Host(node, type, fixture.Store, ownedSet: true));
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var epoch = fixture.Fence.Epoch;
        var stop = node.StopAsync().AsTask();
        try
        {
            await type.Canceled.Task.WaitAsync(Timeout);
            var drain = new HostOwnershipAdministration(node, () => true, TimeSpan.FromMilliseconds(300)).DrainAsync().AsTask();
            await drain.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(500);
            Check(!stop.IsCompleted && fixture.Fence.Owner == node.Node && !type.Exited.Task.IsCompleted,
                "a joined administration drain ends only its own wait and does not kill the stop under way");
        }
        finally
        {
            type.Finish.TrySetResult();
            await DisposeQuietlyAsync(node);
        }
        await OutcomeAsync(stop);
        Check(fixture.Fence.Owner is null && fixture.Fence.Epoch == epoch, "the original stop then releases at the original epoch");
    }

    // A dispose that joins a stop under way still kills it at the dispose deadline: renewals stop, and the fence is left
    // to expire instead of being held by a stop that drains on.
    private static async Task DisposeJoinsStopAsync()
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        var node = new RuntimeNode("dispose-joins", fixture.Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(20), DisposeDeadline = TimeSpan.FromMilliseconds(300)
        });
        await node.StartAsync(fixture.Host(node, type, fixture.Store, ownedSet: true));
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var stop = node.StopAsync().AsTask();     // started first, with no deadline of its own
        try
        {
            await type.Canceled.Task.WaitAsync(Timeout);
            var disposed = await OutcomeAsync(node.DisposeAsync().AsTask()) is null;
            var stopEnded = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(2))) == stop;
            var beats = node.Heartbeats.Renewed;
            await Task.Delay(300);
            Check(disposed && stopEnded && node.Heartbeats.Renewed == beats && fixture.Fence.Owner == node.Node && !type.Exited.Task.IsCompleted,
                "a dispose that joins a stop under way kills it at the dispose deadline: renewals stop and the fence is left for expiry");
        }
        finally
        {
            type.Finish.TrySetResult();
            await type.Exited.Task.WaitAsync(Timeout);
            await OutcomeAsync(stop);
        }
    }

    // A turn drain that faults with anything but a lost fence abandons the run, as the job drain does: nothing is released.
    private static async Task TurnDrainFaultAsync()
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        var fault = new InvalidOperationException("injected turn drain fault");
        var node = new RuntimeNode("turn-drain-fault", fixture.Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
        }, beforeRelease: _ => Task.FromException(fault));
        await node.StartAsync(fixture.Host(node, type, fixture.Store, ownedSet: true));
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var stop = node.StopAsync().AsTask();
        bool canceled, waited;
        try
        {
            canceled = await Task.WhenAny(type.Canceled.Task, Task.Delay(TimeSpan.FromSeconds(3))) == type.Canceled.Task;
            await Task.Delay(200);
            waited = !stop.IsCompleted;
        }
        finally { type.Finish.TrySetResult(); }
        var reported = await OutcomeAsync(stop);
        await DisposeQuietlyAsync(node);
        var ticket = fixture.JobTicket();
        var published = fixture.Store.Messages.Values.Any(row => row.Message.Kind == Kinds.JobFinished);
        Check(canceled && waited && type.Exited.Task.IsCompleted && !ticket.Done && !published,
            $"a turn drain fault still cancels the entered job and waits for it to exit before the stop ends; its ticket stays outstanding and nothing is published (done {ticket.Done}, published {published})");
        Check(ReferenceEquals(reported, fault) && fixture.Fence.Owner == node.Node,
            $"a turn drain fault abandons the run: the stop reports it and releases nothing ({reported?.Message ?? "no error"}, owner {fixture.Fence.Owner ?? "none"})");
    }

    // The turn drain: a Worker whose own drain fails with a lost fence (its short guard expired) keeps getting the drain's
    // pass, so the gate stays open while the other Worker's entered turn drains on past two leases.
    private static async Task TurnDrainPendingAsync()
    {
        const int partitions = 2;
        var fixture = new Fixture(partitions, manualClock: true);
        TurnProbe[] types = [new(), new()];
        var lease = fixture.Options.Lease;
        var shortLease = fixture.Options with { Lease = TimeSpan.FromSeconds(2) };
        var workers = Enumerable.Range(0, partitions).Select(partition => new Worker("turn-pending", partition, fixture.Store,
            new ActorAdapter(fixture.Store, types[partition]), partition == 1 ? shortLease : fixture.Options)).ToArray();
        var host = new DurableHost(workers);
        var node = new RuntimeNode("turn-pending", fixture.Store, partitions, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
        }, beforeRelease: host.DrainEnteredTurnsAsync);
        await node.StartAsync(host);
        await WaitFor(() => fixture.Store.Partitions.All(row => row.Owner == node.Node));
        for (var key = 0; key < 32 && !types.All(type => type.Entered.Task.IsCompleted); key++)
        {
            await fixture.StartActorAsync(node.Node, "turn-" + key, TurnProbe.TypeKey);
            await Task.Delay(100);
        }
        Check(types.All(type => type.Entered.Task.IsCompleted), "each of the two Workers has an entered turn");
        var epoch = fixture.Store.Partitions[0].Epoch;
        var stop = node.StopAsync().AsTask();
        try
        {
            await Task.Delay(300);
            fixture.Advance(TimeSpan.FromSeconds(3));     // Worker 1's short guard expires: its own turn drain fails with a lost fence
            await WaitFor(() => !workers[1].Owns);
            var every = true;
            for (var moved = TimeSpan.Zero; moved < lease * 2 + TimeSpan.FromSeconds(5); moved += TimeSpan.FromSeconds(1))
            {
                var renewed = node.Heartbeats.Renewed;
                fixture.Advance(TimeSpan.FromSeconds(1));
                every &= await RenewedAsync(node, renewed);
                await Task.Delay(500);     // the entered turn's own drain passes on the real clock, every third of its lease
            }
            var sibling = await fixture.Store.TakeAsync(0, "sibling", lease);
            Console.WriteLine($"  turn drain pending: renewed every step {every}, sibling took {sibling?.Epoch.ToString() ?? "none"}, renewals {node.Heartbeats.Renewed}");
            Check(every && sibling is null && fixture.Store.Partitions[0].Owner == node.Node && fixture.Store.Partitions[0].Epoch == epoch && !stop.IsCompleted,
                "in the turn drain, a Worker whose drain lost its fence keeps the gate open while the other Worker's turn drains past two leases");
        }
        finally
        {
            foreach (var type in types) type.Release.TrySetResult();
        }
        await OutcomeAsync(stop);
        await DisposeQuietlyAsync(node);
        Check(fixture.Store.Partitions[0].Owner is null && fixture.Store.Partitions[0].Epoch == epoch,
            "after the turn ends, its partition is released at its original epoch");
    }

    /// <summary>An actor whose start turn stays entered until released, whatever the cancellation.</summary>
    private sealed class TurnProbe : Contract.IActorType
    {
        internal const string TypeKey = "host-drain-turn-probe";
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Type => TypeKey;
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (!turn.Inbox.Any(message => message.Kind == Contract.Kinds.Start)) return;
            Entered.TrySetResult();
            await Release.Task;
            turn.State = Json.Write("released");
        }
    }

    // The drain's outcome inspection on its own: a fault wins over a loss, a loss alone is reported as a loss, and a drain
    // cancelled without the deadline is a fault, not a clean drain.
    private static async Task DrainOutcomesAsync()
    {
        var host = new DurableHost([]);
        var loss = new ScopeOwnershipLostException("loss");
        var fault = new InvalidOperationException("fault");
        async Task<Exception?> Outcome(params Task[] drains) => await OutcomeAsync(host.KeepLiveWhileAsync([], drains, drainsKeepThemselves: false, CancellationToken.None));
        var clean = await Outcome(Task.CompletedTask);
        var masked = await Outcome(Task.FromException(loss), Task.FromException(fault));
        var lost = await Outcome(Task.FromException(loss), Task.CompletedTask);
        var stray = await Outcome(Task.FromCanceled(new CancellationToken(canceled: true)));
        Console.WriteLine($"  drain outcomes: clean {clean?.GetType().Name ?? "none"}, masked {masked?.Message}, lost {lost?.GetType().Name}, stray cancel {stray?.GetType().Name ?? "none"}: {stray?.Message}");
        Check(clean is null && ReferenceEquals(masked, fault) && ReferenceEquals(lost, loss) &&
            stray is InvalidOperationException and not ScopeOwnershipLostException,
            "the drain outcome: a fault wins over a loss, a loss alone is a loss, and a stray cancellation is a fault");
    }

    // A Worker whose guard expires during the drain goes pending beside a Worker whose job drains on. The pending Worker
    // keeps getting the drain's pass, so the gate stays open and the job Worker's partition keeps its epoch past two leases.
    private static async Task PendingWorkerAsync()
    {
        const int partitions = 2;
        var fixture = new Fixture(partitions, manualClock: true);
        JobProbe[] types = [new(), new()];
        types[1].Finish.TrySetResult();     // jobs on the short-lease Worker end at once
        var lease = fixture.Options.Lease;
        var node = fixture.Node("pending-worker", partitions);
        var shortLease = fixture.Options with { Lease = TimeSpan.FromSeconds(2) };
        var host = new DurableHost(Enumerable.Range(0, partitions).Select(partition => new Worker(node.Node, partition, fixture.Store,
            new ActorAdapter(fixture.Store, types[partition]), partition == 1 ? shortLease : fixture.Options)));
        await node.StartAsync(host);
        await WaitFor(() => fixture.Store.Partitions.All(row => row.Owner == node.Node));
        for (var key = 0; key < 32 && !types[0].Entered.Task.IsCompleted; key++)
        {
            await fixture.StartActorAsync(node, "pending-" + key);
            await Task.Delay(100);
        }
        await types[0].Entered.Task.WaitAsync(Timeout);
        var epoch = fixture.Store.Partitions[0].Epoch;
        var stop = node.StopAsync().AsTask();
        try
        {
            await types[0].Canceled.Task.WaitAsync(Timeout);
            fixture.Advance(TimeSpan.FromSeconds(3));     // past the short lease in one step: Worker 1's guard expires, it goes pending
            await Task.Delay(300);
            var every = true;
            for (var moved = TimeSpan.Zero; moved < lease * 2 + TimeSpan.FromSeconds(5); moved += TimeSpan.FromSeconds(1))
            {
                var renewed = node.Heartbeats.Renewed;
                fixture.Advance(TimeSpan.FromSeconds(1));
                every &= await RenewedAsync(node, renewed);
            }
            var sibling = await fixture.Store.TakeAsync(0, "sibling", lease);
            Console.WriteLine($"  pending worker: renewed every step {every}, sibling took {sibling?.Epoch.ToString() ?? "none"}, renewals {node.Heartbeats.Renewed}");
            Check(every && sibling is null && fixture.Store.Partitions[0].Owner == node.Node && fixture.Store.Partitions[0].Epoch == epoch && !stop.IsCompleted,
                "a Worker that went pending during the drain keeps the gate open, so the job Worker's partition keeps its epoch past two leases");
        }
        finally { types[0].Finish.TrySetResult(); }
        await OutcomeAsync(stop);
        await DisposeQuietlyAsync(node);
        Check(fixture.Store.Partitions[0].Owner is null && fixture.Store.Partitions[0].Epoch == epoch,
            "after the job exits, its partition is released at its original epoch");
    }

    // An error other than a store error while a drain round judges an answer is not retried: it faults the drain and the
    // run is abandoned.
    private static async Task AnswerFaultAsync(Exception fault)
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        var armed = 1;
        var options = fixture.Options with { BeforeDrainAnswer = () => { if (Interlocked.Exchange(ref armed, 0) == 1) throw fault; } };
        var node = fixture.Node("answer-fault");
        await node.StartAsync(fixture.Host(node, type, fixture.Store, ownedSet: true, options));
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var stop = node.StopAsync().AsTask();
        try
        {
            await type.Canceled.Task.WaitAsync(Timeout);
            await WaitFor(() => Volatile.Read(ref armed) == 0);
            await Task.Delay(200);
        }
        finally { type.Finish.TrySetResult(); }
        var reported = await OutcomeAsync(stop);
        await DisposeQuietlyAsync(node);
        Check(ReferenceEquals(reported, fault) && fixture.Fence.Owner == node.Node,
            $"an error other than a transient store error while the drain judges an answer faults the drain and releases nothing ({fault.GetType().Name}: {reported?.Message ?? "no error"}, owner {fixture.Fence.Owner ?? "none"})");
    }

    // An infinite dispose deadline is an infinite bound: the dispose waits for the graceful stop, which drains on.
    private static async Task DisposeInfiniteDeadlineAsync()
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        var node = new RuntimeNode("dispose-infinite", fixture.Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(20), DisposeDeadline = System.Threading.Timeout.InfiniteTimeSpan
        });
        await node.StartAsync(fixture.Host(node, type, fixture.Store, ownedSet: true));
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var epoch = fixture.Fence.Epoch;
        var dispose = node.DisposeAsync().AsTask();
        try
        {
            await Task.Delay(DisposeGracePlus);
            var beats = node.Heartbeats.Renewed;
            await Task.Delay(300);
            Check(!dispose.IsCompleted && node.Heartbeats.Renewed > beats && fixture.Fence.Owner == node.Node,
                "with an infinite dispose deadline the dispose waits past the grace while the stop drains and renews");
        }
        finally { type.Finish.TrySetResult(); }
        var outcome = await OutcomeAsync(dispose);
        Check(outcome is null && fixture.Fence.Owner is null && fixture.Fence.Epoch == epoch,
            "after the job exits, the graceful stop releases and the dispose returns");
    }

    private static readonly TimeSpan DisposeGracePlus = RuntimeNode.DisposeGrace + TimeSpan.FromSeconds(1);

    // A pass stuck past the kill holds the stop. The dispose returns at its deadline plus the grace and records that.
    private static async Task DisposeBoundExpiryAsync()
    {
        var fixture = new Fixture();
        var host = new StuckHost();
        var node = new RuntimeNode("dispose-bound-expiry", fixture.Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(20), DisposeDeadline = TimeSpan.FromMilliseconds(200)
        });
        await node.StartAsync(host);
        try
        {
            await host.Stuck.Task.WaitAsync(Timeout);
            var clock = Stopwatch.StartNew();
            var outcome = await OutcomeAsync(node.DisposeAsync().AsTask());
            var took = clock.Elapsed;
            Check(outcome is null && took < TimeSpan.FromSeconds(5) && node.LastError is TimeoutException && node.Errors > 0,
                $"a dispose whose killed stop is held by a stuck pass returns after its bound and records it ({took.TotalMilliseconds:F0} ms, {node.LastError?.GetType().Name ?? "nothing recorded"})");
        }
        finally { host.Release.TrySetResult(); }
    }

    /// <summary>A host whose one scope's pass, once started, stays stuck until released, whatever the cancellation.</summary>
    private sealed class StuckHost : IScopeHost
    {
        internal TaskCompletionSource Stuck { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default) =>
            new(new StuckScope(this, scope));

        private sealed class StuckScope(StuckHost host, int scope) : IScopeActivation
        {
            public int Scope => scope;
            public bool Active { get; private set; } = true;
            public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => default;
            public async ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default)
            {
                host.Stuck.TrySetResult();
                await host.Release.Task;
                return TimeSpan.FromMilliseconds(20);
            }
            public ValueTask DisposeAsync()
            {
                Active = false;
                return default;
            }
        }
    }

    // A stop and a dispose waiting together for a failed stop report it once, whichever of them waits first.
    private static async Task ConcurrentStopDisposeAsync()
    {
        foreach (var disposeFirst in new[] { true, false })
        {
            var fixture = new Fixture();
            var type = new JobProbe();
            var fault = new InvalidOperationException("injected turn drain fault");
            var logged = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
            var node = new RuntimeNode("stop-and-dispose", fixture.Store, 1, new LoopOptions
            {
                BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
            }, beforeRelease: _ => Task.FromException(fault), onFault: logged.Enqueue);
            await node.StartAsync(fixture.Host(node, type, fixture.Store, ownedSet: true));
            await fixture.StartActorAsync(node);
            await type.Entered.Task.WaitAsync(Timeout);
            Task first, second;
            if (disposeFirst)
            {
                first = node.DisposeAsync().AsTask();
                await Task.Delay(100);
                second = node.StopAsync().AsTask();
            }
            else
            {
                first = node.StopAsync().AsTask();
                await Task.Delay(100);
                second = node.DisposeAsync().AsTask();
            }
            await type.Canceled.Task.WaitAsync(Timeout);
            await Task.Delay(100);
            var errors = node.Errors;
            type.Finish.TrySetResult();
            var stopOutcome = await OutcomeAsync(disposeFirst ? second : first);
            var disposeOutcome = await OutcomeAsync(disposeFirst ? first : second);
            var recording = Stopwatch.StartNew();
            while (node.Errors == errors && recording.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20);
            await Task.Delay(100);
            Check(ReferenceEquals(stopOutcome, fault) && disposeOutcome is null && node.Errors - errors == 1 && ReferenceEquals(node.LastError, fault) &&
                logged.Count == 1 && logged.All(entry => ReferenceEquals(entry, fault)),
                $"a stop and a dispose waiting together: the stop throws the fault, the dispose stays quiet, and the fault is recorded and logged once ({(disposeFirst ? "dispose" : "stop")} first, recorded {node.Errors - errors}, logged {logged.Count})");
        }
    }

    // The abandon path: Worker 1's drain round faults while Worker 0's entered turn still drains. The job drain that ends
    // the entered jobs must wait for every Worker's turn drain, so it never judges answers beside a turn drain's flush.
    private static async Task AbandonWaitsTurnDrainsAsync()
    {
        const int partitions = 2;
        var fixture = new Fixture(partitions);
        MixedProbe[] types = [new(), new()];
        types[0].JobFinish.TrySetResult();      // Worker 0: a turn that stays entered; its jobs end at once
        types[1].TurnRelease.TrySetResult();    // Worker 1: a job that stays entered; its turns end at once
        var fault = new InvalidOperationException("injected drain round fault");
        var faulting = 0;
        var options = fixture.Options with { BeforeDrainRound = () => { if (Volatile.Read(ref faulting) == 1) throw fault; } };
        var workers = Enumerable.Range(0, partitions).Select(partition => new Worker("abandon-turns", partition, fixture.Store,
            new ActorAdapter(fixture.Store, types[partition]), partition == 1 ? options : fixture.Options)).ToArray();
        var host = new DurableHost(workers);
        var node = new RuntimeNode("abandon-turns", fixture.Store, partitions, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
        }, beforeRelease: host.DrainEnteredTurnsAsync);
        await node.StartAsync(host);
        await WaitFor(() => fixture.Store.Partitions.All(row => row.Owner == node.Node));
        for (var key = 0; key < 32 && !types[0].TurnEntered.Task.IsCompleted; key++)
        {
            await fixture.StartActorAsync(node.Node, "turn-" + key, MixedProbe.TypeKey);
            await Task.Delay(50);
        }
        for (var key = 0; key < 32 && !types[1].JobEntered.Task.IsCompleted; key++)
        {
            await fixture.StartActorAsync(node.Node, "job-" + key, MixedProbe.TypeKey);
            await Task.Delay(50);
        }
        Check(types[0].TurnEntered.Task.IsCompleted && types[1].JobEntered.Task.IsCompleted,
            "Worker 0 has an entered turn and Worker 1 an entered job");
        Volatile.Write(ref faulting, 1);
        var stop = node.StopAsync().AsTask();
        bool canceledEarly;
        try
        {
            await Task.Delay(700);
            canceledEarly = types[1].JobCanceled.Task.IsCompleted;
        }
        finally { types[0].TurnRelease.TrySetResult(); }
        var canceledAfter = await Task.WhenAny(types[1].JobCanceled.Task, Task.Delay(Timeout)) == types[1].JobCanceled.Task;
        types[1].JobFinish.TrySetResult();
        var reported = await OutcomeAsync(stop);
        await DisposeQuietlyAsync(node);
        Check(!canceledEarly && canceledAfter,
            $"the abandon path waits for every Worker's turn drain before the job drain cancels entered jobs (cancelled early {canceledEarly})");
        Check(ReferenceEquals(reported, fault) && fixture.Store.Partitions.All(row => row.Owner == node.Node),
            $"the abandoned run reports the fault and releases nothing ({reported?.Message ?? "no error"})");
    }

    // A Worker whose own drain faulted with anything but a lost fence still gets the drain's pass while others drain.
    private static async Task FaultedDrainPassAsync()
    {
        var fixture = new Fixture(2);
        var rounds = 0;
        var counting = fixture.Options with { BeforeDrainRound = () => Interlocked.Increment(ref rounds) };
        Worker[] workers =
        [
            new("faulted-pass", 0, fixture.Store, new ActorAdapter(fixture.Store, new JobProbe()), fixture.Options),
            new("faulted-pass", 1, fixture.Store, new ActorAdapter(fixture.Store, new JobProbe()), counting),
        ];
        var host = new DurableHost(workers);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fault = new InvalidOperationException("own drain fault");
        var drain = host.KeepLiveWhileAsync(workers, [pending.Task, Task.FromException(fault)], drainsKeepThemselves: true, CancellationToken.None);
        await WaitFor(() => Volatile.Read(ref rounds) > 0);
        pending.TrySetResult();
        var reported = await OutcomeAsync(drain);
        Check(rounds > 0 && ReferenceEquals(reported, fault),
            $"a Worker whose own drain faulted keeps getting the drain's pass, and the fault is reported once all drains end ({rounds} round(s))");
    }

    /// <summary>A start whose type data begins with "turn-" stays entered until released; "job-" starts a job.</summary>
    private sealed class MixedProbe : Contract.IActorType
    {
        internal const string TypeKey = "host-drain-mixed-probe";
        internal TaskCompletionSource TurnEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource TurnRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource JobEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource JobCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource JobFinish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Type => TypeKey;
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (!turn.Inbox.Any(message => message.Kind == Contract.Kinds.Start)) return;
            if (turn.Header.TypeData?.StartsWith("job-", StringComparison.Ordinal) == true)
            {
                turn.State = Json.Write("waiting");
                turn.StartJob("cleanup");
                return;
            }
            TurnEntered.TrySetResult();
            await TurnRelease.Task;
            turn.State = Json.Write("released");
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() => JobCanceled.TrySetResult());
            JobEntered.TrySetResult();
            await JobFinish.Task;
            return "completed";
        }
    }

    // A negative dispose deadline is refused when the node is made, not when it is disposed.
    private static Task NegativeDisposeDeadlineAsync()
    {
        var fixture = new Fixture();
        Exception? refused = null;
        try { _ = new RuntimeNode("negative-deadline", fixture.Store, 1, new LoopOptions { DisposeDeadline = TimeSpan.FromSeconds(-5) }); }
        catch (Exception error) { refused = error; }
        Check(refused is ArgumentOutOfRangeException, $"a negative dispose deadline is refused when the node is made ({refused?.GetType().Name ?? "accepted"})");
        return Task.CompletedTask;
    }

    // The abandon path's wait outlives the stop deadline: Worker 0's turn stays entered past the kill while Worker 1's
    // drain round faults. The stop returns at the kill; the fault that lands afterwards is still recorded on the node.
    private static async Task AbandonFaultAfterKillAsync()
    {
        const int partitions = 2;
        var fixture = new Fixture(partitions);
        MixedProbe[] types = [new(), new()];
        types[0].JobFinish.TrySetResult();
        types[1].TurnRelease.TrySetResult();
        types[1].JobFinish.TrySetResult();
        var fault = new InvalidOperationException("injected drain round fault after the kill");
        var faulting = 0;
        var options = fixture.Options with { BeforeDrainRound = () => { if (Volatile.Read(ref faulting) == 1) throw fault; } };
        var workers = Enumerable.Range(0, partitions).Select(partition => new Worker("fault-after-kill", partition, fixture.Store,
            new ActorAdapter(fixture.Store, types[partition]), partition == 1 ? options : fixture.Options)).ToArray();
        var host = new DurableHost(workers);
        var recorded = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var logged = 0;
        var node = new RuntimeNode("fault-after-kill", fixture.Store, partitions, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
        }, beforeRelease: host.DrainEnteredTurnsAsync, onFault: entry =>
        {
            Interlocked.Increment(ref logged);
            recorded.TrySetResult(entry);
        });
        await node.StartAsync(host);
        await WaitFor(() => fixture.Store.Partitions.All(row => row.Owner == node.Node));
        for (var key = 0; key < 32 && !types[0].TurnEntered.Task.IsCompleted; key++)
        {
            await fixture.StartActorAsync(node.Node, "turn-" + key, MixedProbe.TypeKey);
            await Task.Delay(50);
        }
        await types[0].TurnEntered.Task.WaitAsync(Timeout);
        Volatile.Write(ref faulting, 1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var stopped = await OutcomeAsync(node.StopAsync(deadline.Token).AsTask());     // the kill: it does not carry the fault
        types[0].TurnRelease.TrySetResult();     // the abandon wait ends now if it had not already, and the fault lands
        var landed = await Task.WhenAny(recorded.Task, Task.Delay(Timeout)) == recorded.Task ? await recorded.Task : null;
        await DisposeQuietlyAsync(node);
        await Task.Delay(100);
        Check(stopped is null && ReferenceEquals(landed, fault) && ReferenceEquals(node.LastError, fault) && Volatile.Read(ref logged) == 1,
            $"a fault that lands after the stop deadline is still recorded on the node and logged once ({landed?.Message ?? "nothing recorded"}, logged {Volatile.Read(ref logged)})");
    }

    // The dispose bound expires while a beside loop holds the stop; the stop's fault, when it lands, is recorded.
    private static async Task BoundExpiryFaultAsync()
    {
        var fixture = new Fixture();
        var fault = new InvalidOperationException("injected turn drain fault behind a stuck beside loop");
        var beside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logged = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var node = new RuntimeNode("bound-expiry-fault", fixture.Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(20), DisposeDeadline = TimeSpan.FromMilliseconds(200)
        }, beside: _ => beside.Task, beforeRelease: _ => Task.FromException(fault), onFault: logged.Enqueue);
        await node.StartAsync(fixture.Host(node, new JobProbe(), fixture.Store, ownedSet: true));
        await WaitFor(() => fixture.Fence.Owner == node.Node);
        var disposed = await OutcomeAsync(node.DisposeAsync().AsTask());
        var atBound = node.LastError;
        beside.TrySetResult();
        var clock = Stopwatch.StartNew();
        while (!ReferenceEquals(node.LastError, fault) && clock.Elapsed < Timeout) await Task.Delay(20);
        await Task.Delay(100);
        Check(disposed is null && atBound is TimeoutException && ReferenceEquals(node.LastError, fault) &&
            logged.Count == 2 && logged.First() is TimeoutException && ReferenceEquals(logged.Last(), fault),
            $"a dispose returns at its bound, and the stop's fault that lands later is recorded; each is logged once ({atBound?.GetType().Name ?? "nothing"} then {node.LastError?.Message ?? "nothing"}, logged {logged.Count})");
    }

    // A fence lost in the turn drain and again in the job drain is reported as one loss, not an aggregate.
    private static async Task LossInBothDrainsAsync()
    {
        const int partitions = 2;
        var fixture = new Fixture(partitions);
        var type = new JobProbe();
        var jobPartition = -1;
        var store = new CountingStore(fixture.Store, static () => { }, observeCommit: (batch, landed) =>
        {
            if (!landed) return;
            foreach (var turn in batch.Turns)
                if (turn.Sent.Any(message => message.Kind == Kinds.Job)) Volatile.Write(ref jobPartition, batch.PartitionOf(turn));
        });
        var turnLoss = new ScopeOwnershipLostException("injected turn drain loss");
        var node = new RuntimeNode("loss-in-both", store, partitions, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
        }, beforeRelease: _ => Task.FromException(turnLoss));
        await node.StartAsync(fixture.Host(node, type, store, ownedSet: true));
        await WaitFor(() => fixture.Store.Partitions.All(row => row.Owner == node.Node));
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        var stop = node.StopAsync().AsTask();
        try
        {
            await type.Canceled.Task.WaitAsync(Timeout);
            var row = fixture.Store.Partitions[Volatile.Read(ref jobPartition)];
            (row.Owner, row.Epoch) = ("sibling", row.Epoch + 1);     // the job drain loses this fence too
            await RenewedAsync(node, node.Heartbeats.Renewed);
            await Task.Delay(300);
        }
        finally { type.Finish.TrySetResult(); }
        var reported = await OutcomeAsync(stop);
        await DisposeQuietlyAsync(node);
        Check(reported is ScopeOwnershipLostException,
            $"a fence lost in both drains is reported as one ScopeOwnershipLostException ({reported?.GetType().Name ?? "nothing"})");
    }

    // A judgement takes a newer answer, one that names a loss, than the one a drain round read before; then a transient
    // store error interrupts it. The loss is reported, and the newer answer, not the older, waits for the next round.
    private static async Task AnswerPutBackAsync(bool newerAfterConsumption)
    {
        var fixture = new Fixture(2, manualClock: true);
        var store = fixture.Store;
        var lease = fixture.Options.Lease;
        var window = TimeSpan.FromSeconds(3);
        Worker? worker = null;
        Worker.HeartbeatSent? sentNew = null, sentNewest = null;
        NodeBeat? answerNew = null, answerNewest = null;
        var deliverNewer = 0;
        var failOnce = 0;
        var options = fixture.Options with
        {
            BeforeDrainAnswer = () => { if (Interlocked.Exchange(ref deliverNewer, 0) == 1) worker!.HeartbeatAnswered(sentNew!, answerNew!); },
            AfterAnswerConsumed = () =>
            {
                if (Interlocked.Exchange(ref failOnce, 0) != 1) return;
                // An even newer answer arrives while the judgement runs: the put-back must keep it.
                if (sentNewest is not null) worker!.HeartbeatAnswered(sentNewest, answerNewest!);
                throw new StoreException(Failure.Transient, "injected transient error while judging");
            }
        };
        worker = new Worker("put-back", 0, store, new ActorAdapter(store, new JobProbe()), options);
        await worker.TakeAsync();
        await store.TakeAsync(1, "put-back", lease);     // listed for this node but not held: the answer waits for a pass
        var sentOld = worker.BeforeHeartbeat();
        worker.HeartbeatAnswered(sentOld, await store.NodeHeartbeatAsync("put-back", lease, window));
        Check(worker.Owns && worker.PendingAnswerSentAt == sentOld.At, "an older answer that names no loss is recorded");
        fixture.Advance(TimeSpan.FromSeconds(1));
        var row = store.Partitions[0];
        (row.Owner, row.Epoch) = ("sibling", row.Epoch + 1);     // the newer answer omits partition 0: a loss
        sentNew = worker.BeforeHeartbeat();
        answerNew = await store.NodeHeartbeatAsync("put-back", lease, window);
        if (newerAfterConsumption)
        {
            fixture.Advance(TimeSpan.FromSeconds(1));
            sentNewest = worker.BeforeHeartbeat();
            answerNewest = await store.NodeHeartbeatAsync("put-back", lease, window);
        }
        (deliverNewer, failOnce) = (1, 1);
        var kept = await worker.KeepDrainLiveAsync(worker.DrainGeneration, ownedAtStart: true, CancellationToken.None);
        var pending = worker.PendingAnswerSentAt;
        Console.WriteLine($"  put-back: kept {kept}, pending answer sent at {pending?.ToString() ?? "none"} (older {sentOld.At}, newer {sentNew.At}, newest {sentNewest?.At.ToString() ?? "none"})");
        if (newerAfterConsumption)
            Check(!kept && pending == sentNewest!.At,
                "a judgement interrupted by a transient error keeps an answer newer than the one it took, and reports the loss it took");
        else
            Check(!kept && pending == sentNew.At,
                "a judgement interrupted by a transient error reports the loss its newer answer names, and puts back that answer, not the older one");
    }

    // A dispose deadline past the longest timer delay is infinite, not an error at dispose.
    private static async Task DisposeMaxDeadlineAsync()
    {
        var fixture = new Fixture();
        var node = new RuntimeNode("dispose-max", fixture.Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(20), DisposeDeadline = TimeSpan.MaxValue
        });
        await node.StartAsync(fixture.Host(node, new JobProbe(), fixture.Store, ownedSet: true));
        await WaitFor(() => fixture.Fence.Owner == node.Node);
        var outcome = await OutcomeAsync(node.DisposeAsync().AsTask());
        Check(outcome is null && fixture.Fence.Owner is null,
            $"a dispose deadline of TimeSpan.MaxValue is infinite: the dispose stops and releases ({outcome?.GetType().Name ?? "no error"})");
    }

    // A drain that throws a cancellation of its own, with no kill, ends the stop cancelled: that is recorded and logged.
    private static async Task CanceledHaltAsync()
    {
        var fixture = new Fixture();
        var logged = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var node = new RuntimeNode("canceled-halt", fixture.Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
        }, beforeRelease: _ => Task.FromException(new OperationCanceledException("a drain's own cancellation")), onFault: logged.Enqueue);
        await node.StartAsync(fixture.Host(node, new JobProbe(), fixture.Store, ownedSet: true));
        await WaitFor(() => fixture.Fence.Owner == node.Node);
        var outcome = await OutcomeAsync(node.StopAsync().AsTask());
        await Task.Delay(100);
        await DisposeQuietlyAsync(node);
        Check(outcome is OperationCanceledException && node.LastError is OperationCanceledException && logged.Count == 1,
            $"a stop that ends cancelled without a kill is recorded and logged once ({outcome?.GetType().Name ?? "no error"}, recorded {node.LastError?.GetType().Name ?? "nothing"}, logged {logged.Count})");
    }

    // A cancellation the kill caused is no fault: nothing is recorded or reported.
    private static async Task KillCancelNotRecordedAsync()
    {
        var fixture = new Fixture();
        var logged = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var node = new RuntimeNode("kill-cancel", fixture.Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
        }, beforeRelease: async token => await Task.Delay(System.Threading.Timeout.Infinite, token), onFault: logged.Enqueue);
        await node.StartAsync(fixture.Host(node, new JobProbe(), fixture.Store, ownedSet: true));
        await WaitFor(() => fixture.Fence.Owner == node.Node);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var outcome = await OutcomeAsync(node.StopAsync(deadline.Token).AsTask());
        await Task.Delay(200);
        await DisposeQuietlyAsync(node);
        Check(outcome is null && logged.IsEmpty && node.LastError is null,
            $"a cancellation the kill caused is not recorded (outcome {outcome?.GetType().Name ?? "none"}, logged {logged.Count}, recorded {node.LastError?.GetType().Name ?? "nothing"})");
    }

    // The turn drain throws its own cancellation, then a kill lands while the abandoned run's job drain waits: the drain's
    // cancellation is recorded once, as thrown, with its own message.
    private static async Task OwnCancelThenKillAsync()
    {
        var fixture = new Fixture();
        var type = new JobProbe();
        var own = new OperationCanceledException("the turn drain's own cancellation");
        var logged = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var node = new RuntimeNode("own-cancel-kill", fixture.Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
        }, beforeRelease: async _ =>
        {
            await Task.Yield();
            throw own;
        }, onFault: logged.Enqueue);
        await node.StartAsync(fixture.Host(node, type, fixture.Store, ownedSet: true));
        await fixture.StartActorAsync(node);
        await type.Entered.Task.WaitAsync(Timeout);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));     // lands while the job drain waits
        Exception? outcome;
        try { outcome = await OutcomeAsync(node.StopAsync(deadline.Token).AsTask()); }
        finally { type.Finish.TrySetResult(); }
        await Task.Delay(200);
        await DisposeQuietlyAsync(node);
        Check(logged.Count == 1 && ReferenceEquals(logged.First(), own) && ReferenceEquals(node.LastError, own),
            $"a drain's own cancellation followed by a kill is recorded once, as thrown ({logged.Count} logged, {node.LastError?.Message ?? "nothing recorded"}; stop {outcome?.GetType().Name ?? "returned"})");
    }

    // Errors the loops survive reach the host's callback, once per distinct error: a crashed beside loop, and a pass that
    // fails every time.
    private static async Task LoopErrorsLoggedAsync()
    {
        var fixture = new Fixture();
        var logged = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var host = new ThrowingHost();
        var node = new RuntimeNode("loop-errors", fixture.Store, 1, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
        }, beside: _ => Task.FromException(new InvalidOperationException("the beside loop crashed")), onFault: logged.Enqueue);
        await node.StartAsync(host);
        var clock = Stopwatch.StartNew();
        while (Volatile.Read(ref host.Passes) < 4 && clock.Elapsed < Timeout) await Task.Delay(20);
        await DisposeQuietlyAsync(node);
        var passErrors = logged.Count(entry => entry.Message == ThrowingHost.Message);
        var besideErrors = logged.Count(entry => entry.Message == "the beside loop crashed");
        Check(host.Passes >= 4 && node.Errors >= host.Passes && passErrors == 1 && besideErrors == 1,
            $"loop errors reach the callback once per distinct error ({host.Passes} failing passes, {node.Errors} counted, logged pass {passErrors}, beside {besideErrors})");
    }

    /// <summary>A host whose one scope's every pass throws.</summary>
    private sealed class ThrowingHost : IScopeHost
    {
        internal const string Message = "a pass that fails every time";
        internal int Passes;
        public ValueTask<IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default) =>
            new(new ThrowingScope(this, scope));

        private sealed class ThrowingScope(ThrowingHost host, int scope) : IScopeActivation
        {
            public int Scope => scope;
            public bool Active { get; private set; } = true;
            public ValueTask WokenAsync(InstanceRef? actor, CancellationToken cancellationToken = default) => default;
            public ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref host.Passes);
                throw new InvalidOperationException(Message);
            }
            public ValueTask DisposeAsync()
            {
                Active = false;
                return default;
            }
        }
    }

    // Deadlines at the edge of the timer's longest delay: the kill and the bound agree, and neither throws at dispose.
    private static async Task DisposeDeadlineBoundaryAsync()
    {
        var longest = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        var results = new List<string>();
        foreach (var deadline in new[] { longest - RuntimeNode.DisposeGrace, longest - RuntimeNode.DisposeGrace + TimeSpan.FromMilliseconds(1), longest })
        {
            var fixture = new Fixture();
            var node = new RuntimeNode("dispose-boundary", fixture.Store, 1, new LoopOptions
            {
                BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100),
                PollInterval = TimeSpan.FromMilliseconds(20), DisposeDeadline = deadline
            });
            await node.StartAsync(fixture.Host(node, new JobProbe(), fixture.Store, ownedSet: true));
            await WaitFor(() => fixture.Fence.Owner == node.Node);
            var outcome = await OutcomeAsync(node.DisposeAsync().AsTask());
            results.Add(outcome is null && fixture.Fence.Owner is null ? "ok" : outcome?.GetType().Name ?? "kept");
        }
        Check(results.All(result => result == "ok"),
            $"dispose deadlines at the timer limit (limit - grace, limit - grace + 1 ms, limit) stop and release ({string.Join(", ", results)})");
    }

    private static async Task<Exception?> OutcomeAsync(Task task)
    {
        try
        {
            await task.WaitAsync(Timeout);
            return null;
        }
        catch (TimeoutException error) { return error; }
        catch (Exception error) { return error; }
    }

    private static async Task DisposeQuietlyAsync(RuntimeNode node)
    {
        try { await node.DisposeAsync().AsTask().WaitAsync(Timeout); }
        catch (Exception) { }     // the case checks the stop's own outcome
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

        internal RuntimeNode Node(string name, int partitions = 1) => new(name, Store, partitions, new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(20)
        });

        internal DurableHost Host(RuntimeNode node, JobProbe model, IKernelStore store, bool ownedSet, WorkerOptions? options = null)
        {
            var adapter = new ActorAdapter(Store, model);
            return ownedSet ? DurableHost.ForNode(node.Node, store, adapter, Store.Partitions.Length, options ?? Options) :
                new DurableHost([new Worker(node.Node, 0, store, adapter, options ?? Options)]);
        }

        internal Task<Contract.ActorRef> StartActorAsync(RuntimeNode node, string key = "drain") => StartActorAsync(node.Node, key);

        internal async Task<Contract.ActorRef> StartActorAsync(string node, string key = "drain", string type = JobProbe.TypeKey)
        {
            var first = key == "drain";
            var ingress = new InMemoryIngress(Store, new Requests(first ? "job-drain" : "job-drain-" + key), node);
            var receipt = await ingress.SendAsync(Scope, new Contract.Target.Start(new(type, key, History: Contract.History.Full)),
                first ? "start" : "start-" + key, Contract.Kinds.Start);
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

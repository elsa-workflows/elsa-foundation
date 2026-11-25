using System.Diagnostics;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresPreviewLifetimeChecks
{
    private const string WaitingState = "\"waiting\"";

    internal static async Task<int> RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        var token = deadline.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use a dedicated PostgreSQL fixture database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => new(options);
        await using var observer = Open();
        if (!await PostgresFixtureDatabase.IsEmptyAsync(observer, token))
            throw new InvalidOperationException("The host lifetime fixture requires an empty PostgreSQL database.");
        var store = new EfStore(Open, postgres: true);
        await store.CreateAsync(1);
        await store.InstallPreviewRootSchemaAsync(token);
        var scope = EmbeddedTenant.Bind("host-lifetime");
        var ingress = await store.OpenPreviewRootIngressAsync(WaitingActor.Name, TimeSpan.FromHours(1), token);
        var reads = await store.OpenPreviewRootReadsAsync(cancellationToken: token);
        var start = await ingress.SendAsync(scope, new Contract.Target.Start(new(WaitingActor.Name, "fixture")),
            "start", Contract.Kinds.Start, cancellationToken: token);
        Require(start is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the first root is admitted");
        var reference = start.Actor!.Value;
        var sibling = await ingress.SendAsync(scope, new Contract.Target.Start(new(WaitingActor.Name, "sibling")),
            "sibling-start", Contract.Kinds.Start, cancellationToken: token);
        Require(sibling is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "an independent sibling root is admitted");
        var siblingReference = sibling.Actor!.Value;
        var initial = new WaitingActor();
        using (var host = Build("lifetime-first", initial))
        {
            Task? stopping = null;
            try
            {
                await host.StartAsync(token);
                await Task.WhenAll(initial.Entered.Task, initial.SiblingEntered.Task).WaitAsync(token);
                stopping = host.StopAsync(token);
                var owner = await ReadFenceAsync();
                var lease = await observer.Nodes.AsNoTracking().Where(node => node.Id == owner.Owner)
                    .Select(node => node.LeaseUntil).SingleAsync(token);
                Require(!stopping.IsCompleted && owner.Owner == "lifetime-first" &&
                    await reads.GetAsync(scope, reference, token) is null, "application stop waits while the entered turn still owns its fence");
                initial.ReleaseSibling.TrySetResult();
                while (await reads.GetAsync(scope, siblingReference, token) is null && !stopping.IsCompleted)
                    await Task.Delay(25, token);
                Require(!stopping.IsCompleted && !initial.Release.Task.IsCompleted &&
                    await store.ReadFirstRootAsync(scope.Tenant, siblingReference.Id, token) is { Revision: 1, State: WaitingState },
                    "the completed sibling commits while the slower entered turn is still waiting");

                // A short turn could pass with the heartbeat already stopped. Hold this turn past the original lease.
                await Task.Delay(Defaults.Lease, token);
                var renewed = await observer.Nodes.AsNoTracking().Where(node => node.Id == owner.Owner)
                    .Select(node => node.LeaseUntil).SingleAsync(token);
                Require(!stopping.IsCompleted && renewed > lease && (await ReadFenceAsync()).Owner == "lifetime-first",
                    "the drain renews ownership beyond the original lease while the slow turn remains entered");
                initial.Release.TrySetResult();
                await stopping;
                var after = await ReadFenceAsync();
                var saved = await store.ReadFirstRootAsync(scope.Tenant, reference.Id, token);
                Console.WriteLine($"shutdown witness: owner={after.Owner ?? "<none>"}, epoch={owner.Epoch}->{after.Epoch}, revision={saved?.Revision}, state={saved?.State}, status={saved?.Status}, turns={initial.Turns}");
                Require(after.Owner is null && after.Epoch == owner.Epoch && saved is { Revision: 1, State: WaitingState },
                    "the entered turn commits before the application gives up ownership");
            }
            finally
            {
                initial.Release.TrySetResult();
                initial.ReleaseSibling.TrySetResult();
                if (stopping is not null) await stopping;
            }
        }

        var prefix = await reads.TailAsync(scope, reference, cancellationToken: token);
        var firstFence = await ReadFenceAsync();
        var fresh = new WaitingActor(acceptStart: false);
        using (var replacement = Build("lifetime-second", fresh))
        {
            await replacement.StartAsync(token);
            var resumed = await SendCompleteAsync();
            Require(resumed.Outcome == Contract.AdmissionOutcome.Accepted, "the replacement accepts a continuation");
            while (await reads.GetAsync(scope, reference, token) is not { Header.Status: Contract.ActorStatus.Finished })
                await Task.Delay(25, token);
            var finished = await store.ReadFirstRootAsync(scope.Tenant, reference.Id, token);
            var fence = await ReadFenceAsync();
            Require(finished is { Revision: 2 } && fresh.Turns == 1 &&
                fence.Owner == "lifetime-second" && fence.Epoch > firstFence.Epoch,
                "the replacement continues the saved state under a newer fence");
            var suffix = await reads.TailAsync(scope, reference, prefix.Next, token);
            Require(prefix.Items is [{ Revision: 1 }, ..] && prefix.Next is not null &&
                suffix.Items is [{ Revision: 2 }, ..] && suffix.Items.All(entry => entry.Revision == 2),
                "the original journal bookmark returns the resumed revision");
            Require((await SendCompleteAsync()).Outcome == Contract.AdmissionOutcome.Duplicate &&
                (await reads.TailAsync(scope, reference, suffix.Next, token)).Items.Count == 0,
                "replaying the continuation adds no journal entry");
            await replacement.StopAsync(token);
            Require((await ReadFenceAsync()).Owner is null, "the replacement also releases ownership through application shutdown");
        }
        // KERNEL_LIFETIME_PROBES=name[,name]: run only these deadline probes after the main flow (for fail-before witnesses).
        string[] probes = ["turn-deadline", "startup-deadline", "startup-retry", "dispose-prompt", "dispose-stuck", "dispose-report", "dispose-joins", "negative-deadline", "shutdown-fault", "joined-stop-warning", "aggregate-once", "loop-error-repeat", "drain-fault-then-kill", "late-fault-warning", "joined-stop-no-escalation", "kill-warning-inline", "kill-window-warning"];
        var only = Environment.GetEnvironmentVariable("KERNEL_LIFETIME_PROBES")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (only is { Length: 0 } || only?.FirstOrDefault(name => !probes.Contains(name, StringComparer.Ordinal)) is { } unknown)
            throw new InvalidOperationException($"KERNEL_LIFETIME_PROBES names no known probe ({string.Join(", ", probes)}).");
        bool Probe(string name) => only is null || only.Contains(name, StringComparer.Ordinal);
        if (Probe("turn-deadline")) await CheckTurnDeadlineAsync();
        if (Probe("startup-deadline")) await CheckStartupDeadlineAsync();
        if (Probe("startup-retry")) await CheckStartupRetryAsync();
        if (Probe("dispose-prompt")) await CheckDisposeDeadlineAsync(stuck: false);
        if (Probe("dispose-stuck")) await CheckDisposeDeadlineAsync(stuck: true);
        if (Probe("dispose-report")) await CheckDisposeReportsOnceAsync();
        if (Probe("dispose-joins")) await CheckJoinedDisposeKillsAsync();
        if (Probe("negative-deadline")) CheckNegativeDisposeDeadline();
        if (Probe("shutdown-fault")) await CheckShutdownFaultAsync();
        if (Probe("joined-stop-warning")) await CheckJoinedStopWarningAsync();
        if (Probe("aggregate-once")) await CheckAggregateOnceAsync();
        if (Probe("loop-error-repeat")) CheckLoopErrorRepeat();
        if (Probe("drain-fault-then-kill")) await CheckDrainFaultThenKillAsync();
        if (Probe("late-fault-warning")) await CheckLateFaultWarningAsync();
        if (Probe("joined-stop-no-escalation")) await CheckJoinedStopNoEscalationAsync();
        if (Probe("kill-warning-inline")) await CheckKillWarningInlineAsync();
        if (Probe("kill-window-warning")) await CheckKillWindowWarningAsync();
        return 0;

        // A StopAsync that joins a stop under way, with a short deadline of its own, kills nothing and so never runs the
        // process policy, even past KillGrace while the stop it joined still drains.
        async Task CheckJoinedStopNoEscalationAsync()
        {
            const string name = "joined-stop-no-escalation";
            var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(new(WaitingActor.Name, "fixture")),
                name + "-start", Contract.Kinds.Start, cancellationToken: token);
            Require(admitted.Outcome == Contract.AdmissionOutcome.Accepted, $"the {name} probe admits an independent root");
            var held = new WaitingActor();
            var escalations = 0;
            var service = new PostgresPreviewHostedService(Open, name, held, _ => Interlocked.Increment(ref escalations),
                new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(25) });
            using var application = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Task? first = null;
            try
            {
                await service.StartAsync(token);
                await held.Entered.Task.WaitAsync(token);
                first = service.StopAsync(application.Token);     // the stop with a distant deadline is under way
                using var joined = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                Exception? outcome = null;
                try { await service.StopAsync(joined.Token).WaitAsync(token); }
                catch (OperationCanceledException error) when (!token.IsCancellationRequested) { outcome = error; }
                await Task.Delay(PostgresPreviewHostedService.KillGrace + TimeSpan.FromMilliseconds(700), token);
                Require(outcome is OperationCanceledException && Volatile.Read(ref escalations) == 0 && !first.IsCompleted,
                    $"a joined stop's deadline ends only its own wait and never runs the process policy ({Volatile.Read(ref escalations)} escalations, first stop {(first.IsCompleted ? "ended" : "still draining")})");
            }
            finally
            {
                held.Release.TrySetResult();
                if (first is not null)
                    try { await first.WaitAsync(token); }
                    catch (Exception) when (!token.IsCancellationRequested) { }
                await service.DisposeAsync();
            }
        }

        // The deadline's kill completes the node's stop inside the kill's own callback: the turn drain waits on the kill
        // token, and the node has no heartbeat or beside loop to join. The kill is still warned of, once.
        async Task CheckKillWarningInlineAsync()
        {
            var log = new RecordingLogger();
            var service = InMemoryService("kill-warning-inline", log, (store, report) =>
                (new RuntimeNode("kill-warning-inline", store, 1, InMemoryLoop,
                    beforeRelease: kill => Task.Delay(System.Threading.Timeout.Infinite, kill), onFault: report), new SlowReleaseScopes()),
                static (_, _) => Task.CompletedTask);
            await service.StartAsync(token);
            using var application = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            try { await service.StopAsync(application.Token).WaitAsync(token); }
            catch (OperationCanceledException) when (application.IsCancellationRequested) { }
            await Task.Delay(200, token);
            await service.DisposeAsync();
            Require(log.Errors.Count == 0 && log.Warnings.Count == 1 && log.Warnings[0].Contains("lease expiry", StringComparison.Ordinal),
                $"a deadline kill whose stop completes inside the kill is warned of once ({log.Errors.Count} Error, {log.Warnings.Count} Warning)");
        }

        // The kill interrupts the stop's release; the halt then waits for its beside loop, which the test holds. The release
        // faults inside that window, before the stop returns: the fault is the kill's and is logged at Warning, not Error.
        async Task CheckKillWindowWarningAsync()
        {
            var scopes = new SlowReleaseScopes();
            var beside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var log = new RecordingLogger();
            var service = InMemoryService("kill-window-warning", log, (store, report) =>
                (new RuntimeNode("kill-window-warning", store, 1, InMemoryLoop, beside: async end =>
                {
                    try { await Task.Delay(System.Threading.Timeout.Infinite, end); }
                    catch (OperationCanceledException) { }
                    await beside.Task;     // the halt waits for this after the kill
                }, onFault: report), scopes), static (_, _) => Task.CompletedTask);
            await service.StartAsync(token);
            using var application = new CancellationTokenSource();
            var stopping = service.StopAsync(application.Token);
            await scopes.Releasing.Task.WaitAsync(token);
            application.Cancel();     // the kill lands while the release is in flight
            await Task.Delay(200, token);
            scopes.Finish.TrySetResult();     // the release faults while the halt still waits for the beside loop
            var deadline = Stopwatch.StartNew();
            while (log.Errors.Count + log.WarningErrors.Count(error => ReferenceEquals(error, scopes.Fault)) == 0 && deadline.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(20, token);
            var returned = stopping.IsCompleted;
            beside.TrySetResult();
            try { await stopping.WaitAsync(token); }
            catch (OperationCanceledException) when (application.IsCancellationRequested) { }
            await Task.Delay(200, token);
            await service.DisposeAsync();
            Require(!returned && log.Errors.Count == 0 && log.WarningErrors.Count(error => ReferenceEquals(error, scopes.Fault)) == 1,
                $"a kill-caused fault that lands before the killed stop returns is logged at Warning, not Error (stop returned {returned}, {log.Errors.Count} Error, {log.Warnings.Count} Warning)");
        }

        // The turn drain faults before any kill; the job drain then waits for a job that ignores cancellation until the
        // application deadline kills it. The fault is logged once at Error, the kill once at Warning.
        async Task CheckDrainFaultThenKillAsync()
        {
            var fault = new InvalidOperationException("injected turn drain fault before the kill");
            var job = new HeldJobActor();
            var log = new RecordingLogger();
            var service = InMemoryService("drain-fault-then-kill", log, (store, report) =>
            {
                var node = new RuntimeNode("drain-fault-then-kill", store, 1, InMemoryLoop, beforeRelease: async _ =>
                {
                    await Task.Yield();
                    throw fault;
                }, onFault: report);
                return (node, DurableHost.ForNode(node.Node, store, new Kernel.Durable.ActorAdapter(store, job), 1, InMemoryWorkers));
            }, async (store, node) => await StartInMemoryActorAsync(store, node, HeldJobActor.Name));
            await service.StartAsync(token);
            try
            {
                await job.Entered.Task.WaitAsync(token);
                using var application = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));     // lands while the job drain waits
                try { await service.StopAsync(application.Token); }
                catch (OperationCanceledException) when (application.IsCancellationRequested) { }
            }
            finally { job.Finish.TrySetResult(); }
            await Task.Delay(300, token);
            await service.DisposeAsync();
            Require(log.Errors.Count == 1 && ReferenceEquals(log.Errors[0], fault) && log.Warnings.Count == 1 && log.Warnings[0].Contains("lease expiry", StringComparison.Ordinal),
                $"a turn drain fault before the kill is logged once at Error and the kill once at Warning ({log.Errors.Count} Error, {log.Warnings.Count} Warning: {string.Join(" | ", log.Warnings)})");
        }

        // The deadline kills the stop while its release is in flight; the release faults later. The fault is logged once,
        // at Warning, with no Error. (The kill's own Warning is not asserted: it is logged only when the service's wait
        // sees the deadline before the node's stop returns.)
        async Task CheckLateFaultWarningAsync()
        {
            var scopes = new SlowReleaseScopes();
            var log = new RecordingLogger();
            var service = InMemoryService("late-fault-warning", log, (store, report) =>
                (new RuntimeNode("late-fault-warning", store, 1, InMemoryLoop, onFault: report), scopes), static (_, _) => Task.CompletedTask);
            await service.StartAsync(token);
            using var application = new CancellationTokenSource();
            var stopping = service.StopAsync(application.Token);
            await scopes.Releasing.Task.WaitAsync(token);
            application.Cancel();     // the kill lands while the release is in flight
            try { await stopping.WaitAsync(token); }
            catch (OperationCanceledException) when (application.IsCancellationRequested) { }
            scopes.Finish.TrySetResult();     // the release returns, and faults
            await Task.Delay(300, token);
            await service.DisposeAsync();
            Require(log.Errors.Count == 0 && log.WarningErrors.Count(error => ReferenceEquals(error, scopes.Fault)) == 1 &&
                log.Warnings.Count(warning => warning.Contains("after the deadline killed its drain", StringComparison.Ordinal)) == 1,
                $"a fault from an operation the kill interrupted is logged once at Warning, with no Error ({log.Errors.Count} Error, {log.Warnings.Count} Warning: {string.Join(" | ", log.Warnings)})");
        }

        PostgresPreviewHostedService InMemoryService(string name, RecordingLogger log,
            Func<Kernel.Durable.InMemoryStore, Action<Exception>, (RuntimeNode Node, Kernel.Runtime.IScopeHost Host)> build,
            Func<Kernel.Durable.InMemoryStore, RuntimeNode, Task> started) =>
            new(Open, name, new WaitingActor(false), _ => { }, InMemoryLoop, log)
            {
                StartNode = async (report, cancellationToken) =>
                {
                    var store = new Kernel.Durable.InMemoryStore(1, () => Environment.TickCount64);
                    var (node, host) = build(store, report);
                    await node.StartAsync(host, cancellationToken);
                    while (!node.Runtime.Owns(0)) await Task.Delay(10, cancellationToken);
                    await started(store, node);
                    return node;
                }
            };

        // The node reports a loop error again once its window has passed, with the repeats it suppressed, also when the
        // loop rethrows the same instance (an awaited faulted task): the service logs each report. An aggregate with no
        // inner exceptions is logged as itself.
        void CheckLoopErrorRepeat()
        {
            var log = new RecordingLogger();
            var service = new PostgresPreviewHostedService(Open, "loop-error-repeat", new WaitingActor(false), Environment.FailFast, logger: log);
            var repeated = new InvalidOperationException("injected loop error, rethrown as the same instance");
            service.NodeFault(repeated);
            repeated.Data["SuppressedRepeats"] = 3L;     // as the node sets it on the report after the window
            service.NodeFault(repeated);
            var messages = log.ErrorMessages;
            Require(log.Errors.Count == 2 && log.Errors.All(error => ReferenceEquals(error, repeated)) && messages[^1].Contains("(3 earlier repeats", StringComparison.Ordinal),
                $"a loop error the node reports again as the same instance is logged again, with its suppressed count ({log.Errors.Count} Error entr{(log.Errors.Count == 1 ? "y" : "ies")}{(messages.Count > 0 ? ": " + messages[^1] : "")})");
            var empty = new AggregateException();
            service.NodeFault(empty);
            Require(log.Errors.Count == 3 && ReferenceEquals(log.Errors[^1], empty), "an aggregate with no inner exceptions is logged as itself");
        }

        // An aggregate shutdown fault is logged as its inner faults, one Error each, never as the aggregate as well.
        async Task CheckAggregateOnceAsync()
        {
            var first = new InvalidOperationException("injected first shutdown fault");
            var second = new InvalidOperationException("injected second shutdown fault");
            var log = new RecordingLogger();
            var service = new PostgresPreviewHostedService(Open, "aggregate-once", new WaitingActor(false), Environment.FailFast,
                new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(25) }, log) { AfterNodeStop = () => throw new AggregateException(first, second) };
            await service.StartAsync(token);
            try { await service.StopAsync(token); }
            catch (Exception) when (!token.IsCancellationRequested) { }
            await service.DisposeAsync();
            await Task.Delay(200, token);
            var errors = log.Errors;
            Require(errors.Count == 2 && errors.Count(error => ReferenceEquals(error, first)) == 1 && errors.Count(error => ReferenceEquals(error, second)) == 1,
                $"an aggregate shutdown fault logs one Error per distinct inner fault and none for the aggregate ({string.Join(", ", errors.Select(error => error?.Message ?? "?"))})");
        }

        // A StopAsync that joins another caller's stop kills nothing when its own deadline fires: no Warning. A later
        // dispose deadline that does kill the drain logs the one Warning.
        async Task CheckJoinedStopWarningAsync()
        {
            const string name = "joined-stop-warning";
            var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(new(WaitingActor.Name, "fixture")),
                name + "-start", Contract.Kinds.Start, cancellationToken: token);
            Require(admitted.Outcome == Contract.AdmissionOutcome.Accepted, $"the {name} probe admits an independent root");
            var held = new WaitingActor();
            var log = new RecordingLogger();
            var service = new PostgresPreviewHostedService(Open, name, held, _ => { },
                new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(25), DisposeDeadline = TimeSpan.FromMilliseconds(500) }, log);
            using var application = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Task? first = null;
            try
            {
                await service.StartAsync(token);
                await held.Entered.Task.WaitAsync(token);
                first = service.StopAsync(application.Token);
                using var joined = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                try { await service.StopAsync(joined.Token).WaitAsync(token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                var afterJoined = log.Warnings.Count;
                await service.DisposeAsync().AsTask().WaitAsync(token);
                Require(afterJoined == 0 && log.Warnings.Count == 1 && log.Errors.Count == 0,
                    $"a joined stop's deadline warns nothing, and the dispose deadline that kills the drain warns once ({afterJoined} then {log.Warnings.Count} Warning)");
            }
            finally
            {
                held.Release.TrySetResult();
                if (first is not null)
                    try { await first.WaitAsync(token); }
                    catch (Exception) when (!token.IsCancellationRequested) { }
            }
        }

        // A fault the shutdown raises outside the node is kept on LastError and logged once at Error.
        async Task CheckShutdownFaultAsync()
        {
            var fault = new InvalidOperationException("injected shutdown fault outside the node");
            var log = new RecordingLogger();
            var service = new PostgresPreviewHostedService(Open, "shutdown-fault", new WaitingActor(false), Environment.FailFast,
                new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(25) }, log) { AfterNodeStop = () => throw fault };
            await service.StartAsync(token);
            Exception? reported = null;
            try { await service.StopAsync(token); }
            catch (Exception error) when (!token.IsCancellationRequested) { reported = error; }
            await Task.Delay(200, token);
            var errors = log.Errors;
            Require(ReferenceEquals(reported, fault) && ReferenceEquals(service.LastError, fault) && errors.Count == 1 && ReferenceEquals(errors[0], fault),
                $"a shutdown fault outside the node is thrown by StopAsync, kept on LastError and logged once at Error ({errors.Count} Error entr{(errors.Count == 1 ? "y" : "ies")})");
        }

        // A negative dispose deadline is refused when the hosted service is made.
        void CheckNegativeDisposeDeadline()
        {
            Exception? refused = null;
            try { _ = new PostgresPreviewHostedService(Open, "negative-deadline", new WaitingActor(false), Environment.FailFast,
                new LoopOptions { DisposeDeadline = TimeSpan.FromSeconds(-5) }); }
            catch (Exception error) { refused = error; }
            Require(refused is ArgumentOutOfRangeException, $"a negative dispose deadline is refused when the hosted service is made ({refused?.GetType().Name ?? "accepted"})");
        }

        // A dispose never throws the stop's failure: the node records it, whoever waits, and a second dispose is quiet too.
        async Task CheckDisposeReportsOnceAsync()
        {
            const string name = "dispose-report";
            var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(new(WaitingActor.Name, "fixture")),
                name + "-start", Contract.Kinds.Start, cancellationToken: token);
            Require(admitted.Outcome == Contract.AdmissionOutcome.Accepted, $"the {name} probe admits an independent root");
            var held = new WaitingActor();
            var log = new RecordingLogger();
            var service = new PostgresPreviewHostedService(Open, name, held, Environment.FailFast,
                new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(25), DisposeDeadline = TimeSpan.FromSeconds(30) }, log);
            Exception? reported = null, again = null;
            try
            {
                await service.StartAsync(token);
                await held.Entered.Task.WaitAsync(token);
                var first = service.DisposeAsync().AsTask();
                await Task.Delay(300, token);
                // A sibling takes the partition while the entered turn drains: the drain loses its fence.
                await observer.Database.ExecuteSqlRawAsync("UPDATE \"Partitions\" SET \"Owner\" = 'dispose-thief', \"Epoch\" = \"Epoch\" + 1", token);
                try { await first.WaitAsync(token); }
                catch (Exception error) when (!token.IsCancellationRequested) { reported = error; }
                try { await service.DisposeAsync().AsTask().WaitAsync(token); }
                catch (Exception error) when (!token.IsCancellationRequested) { again = error; }
            }
            finally { held.Release.TrySetResult(); }
            var recorded = service.LastError;
            await Task.Delay(200, token);
            var errors = log.Errors;
            Require(reported is null && again is null && recorded is Kernel.Runtime.ScopeOwnershipLostException &&
                errors.Count == 1 && errors[0] is Kernel.Runtime.ScopeOwnershipLostException,
                $"a dispose stays quiet about the lost fence, which the service records and logs once at Error ({reported?.GetType().Name ?? "quiet"}, then {again?.GetType().Name ?? "quiet"}, recorded {recorded?.GetType().Name ?? "nothing"}, {errors.Count} Error entr{(errors.Count == 1 ? "y" : "ies")})");
        }

        // A dispose that joins a stop already under way kills it at the dispose deadline: no escalation, fence left for expiry.
        async Task CheckJoinedDisposeKillsAsync()
        {
            const string name = "dispose-joins";
            var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(new(WaitingActor.Name, "fixture")),
                name + "-start", Contract.Kinds.Start, cancellationToken: token);
            Require(admitted.Outcome == Contract.AdmissionOutcome.Accepted, $"the {name} probe admits an independent root");
            var held = new WaitingActor();
            var escalation = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new PostgresPreviewHostedService(Open, name, held, reason => escalation.TrySetResult(reason),
                new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(25), DisposeDeadline = TimeSpan.FromMilliseconds(500) });
            using var application = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Task? stopping = null;
            try
            {
                await service.StartAsync(token);
                await held.Entered.Task.WaitAsync(token);
                stopping = service.StopAsync(application.Token);     // a stop with a distant deadline is under way
                await Task.Delay(300, token);
                await service.DisposeAsync().AsTask().WaitAsync(token);
                await Task.Delay(500, token);
                Require(!escalation.Task.IsCompleted && stopping.IsCompleted && (await ReadFenceAsync()).Owner == name && !held.Release.Task.IsCompleted,
                    "a dispose that joins a stop under way kills it at its deadline without escalation, and leaves the fence for expiry");
            }
            finally
            {
                held.Release.TrySetResult();
                if (stopping is not null)
                    try { await stopping.WaitAsync(token); }
                    catch (Exception) when (!token.IsCancellationRequested) { }
            }
        }

        // Dispose without StopAsync (a later hosted service failed to start) is bounded by the dispose deadline, which kills
        // the stop. A stop that the kill ends promptly does not escalate; one that stays stuck past the grace does.
        async Task CheckDisposeDeadlineAsync(bool stuck)
        {
            var name = stuck ? "dispose-stuck" : "dispose-prompt";
            var escalation = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loop = new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(25), DisposeDeadline = TimeSpan.FromMilliseconds(500) };
            if (stuck)
            {
                // A preflight that stays blocked keeps the killed stop from finishing: past the grace, the policy runs.
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var service = new PostgresPreviewHostedService(() =>
                {
                    entered.TrySetResult();
                    release.Task.GetAwaiter().GetResult();
                    return Open();
                }, name, new WaitingActor(false), reason => escalation.TrySetResult(reason), loop);
                var starting = service.StartAsync(token);
                try
                {
                    await entered.Task.WaitAsync(token);
                    await service.DisposeAsync().AsTask().WaitAsync(token);
                    Require(escalation.Task.IsCompleted && (await escalation.Task).Contains("dispose deadline", StringComparison.Ordinal),
                        "a dispose whose killed stop stays blocked past the grace runs the process policy");
                }
                finally { release.TrySetResult(); }
                try { await starting.WaitAsync(token); }
                catch (Exception) when (!token.IsCancellationRequested) { }
                return;
            }
            var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(new(WaitingActor.Name, "fixture")),
                name + "-start", Contract.Kinds.Start, cancellationToken: token);
            Require(admitted.Outcome == Contract.AdmissionOutcome.Accepted, $"the {name} probe admits an independent root");
            var held = new WaitingActor();
            var log = new RecordingLogger();
            var prompt = new PostgresPreviewHostedService(Open, name, held, reason => escalation.TrySetResult(reason), loop, log);
            try
            {
                await prompt.StartAsync(token);
                await held.Entered.Task.WaitAsync(token);     // after an earlier probe's fence has run out, if it left one
                await prompt.DisposeAsync().AsTask().WaitAsync(token);
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
                Require(!escalation.Task.IsCompleted && (await ReadFenceAsync()).Owner == name && !held.Release.Task.IsCompleted &&
                    log.Warnings.Count == 1 && log.Warnings[0].Contains("lease expiry", StringComparison.Ordinal) && log.Errors.Count == 0 && prompt.LastError is null,
                    $"a dispose whose deadline kill ends the stop promptly does not run the process policy, leaves the fence for expiry, and logs one Warning ({log.Warnings.Count} Warning, {log.Errors.Count} Error)");
            }
            finally { held.Release.TrySetResult(); }
        }

        async Task CheckTurnDeadlineAsync()
        {
            var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(new(WaitingActor.Name, "fixture")),
                "deadline-start", Contract.Kinds.Start, cancellationToken: token);
            Require(admitted.Outcome == Contract.AdmissionOutcome.Accepted, "the deadline probe admits an independent root");
            var held = new WaitingActor();
            var escalation = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var host = Build("lifetime-deadline", held, reason => escalation.TrySetResult(reason));
            try
            {
                await host.StartAsync(token);
                await held.Entered.Task.WaitAsync(token);
                await ExpectDeadlineAsync(host.StopAsync, escalation, escalates: false);
                Require(!held.Release.Task.IsCompleted && (await ReadFenceAsync()).Owner == "lifetime-deadline",
                    "a host deadline kills the stop, which ends promptly without escalation, and the entered turn keeps its fence for expiry");
            }
            finally { held.Release.TrySetResult(); }
        }

        async Task CheckStartupDeadlineAsync()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var escalation = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var startupLog = new RecordingLogger();
            await using var service = new PostgresPreviewHostedService(() =>
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                return Open();
            }, "lifetime-startup", new WaitingActor(false), reason => escalation.TrySetResult(reason), logger: startupLog);
            var starting = service.StartAsync(token);
            try
            {
                await entered.Task.WaitAsync(token);
                await ExpectDeadlineAsync(service.StopAsync, escalation, escalates: true);
                Require(!starting.IsCompleted && startupLog.Warnings.Count == 0,
                    $"a host deadline requests escalation while preflight is still blocked, and warns of no kill: nothing was owned ({startupLog.Warnings.Count} Warning)");
            }
            finally { release.TrySetResult(); }
            try
            {
                await starting.WaitAsync(token);
                throw new InvalidOperationException("Cancelled preflight started a node.");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        }

        async Task CheckStartupRetryAsync()
        {
            var attempts = 0;
            await using var service = new PostgresPreviewHostedService(() =>
            {
                if (Interlocked.Increment(ref attempts) == 1) throw new IOException("Fixture startup failure.");
                return Open();
            }, "lifetime-retry", new WaitingActor(false), Environment.FailFast);
            try
            {
                await service.StartAsync(token);
                throw new InvalidOperationException("The initial startup failure was hidden.");
            }
            catch (IOException error) when (error.Message == "Fixture startup failure.") { }
            await service.StartAsync(token);
            await service.StopAsync(token);
            Require(attempts > 1, "a failed startup can retry with a healthy database context");
        }

        async Task ExpectDeadlineAsync(Func<CancellationToken, Task> stop, TaskCompletionSource<string> escalation, bool escalates)
        {
            using var shutdown = new CancellationTokenSource();
            var stopping = stop(shutdown.Token);
            shutdown.Cancel();
            try
            {
                await stopping.WaitAsync(token);
                throw new InvalidOperationException("Application shutdown accepted an expired deadline.");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            if (escalates)
                Require((await escalation.Task.WaitAsync(token)).Contains("shutdown deadline", StringComparison.Ordinal),
                    "the supplied process policy receives the shutdown deadline");
            else
            {
                await Task.Delay(PostgresPreviewHostedService.KillGrace + TimeSpan.FromMilliseconds(500), token);
                Require(!escalation.Task.IsCompleted, "the process policy does not run when the deadline's kill ends the stop within its grace");
            }
        }

        IHost Build(string node, WaitingActor actor, Action<string>? endProcess = null)
        {
            var builder = Host.CreateEmptyApplicationBuilder(new());
            builder.Logging.ClearProviders();
            builder.Services.AddHostedService(_ => new PostgresPreviewHostedService(Open, node, actor, endProcess ?? Environment.FailFast,
                new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(25) }));
            return builder.Build();
        }

        Task<PartitionEntity> ReadFenceAsync() => observer.Partitions.AsNoTracking().SingleAsync(token);
        ValueTask<Contract.Admission> SendCompleteAsync() =>
            ingress.SendAsync(scope, new Contract.Target.Ref(reference), "complete", "complete", cancellationToken: token);
    }

    /// <summary>A logger that keeps the exceptions of its Error entries.</summary>
    private static readonly LoopOptions InMemoryLoop = new()
    {
        BalanceEvery = TimeSpan.FromMilliseconds(20), Window = TimeSpan.FromMilliseconds(100), PollInterval = TimeSpan.FromMilliseconds(20)
    };

    private static readonly Kernel.Durable.WorkerOptions InMemoryWorkers = new() { Partitions = 1, JobScheduler = TaskScheduler.Default };

    private static async Task StartInMemoryActorAsync(Kernel.Durable.InMemoryStore store, RuntimeNode node, string type)
    {
        var ingress = new Kernel.Foundation.InMemory.InMemoryIngress(store, new Kernel.Durable.Requests(node.Node), node.Node);
        var receipt = await ingress.SendAsync(Elsa.ActorFoundation.TestSupport.ContractScopes.Bind(node.Node),
            new Contract.Target.Start(new(type, "held", History: Contract.History.Full)), "start", Contract.Kinds.Start);
        if (receipt.Outcome != Contract.AdmissionOutcome.Accepted) throw new InvalidOperationException("The held job actor must start.");
    }

    /// <summary>An actor whose start issues one job that waits for <see cref="Finish"/>, ignoring cancellation.</summary>
    private sealed class HeldJobActor : Contract.IActorType
    {
        internal const string Name = "held-job-actor";
        public string Type => Name;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Inbox.Any(message => message.Kind == Contract.Kinds.Start))
            {
                turn.State = Kernel.Durable.Json.Write("waiting");
                turn.StartJob("held");
            }
            return default;
        }

        public async ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Finish.Task;
            return "done";
        }
    }

    /// <summary>A host whose one scope's give-up waits for <see cref="Finish"/> and then throws <see cref="Fault"/>.</summary>
    private sealed class SlowReleaseScopes : Kernel.Runtime.IScopeHost
    {
        internal InvalidOperationException Fault { get; } = new("injected release fault after the kill");
        internal TaskCompletionSource Releasing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<Kernel.Runtime.IScopeActivation?> ActivateAsync(int scope, CancellationToken cancellationToken = default) =>
            new(new SlowScope(this, scope));

        private sealed class SlowScope(SlowReleaseScopes host, int scope) : Kernel.Runtime.IScopeActivation
        {
            public int Scope => scope;
            public bool Active { get; private set; } = true;
            public ValueTask WokenAsync(Kernel.Runtime.InstanceRef? actor, CancellationToken cancellationToken = default) => default;
            public ValueTask<TimeSpan?> RunAsync(CancellationToken cancellationToken = default) => new(TimeSpan.FromMilliseconds(50));
            public async ValueTask DisposeAsync()
            {
                host.Releasing.TrySetResult();
                await host.Finish.Task;
                Active = false;
                throw host.Fault;
            }
        }
    }

    private sealed class RecordingLogger : ILogger<PostgresPreviewHostedService>
    {
        private readonly List<Exception?> _errors = [];
        private readonly List<string> _warnings = [], _errorMessages = [];
        private readonly List<Exception?> _warningErrors = [];
        internal IReadOnlyList<Exception?> WarningErrors { get { lock (_errors) return [.. _warningErrors]; } }
        internal IReadOnlyList<Exception?> Errors { get { lock (_errors) return [.. _errors]; } }
        internal IReadOnlyList<string> ErrorMessages { get { lock (_errors) return [.. _errorMessages]; } }
        internal IReadOnlyList<string> Warnings { get { lock (_errors) return [.. _warnings]; } }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_errors)
                if (logLevel == LogLevel.Error)
                {
                    _errors.Add(exception);
                    _errorMessages.Add(formatter(state, exception));
                }
                else if (logLevel == LogLevel.Warning)
                {
                    _warnings.Add(formatter(state, exception));
                    _warningErrors.Add(exception);
                }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }

    private sealed class WaitingActor(bool acceptStart = true) : Contract.IActorType
    {
        internal const string Name = "lifetime-actor";
        public string Type => Name;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SiblingEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSibling { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Turns { get; private set; }

        public bool Withholds(Contract.ActorHeader header) => false;

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            Turns++;
            if (turn.StartSettings is not null)
            {
                if (!acceptStart) throw new InvalidOperationException("The successor received a duplicate creator turn.");
                var sibling = turn.Header.TypeData == "sibling";
                (sibling ? SiblingEntered : Entered).TrySetResult();
                await (sibling ? ReleaseSibling : Release).Task.WaitAsync(cancellationToken);
                turn.State = WaitingState;
                return;
            }
            if (turn.State != WaitingState || turn.Inbox is not [{ Kind: "complete" }])
                throw new InvalidOperationException("The continuation requires the previous host's checkpoint.");
            turn.End(Contract.ActorStatus.Finished);
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => throw new NotSupportedException();
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}

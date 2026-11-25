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
        string[] probes = ["turn-deadline", "startup-deadline", "startup-retry", "dispose-prompt", "dispose-stuck", "dispose-report", "dispose-joins", "negative-deadline"];
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
        return 0;

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
            var service = new PostgresPreviewHostedService(Open, name, held, Environment.FailFast,
                new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(25), DisposeDeadline = TimeSpan.FromSeconds(30) });
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
            Require(reported is null && again is null && recorded is Kernel.Runtime.ScopeOwnershipLostException,
                $"a dispose stays quiet about the lost fence, which the service records ({reported?.GetType().Name ?? "quiet"}, then {again?.GetType().Name ?? "quiet"}, recorded {recorded?.GetType().Name ?? "nothing"})");
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
            var prompt = new PostgresPreviewHostedService(Open, name, held, reason => escalation.TrySetResult(reason), loop);
            try
            {
                await prompt.StartAsync(token);
                await held.Entered.Task.WaitAsync(token);     // after an earlier probe's fence has run out, if it left one
                await prompt.DisposeAsync().AsTask().WaitAsync(token);
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
                Require(!escalation.Task.IsCompleted && (await ReadFenceAsync()).Owner == name && !held.Release.Task.IsCompleted,
                    "a dispose whose deadline kill ends the stop promptly does not run the process policy, and leaves the fence for expiry");
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
            await using var service = new PostgresPreviewHostedService(() =>
            {
                entered.TrySetResult();
                release.Task.GetAwaiter().GetResult();
                return Open();
            }, "lifetime-startup", new WaitingActor(false), reason => escalation.TrySetResult(reason));
            var starting = service.StartAsync(token);
            try
            {
                await entered.Task.WaitAsync(token);
                await ExpectDeadlineAsync(service.StopAsync, escalation, escalates: true);
                Require(!starting.IsCompleted, "a host deadline requests escalation while preflight is still blocked");
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

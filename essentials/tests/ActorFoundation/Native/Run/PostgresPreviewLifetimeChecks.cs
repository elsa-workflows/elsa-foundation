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
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
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
        await CheckTurnDeadlineAsync();
        await CheckStartupDeadlineAsync();
        await CheckStartupRetryAsync();
        return 0;

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
                await ExpectDeadlineAsync(host.StopAsync, escalation);
                Require(!held.Release.Task.IsCompleted && (await ReadFenceAsync()).Owner == "lifetime-deadline",
                    "a host deadline requests process escalation while the entered turn still owns its fence");
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
                await ExpectDeadlineAsync(service.StopAsync, escalation);
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

        async Task ExpectDeadlineAsync(Func<CancellationToken, Task> stop, TaskCompletionSource<string> escalation)
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
            Require((await escalation.Task.WaitAsync(token)).Contains("shutdown deadline", StringComparison.Ordinal),
                "the supplied process policy receives the shutdown deadline");
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

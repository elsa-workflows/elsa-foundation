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
        var scope = FixedTenantScope.Bind("host-lifetime");
        var ingress = await store.OpenPreviewRootIngressAsync(WaitingActor.Name, TimeSpan.FromHours(1), token);
        var reads = await store.OpenPreviewRootReadsAsync(cancellationToken: token);
        var start = await ingress.SendAsync(scope, new Contract.Target.Start(new(WaitingActor.Name, "fixture")),
            "start", Contract.Kinds.Start, cancellationToken: token);
        Require(start is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the first root is admitted");
        var reference = start.Actor!.Value;
        var initial = new WaitingActor();
        using (var host = Build("lifetime-first", initial))
        {
            Task? stopping = null;
            try
            {
                await host.StartAsync(token);
                await initial.Entered.Task.WaitAsync(token);
                stopping = host.StopAsync(token);
                var owner = await ReadFenceAsync();
                Require(!stopping.IsCompleted && owner.Owner == "lifetime-first" &&
                    await reads.GetAsync(scope, reference, token) is null, "application stop waits while the entered turn still owns its fence");
                initial.Release.TrySetResult();
                await stopping;
                var after = await ReadFenceAsync();
                Require(after.Owner is null && after.Epoch == owner.Epoch &&
                    await store.ReadFirstRootAsync(scope.Tenant, reference.Id, token) is { Revision: 1, State: WaitingState },
                    "the entered turn commits before the application gives up ownership");
            }
            finally
            {
                initial.Release.TrySetResult();
                if (stopping is not null) await stopping;
            }
        }

        var prefix = await reads.TailAsync(scope, reference, cancellationToken: token);
        var firstFence = await ReadFenceAsync();
        var fresh = new WaitingActor();
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
        return 0;

        IHost Build(string node, WaitingActor actor)
        {
            var builder = Host.CreateEmptyApplicationBuilder(new());
            builder.Logging.ClearProviders();
            builder.Services.AddHostedService(_ => new PostgresPreviewHostedService(Open, node, actor, Environment.FailFast,
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

    private sealed class WaitingActor : Contract.IActorType
    {
        internal const string Name = "lifetime-actor";
        public string Type => Name;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Turns { get; private set; }

        public bool Withholds(Contract.ActorHeader header) => false;

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            Turns++;
            if (turn.StartSettings is not null)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
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

using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class RootAdmissionPressureChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool inBatch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var directory = Directory.CreateTempSubdirectory("root-admission-pressure-");
        var path = Path.Combine(directory.FullName, "pressure.db");
        try
        {
            var options = new DbContextOptionsBuilder<KernelDb>();
            var definitions = new SqliteAdmissionDefinitions();
            if (postgres)
                options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                    ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database."));
            else
                options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString());

            KernelDb Open() => KernelDb.ForRootDueBacklogQualification(options.Options, postgres ? null : definitions);
            var store = new EfStore(Open, postgres);
            await using var db = Open();
            if (postgres)
            {
                Check(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the PostgreSQL fixture is empty");
                await store.CreateAsync(1);
                await store.InstallPreviewRootSchemaAsync(token);
            }
            else
                await EfStore.InstallSqliteShippedAsync(db, 1, token);

            const string node = "pressure-node";
            var scope = ContractScopes.Bind("pressure-probe");
            var other = ContractScopes.Bind("separate-tenant");
            var actor = new PressureActor();
            var rateClock = new RateClock();
            var pressure = new RootAdmissionPressure(
                new(2, 1, TimeSpan.FromMilliseconds(75), TimeSpan.FromMilliseconds(100)),
                ordinaryCapacity: 1, operatorCapacity: 1, ordinaryRate: new(1, 3), operatorRate: new(1, 2), rateClock);
            var loops = new LoopOptions
            {
                BalanceEvery = TimeSpan.FromMilliseconds(150), HeartbeatEvery = TimeSpan.FromMilliseconds(150),
                PollInterval = TimeSpan.FromMilliseconds(30)
            };
            void Fatal(string cause) => throw new InvalidOperationException(cause);
            bool Authorize(Contract.BoundScope caller, Contract.ActorRef _, string kind) =>
                ReferenceEquals(caller, scope) && kind == Contract.Kinds.Terminate;

            Contract.IIngress ingress;
            Contract.IActorReads reads;
            RuntimeNode owner;
            if (postgres)
            {
                var requests = PostgresPreviewHosting.CreateRequests(Open, node, actor.Type, deduplicationWindow: null, pressure);
                var foundation = await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(Open, actor.Type, Authorize, requests,
                    cancellationToken: token);
                ingress = foundation;
                reads = foundation;
                owner = await PostgresPreviewHosting.StartAsync(Open, node, actor, Fatal, loops, token, null, requests, fenceInBatch: inBatch);
            }
            else
            {
                var requests = SqliteEmbeddedHosting.CreateRequests(path, node, actor.Type, pressure: pressure, qualifyPressure: true);
                ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(path, actor.Type, Authorize, requests, token);
                reads = new GeneratedRootReads(store, await SqliteRootAdmission.ReadSelectionCursorAsync(db, token), 8, provider: "sqlite");
                owner = await SqliteEmbeddedHosting.StartFoundationAsync(path, node, actor, Fatal, loops, inBatch, token, requests: requests);
            }

            await using var owned = owner;
            var blocker = await StartAsync(scope, "withhold", "hold");
            var live = await StartAsync(scope, "live", "serve");
            var blocked = new Contract.Target.Ref(blocker);
            var target = new Contract.Target.Ref(live);
            await UntilAsync(async () => (await reads.GetAsync(scope, blocker, token))?.Selection is not null &&
                (await reads.GetAsync(scope, live, token))?.Selection is not null);
            for (var i = 0; i < 3; i++)
                Check(await ingress.SendAsync(scope, blocked, "held-" + i, "work", cancellationToken: token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "withheld ingress enters the due backlog");

            await UntilAsync(() => Task.FromResult(IsFull()));
            Check(await ingress.SendAsync(scope, blocked, "held-0", "work", cancellationToken: token) is
                    { Outcome: Contract.AdmissionOutcome.Duplicate } &&
                await ingress.SendAsync(scope, Start("hold"), "withhold", Contract.Kinds.Start, cancellationToken: token) is
                    { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: { } replay } && replay == blocker,
                "live receipts win while the partition is full");
            Check(await ingress.SendAsync(scope, target, "retry-direct", "work", cancellationToken: token) is
                { Reason: Contract.RefusalReason.InboxFull, RetryAfter: not null }, "a new exact send receives InboxFull");
            Check(await ingress.SendAsync(other, target, "other-tenant", "work", cancellationToken: token) is
                { Reason: Contract.RefusalReason.TargetMismatch }, "pressure does not expose another tenant's exact reference");

            var deadline = DateTimeOffset.UtcNow.AddSeconds(10).ToUnixTimeMilliseconds();
            Check(await ingress.RequestAsync(scope, Start("serve"), "retry-start", Contract.Kinds.Start, null, null,
                new(null, deadline), token) is { RefusalReason: Contract.RefusalReason.InboxFull, RetryAfter: not null, Actor: null },
                "held starts preserve the structured pressure refusal");
            using (pressure.Begin(scope.Tenant, RootAdmissionKind.Input))
            {
                Check(await ingress.SendAsync(scope, target, "busy", "work", cancellationToken: token) is
                    { Reason: Contract.RefusalReason.NodeBusy, RetryAfter: not null }, "ordinary in-flight saturation returns NodeBusy");
                var view = (await reads.GetAsync(scope, blocker, token))!;
                var command = new Contract.OperatorCommand(view.Selection, ExpectedTarget: blocker);
                Check(await ingress.SendAsync(scope, blocked, "relieve", Contract.Kinds.Terminate, Json.Write(command), cancellationToken: token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "selected Terminate uses reserved capacity to shed work");
            }

            await UntilAsync(async () => (await reads.GetAsync(scope, blocker, token))?.Header.Status == Contract.ActorStatus.Cancelled &&
                !IsFull());
            Check(await ingress.SendAsync(scope, target, "retry-direct", "work", cancellationToken: token) is
                { Reason: Contract.RefusalReason.RateLimited }, "the independent tenant Input bucket still applies after relief");
            var retried = await StartAsync(scope, "retry-start", "serve");
            Check(retried != blocker && retried != live, "the refused held start left its operation ID reusable");
            Check(await ingress.SendAsync(scope, Start("serve"), "rate-limit", Contract.Kinds.Start, cancellationToken: token) is
                { Reason: Contract.RefusalReason.RateLimited, RetryAfter: { } retry } && retry > TimeSpan.Zero,
                "exhausting the start bucket returns RateLimited and RetryAfter");
            Check(await StartAsync(other, "other-tenant-start", "serve") != retried, "another tenant has a separate bucket");
            rateClock.Advance(TimeSpan.FromSeconds(2));
            Check(await ingress.SendAsync(scope, target, "retry-direct", "work", cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the refused exact send left its operation ID reusable after relief");
            await owner.StopAsync(token);
            return 0;

            Contract.Target.Start Start(string typeData) => new(new(actor.Type, TypeData: typeData));

            async Task<Contract.ActorRef> StartAsync(Contract.BoundScope tenant, string id, string typeData)
            {
                var accepted = await ingress.SendAsync(tenant, Start(typeData), id, Contract.Kinds.Start, cancellationToken: token);
                if (accepted is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } reference })
                    throw new InvalidOperationException($"Expected an accepted start, got {accepted}.");
                return reference;
            }

            bool IsFull()
            {
                using var observation = pressure.Begin(scope.Tenant, RootAdmissionKind.Input);
                try { observation.Require(0); }
                catch (RootAdmissionRefused refusal)
                {
                    return refusal.Admission.Reason == Contract.RefusalReason.InboxFull;
                }
                return false;
            }

            async Task UntilAsync(Func<Task<bool>> condition)
            {
                while (!await condition()) await Task.Delay(25, token);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class RateClock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        internal void Advance(TimeSpan time) => Interlocked.Add(ref ticks, time.Ticks);
    }

    private sealed class PressureActor : Contract.IActorType
    {
        public string Type => "admission-pressure-probe";
        public bool Withholds(Contract.ActorHeader header) => header.TypeData == "hold";
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => throw new NotSupportedException();
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.Inbox is [{ Kind: Contract.Kinds.Terminate }]) turn.End(Contract.ActorStatus.Cancelled);
            else turn.State = "\"ready\"";
            return ValueTask.CompletedTask;
        }
    }
}

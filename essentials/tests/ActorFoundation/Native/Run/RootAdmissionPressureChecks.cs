using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class RootAdmissionPressureChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool inBatch)
    {
        CheckRollbackDuringRefill(TimeSpan.FromSeconds(2), refresh: true);
        CheckRollbackDuringRefill(TimeSpan.FromMilliseconds(500), refresh: false);
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

            var actor = new PressureActor();
            var eventHost = new RootPublicEventHost(actor.Type, false);
            KernelDb Open() => postgres ? KernelDb.ForPostgresRootShipped(options.Options, eventHost)
                : KernelDb.ForSqliteRootRuntime(options.Options, definitions, publicEventHost: eventHost);
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
                var requests = SqliteEmbeddedHosting.CreateRequests(path, node, actor.Type, pressure: pressure);
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
            if (postgres)
            {
                await using var blockedDb = Open();
                await using var blockerWrite = await blockedDb.Database.BeginTransactionAsync(token);
                await blockedDb.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT 1 FROM "Instances" WHERE "FoundationReference" = {live.Id} FOR UPDATE
                    """, token);
                var first = CallAsync(target, "stalled-send", "work");
                await CheckConcurrentBusyAsync(target, "busy-held-send", "work", first);
                await blockerWrite.RollbackAsync(token);
                Check(await first is { RefusalReason: Contract.RefusalReason.InboxFull }, "the first PostgreSQL caller observes pressure after its checkpoint is free");
            }
            else
            {
                await using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
                    { DataSource = path, ForeignKeys = true, Pooling = false }.ToString());
                await writer.OpenAsync(token);
                await using var blockerWrite = writer.BeginTransaction(deferred: false);
                var first = CallAsync(Start("serve"), "stalled-start", Contract.Kinds.Start);
                await CheckConcurrentBusyAsync(Start("serve"), "busy-held-start", Contract.Kinds.Start, first);
                var duplicate = await Task.Run(async () => await ingress.SendAsync(scope, Start("hold"), "withhold", Contract.Kinds.Start,
                    cancellationToken: token), token).WaitAsync(TimeSpan.FromSeconds(2), token);
                Check(duplicate is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: { } existing } && existing == blocker,
                    "an established start replays while the first SQLite held admission still waits for the writer");
                await blockerWrite.RollbackAsync(token);
                Check(await first is { RefusalReason: Contract.RefusalReason.InboxFull }, "the first SQLite caller observes pressure after the writer is free");
            }

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
            await db.Partitions.Where(partition => partition.Id == 0)
                .ExecuteUpdateAsync(update => update.SetProperty(partition => partition.Backlog, 10_001), token);
            Contract.IIngress standalone = postgres
                ? await PostgresPreviewHosting.OpenFoundationAsync(Open, actor.Type, cancellationToken: token)
                : await SqliteEmbeddedHosting.OpenFoundationAsync(path, actor.Type, cancellationToken: token);
            Check(await standalone.SendAsync(scope, Start("serve"), "cold-edge-start", Contract.Kinds.Start, cancellationToken: token) is
                    { Reason: Contract.RefusalReason.InboxFull, RetryAfter: not null } &&
                await standalone.SendAsync(scope, Start("serve"), "live", Contract.Kinds.Start, cancellationToken: token) is
                    { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: { } old } && old == live,
                "one-way ingress primes the published pressure list and still replays existing starts");
            if (postgres)
            {
                var singleConnection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString())
                {
                    MaxPoolSize = 1,
                    Timeout = 2
                };
                var singleOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(singleConnection.ConnectionString).Options;
                var singleConnectionStore = new EfStore(OpenWithOwnedConnection, postgres: true);
                var preview = await singleConnectionStore.OpenPreviewRootIngressAsync(actor.Type, TimeSpan.FromHours(1), token);
                var selected = await singleConnectionStore.OpenSelectedRootIngressAsync(actor.Type, TimeSpan.FromHours(1),
                    (caller, target) => Authorize(caller, target, Contract.Kinds.Terminate), token);
                Check(await preview.SendAsync(scope, Start("serve"), "cold-preview-start", Contract.Kinds.Start, cancellationToken: token) is
                        { Reason: Contract.RefusalReason.InboxFull, RetryAfter: not null } &&
                    await selected.SendAsync(scope, Start("serve"), "cold-selected-start", Contract.Kinds.Start, cancellationToken: token) is
                        { Reason: Contract.RefusalReason.InboxFull, RetryAfter: not null },
                    "both low-level PostgreSQL factories apply shared pressure using one pooled connection");

                KernelDb OpenWithOwnedConnection()
                {
                    var context = KernelDb.ForPostgresRootShipped(singleOptions);
                    try
                    {
                        context.Database.OpenConnection();
                        return context;
                    }
                    catch
                    {
                        context.Dispose();
                        throw;
                    }
                }
            }
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

            Task<Contract.Reply> CallAsync(Contract.Target destination, string id, string kind) =>
                ingress.RequestAsync(scope, destination, id, kind, null, null,
                    new(null, DateTimeOffset.UtcNow.AddSeconds(10).ToUnixTimeMilliseconds()), token).AsTask();

            async Task CheckConcurrentBusyAsync(Contract.Target destination, string id, string kind, Task<Contract.Reply> first)
            {
                await UntilAsync(() =>
                {
                    if (first.IsCompleted) throw new InvalidOperationException("The first held admission must still be waiting.");
                    using var observe = pressure.Begin(scope.Tenant, RootAdmissionKind.Input);
                    return Task.FromResult(observe.IsBusy);
                });
                var refusal = await CallAsync(destination, id, kind).WaitAsync(TimeSpan.FromSeconds(2), token);
                Check(refusal is { RefusalReason: Contract.RefusalReason.NodeBusy, RetryAfter: not null } && !first.IsCompleted,
                    "a second held caller returns NodeBusy while the first admission waits on a physical database lock");
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

    private static void CheckRollbackDuringRefill(TimeSpan elapsed, bool refresh)
    {
        var clock = new RateClock();
        var pressure = new RootAdmissionPressure(new(2, 1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
            ordinaryCapacity: 2, operatorCapacity: 1, ordinaryRate: new(1, 2), operatorRate: new(1, 1), clock);
        using var stalled = pressure.Begin("one", RootAdmissionKind.Input);
        stalled.Require(0);
        clock.Advance(elapsed);
        if (refresh) pressure.Refresh(new Dictionary<int, int>(), nodes: 1);
        using (var admitted = pressure.Begin("one", RootAdmissionKind.Input))
        {
            admitted.Require(0);
            admitted.Committed();
        }
        stalled.Dispose();
        using (var third = pressure.Begin("one", RootAdmissionKind.Input))
        {
            third.Require(0);
            third.Committed();
        }
        clock.Advance(TimeSpan.FromMilliseconds(500));
        using var next = pressure.Begin("one", RootAdmissionKind.Input);
        try { next.Require(0); }
        catch (RootAdmissionRefused refusal) when (refusal.Admission.Reason == Contract.RefusalReason.RateLimited)
        {
            Console.WriteLine("ok a rollback does not mint a token after " + (refresh ? "full" : "partial") + " refill");
            return;
        }
        throw new InvalidOperationException("A rolled-back reservation incorrectly credited the bucket.");
    }

    internal sealed class RateClock : TimeProvider
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

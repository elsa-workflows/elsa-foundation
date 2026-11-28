using Elsa.ActorFoundation.Hosting;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class AuthoredStandaloneEventOutboxChecks
{
    private const string Node = "authored-standalone-event-owner";
    private const string Key = "event:standalone-bridge";
    private const string First = """{"order":42}""";
    private static readonly string Second = "[\"raw\",\"x\"]" + '\0';
    private static readonly Contract.BoundScope Scope = EmbeddedTenant.Bind("published");
    private static readonly Contract.BoundScope Foreign = ContractScopes.Bind("foreign");
    private static readonly Contract.BoundScope Isolated = EmbeddedTenant.Isolate(Scope, "test-run");

    internal static async Task<int> RunAsync(bool postgres, bool fenceInBatch)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(65));
        var token = deadline.Token;
        var actor = new Publisher();
        var loop = new LoopOptions { PollInterval = TimeSpan.FromMilliseconds(20) };
        if (postgres)
        {
            var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                ?? throw new InvalidOperationException("Supply a dedicated PostgreSQL qualification database.");
            var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
            KernelDb Open() => PostgresPreviewHosting.CreateAuthoredStandaloneEventQualificationContext(options, actor.Type, false, Scope);
            await using (var db = Open())
                Require(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the dedicated PostgreSQL database is empty");
            var store = new EfStore(Open, postgres: true);
            await PostgresPreviewHosting.InstallAuthoredStandaloneEventQualificationAsync(options, actor.Type, false, Scope, token);

            var requests = PostgresPreviewHosting.CreateRequests(Open, Node, actor.Type);
            var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, actor.Type, requests, cancellationToken: token);
            var hosted = new PostgresPreviewHostedService(Open, Node, actor, Refused, loop, requests: requests)
            {
                StartNode = (fault, ct) => PostgresPreviewHosting.StartAsync(Open, Node, actor, Refused, loop, ct,
                    fault, requests, fenceInBatch, _ => TimeSpan.Zero, new Kernel.WorkflowActor.ScheduleActor())
            };
            Contract.IAuthoredStandaloneEventOutbox Bind(Contract.BoundScope scope) => PostgresPreviewHosting.BindAuthoredStandaloneEvents(Open, scope);
            await using (hosted)
                await CheckAsync(hosted, foundation, foundation, Bind, token);
            KernelDb Stock() => PostgresPreviewHosting.CreateShippedContext(options, actor.Type, protectsStarts: false);
            await RequireRefusalAsync(() => PostgresPreviewHosting.OpenFoundationAsync(Stock, actor.Type, cancellationToken: token),
                "the shipped PostgreSQL format refuses a private event transfer store");
            await PostgresPreviewHosting.InstallAuthoredStandaloneEventQualificationAsync(options, actor.Type, false, Scope, token);
            await RequireRefusalAsync(() => PostgresPreviewHosting.InstallAuthoredStandaloneEventQualificationAsync(options,
                actor.Type, false, Foreign, token), "a reopened PostgreSQL install refuses another publisher scope");
            await using (var db = Open())
            {
                await db.Database.ExecuteSqlRawAsync("""
                    DROP INDEX "IX_FoundationAuthoredStandaloneEventTransfers_Page"
                    """, token);
                await RequireRefusalAsync(() => store.InstallPreviewRootSchemaAsync(token),
                    "the initialized PostgreSQL profile refuses transfer index drift");
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE INDEX "IX_FoundationAuthoredStandaloneEventTransfers_Page"
                    ON "FoundationAuthoredStandaloneEventTransfers" ("CommittedAt", "Occurrence")
                    """, token);
                await db.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE "FoundationAuthoredStandaloneEventTransfers" RENAME TO "PreviousAuthoredEventTransfers"
                    """, token);
                await RequireRefusalAsync(() => store.InstallPreviewRootSchemaAsync(token),
                    "the initialized PostgreSQL profile refuses removal of its transfer relation");
                Require(await db.Database.SqlQueryRaw<bool>("""
                        SELECT pg_catalog.to_regclass('"FoundationAuthoredStandaloneEventTransfers"') IS NULL AS "Value"
                        """).SingleAsync(token), "the PostgreSQL installer did not create an empty queue");
            }
        }
        else
        {
            var folder = Directory.CreateTempSubdirectory("authored-standalone-event");
            try
            {
                var path = Path.Combine(folder.FullName, "foundation.db");
                var foundation = SqliteEmbeddedFoundation.ForAuthoredStandaloneEventQualification(path, Node, actor.Type, Scope);
                await foundation.InstallShippedAsync(token);
                var hosted = foundation.CreateHostedServiceWithAuxiliaryActor(actor, new Kernel.WorkflowActor.ScheduleActor(), Refused,
                    _ => TimeSpan.Zero, loop, fenceInBatch);
                await using (hosted)
                    await CheckAsync(hosted, await foundation.OpenIngressAsync(token), foundation.CreateReads(), foundation.BindAuthoredStandaloneEvents, token);
                var reopened = SqliteEmbeddedFoundation.ForAuthoredStandaloneEventQualification(path, "cold-reader", actor.Type, Scope);
                await reopened.InstallShippedAsync(token);
                Require((await reopened.BindAuthoredStandaloneEvents(Scope).ReadAsync(cancellationToken: token)).Count == 0,
                    "a newly bound SQLite host sees acknowledgements after reopen");
                await RequireRefusalAsync(() => SqliteEmbeddedFoundation.ForAuthoredStandaloneEventQualification(path, Node, actor.Type, Foreign)
                    .InstallShippedAsync(token), "a reopened SQLite install refuses another publisher scope");
                await RequireRefusalAsync(() => new SqliteEmbeddedFoundation(path, Node, actor.Type).InstallShippedAsync(token),
                    "the shipped SQLite format refuses a private event transfer store");
            }
            finally { folder.Delete(recursive: true); }
        }
        return 0;
    }

    private static async Task CheckAsync(IHostedService owner, Contract.IIngress ingress, Contract.IActorReads reads,
        Func<Contract.BoundScope, Contract.IAuthoredStandaloneEventOutbox> bind, CancellationToken token)
    {
        var outbox = bind(Scope);
        var author = await StartAsync(Scope, "author");
        var foreign = await StartAsync(Foreign, "foreign-author", "author");
        var isolated = await StartAsync(Isolated, "test-author", "author");
        var local = await StartAsync(Scope, "local");
        var rollback = await StartAsync(Scope, "rollback");
        var outside = await ingress.SendAsync(Scope, new Contract.Target.Event(Key), "outside", "event", First, cancellationToken: token);
        Require(outside.Outcome == Contract.AdmissionOutcome.Accepted, "outside publication is accepted through its own ingress route");
        Require((await outbox.ReadAsync(cancellationToken: token)).Count == 0,
            "admitted creators and outside Event ingress do not produce authored transfers");

        await owner.StartAsync(token);
        IReadOnlyList<Contract.AuthoredStandaloneEvent> captured;
        try
        {
            await WaitForAsync(Scope, author, Contract.ActorStatus.Finished);
            await WaitForAsync(Scope, local, Contract.ActorStatus.Finished);
            await WaitForAsync(Scope, rollback, Contract.ActorStatus.Faulted);
            await WaitForAsync(Foreign, foreign, Contract.ActorStatus.Finished);
            await WaitForAsync(Isolated, isolated, Contract.ActorStatus.Finished);
            captured = await outbox.ReadAsync(cancellationToken: token);
            Require(captured.Count == 2 && captured.All(eventItem => eventItem.Publisher == author &&
                    eventItem.EventName == Key && eventItem.PublisherPartition >= 0 && eventItem.CommittedAt > 0) &&
                captured.Select(row => row.Occurrence).Distinct().Count() == 2 &&
                captured.Select(row => row.Payload).ToHashSet().SetEquals([First, Second]),
                "only the successful bound author's committed publications appear, with exact distinct identity and wire");
            Require((await outbox.ReadAsync(captured[0].Cursor, token)).SequenceEqual(captured.Skip(1)),
                "a bounded continuation advances without claiming or deleting the first item");
            await RequireRefusalAsync(() => bind(Foreign).ReadAsync(cancellationToken: token).AsTask(),
                "another tenant cannot read the publisher's transfer rows");
            await RequireRefusalAsync(() => bind(Isolated).AcknowledgeAsync(captured[0].Occurrence, captured[0].TransferFingerprint, token).AsTask(),
                "an isolated TestRun host cannot acknowledge any ordinary transfer");
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await owner.StopAsync(stop.Token);
        }

        var cold = bind(Scope);
        Require((await cold.ReadAsync(cancellationToken: token)).SequenceEqual(captured),
            "a cold observer receives the same stable rows until destination acknowledgement");
        var first = captured[0];
        Require(!await cold.AcknowledgeAsync(first.Occurrence, new string('A', 64), token) &&
            (await cold.ReadAsync(cancellationToken: token)).SequenceEqual(captured),
            "a wrong fingerprint cannot acknowledge an existing publication");
        Require(await cold.AcknowledgeAsync(first.Occurrence, first.TransferFingerprint, token) &&
            !await cold.AcknowledgeAsync(first.Occurrence, first.TransferFingerprint, token) &&
            (await cold.ReadAsync(cancellationToken: token)).SequenceEqual(captured.Skip(1)),
            "exact acknowledgement settles independently, and duplicate acknowledgement has no effect");
        var second = captured[1];
        Require(await cold.AcknowledgeAsync(second.Occurrence, second.TransferFingerprint, token) &&
            (await cold.ReadAsync(cancellationToken: token)).Count == 0, "all destination acknowledgements clear the pending set");

        async Task<Contract.ActorRef> StartAsync(Contract.BoundScope scope, string operation, string? mode = null)
        {
            var result = await ingress.SendAsync(scope, new Contract.Target.Start(new(Publisher.ActorType, mode ?? operation, "{}")),
                operation, Contract.Kinds.Start, cancellationToken: token);
            return result is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } started }
                ? started : throw new InvalidOperationException($"The fixture start {operation} was refused as {result}.");
        }

        async Task WaitForAsync(Contract.BoundScope scope, Contract.ActorRef reference, Contract.ActorStatus status)
        {
            while ((await reads.GetAsync(scope, reference, token))?.Header.Status != status)
                await Task.Delay(20, token);
        }
    }

    private static async Task RequireRefusalAsync(Func<Task> work, string description)
    {
        try { await work(); }
        catch (Exception error) when (error is StoreFormatException or NotSupportedException)
        {
            Console.WriteLine("ok " + description);
            return;
        }
        throw new InvalidOperationException(description);
    }

    private static void Refused(string reason) => throw new InvalidOperationException(reason);

    private static void Require(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }

    private sealed class Publisher : Contract.IActorType
    {
        internal const string ActorType = "standalone-event-publisher";
        public string Type => ActorType;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            turn.State = "{}";
            if (turn.Header.TypeData == "local")
                turn.Emit(Key, First);
            else
            {
                turn.Publish(Key, First, TimeSpan.Zero);
                if (turn.Header.TypeData == "rollback") throw new InvalidOperationException("rollback authored Event");
                turn.Publish(Key, Second, TimeSpan.Zero);
            }
            turn.End(Contract.ActorStatus.Finished);
            return ValueTask.CompletedTask;
        }
    }
}

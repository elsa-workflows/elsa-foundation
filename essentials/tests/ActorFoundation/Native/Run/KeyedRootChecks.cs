using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class KeyedRootChecks
{
    private const string ActorType = "keyed-root-check";
    private const string Node = "keyed-root-owner";

    internal static async Task<int> RunAsync(bool postgres, bool inBatch)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var token = deadline.Token;
        var directory = Directory.CreateTempSubdirectory("keyed-root-check");
        try
        {
            var fixture = await OpenAsync(postgres, inBatch, Path.Combine(directory.FullName, "root.db"), token);
            var scope = ContractScopes.Bind("keyed");
            var other = ContractScopes.Bind("other");
            var start = new Contract.StartSpec(ActorType, "keyed", "seed");
            var reuseWindow = TimeSpan.FromMilliseconds(100);
            var keep = new Contract.AddOptions(KeepFor: reuseWindow);
            var key = new Contract.Target.Key("Invoice", start);
            var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => SendAsync(scope, key, "start",
                Contract.Kinds.Start), token)));
            var accepted = attempts.Single(item => item.Outcome == Contract.AdmissionOutcome.Accepted);
            var reference = accepted.Actor!.Value;
            Require(attempts.All(item => item.Actor == reference) && attempts.Count(item => item.Outcome == Contract.AdmissionOutcome.Duplicate) == 3,
                "concurrent first uses admit a single normal creator and replay its exact actor");

            var conflict = await SendAsync(scope, new Contract.Target.Key("Invoice", start with { Input = "other" }), "start", Contract.Kinds.Start);
            var repeat = await SendAsync(scope, key, "START", Contract.Kinds.Start);
            Require(conflict.Reason == Contract.RefusalReason.Conflict && repeat.Reason == Contract.RefusalReason.Repeat,
                "changed input conflicts and an ordinally different operation cannot create another root for this key");
            var lower = await SendAsync(scope, new Contract.Target.Key("invoice", start), "start", Contract.Kinds.Start);
            var foreign = await SendAsync(other, key, "start", Contract.Kinds.Start);
            Require(lower.Outcome == Contract.AdmissionOutcome.Accepted && foreign.Outcome == Contract.AdmissionOutcome.Accepted &&
                lower.Actor != reference && foreign.Actor != reference && lower.Actor != foreign.Actor,
                "binary key spellings and bound tenants get different actor references");

            var command = new Contract.Target.Key("Invoice");
            const string exactOperation = "opaque\0\uD800";
            var first = await SendAsync(scope, command, exactOperation, "append", "first");
            var retry = await SendAsync(scope, command, exactOperation, "append", "first");
            var changed = await SendAsync(scope, command, exactOperation, "append", "changed");
            var second = await SendAsync(scope, command, "OPAQUE\0\uD800", "append", "second");
            Require(first is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: var selected } && selected == reference &&
                retry is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var previous } && previous == reference &&
                changed.Reason == Contract.RefusalReason.Conflict && second.Outcome == Contract.AdmissionOutcome.Accepted,
                "pending creator accepts exact opaque keyed sends while the receipt rejects changed data");

            await using var db = fixture.Open();
            var stored = await db.Database.SqlQuery<KeyedImage>($"""
                SELECT b."Creation", b."Actor", b."Partition", b."Creator", k."KeyBytes"
                FROM "FoundationRootBirths" b JOIN "FoundationKeyBindings" k
                    ON k."Tenant" = b."Tenant" AND k."Reference" = b."Reference"
                WHERE b."Reference" = {reference.Id}
                """).SingleAsync(token);
            var partitions = await db.Partitions.CountAsync(token);
            Require(stored.Creation == 1 && stored.Partition == InstanceRef.Spread("keyed_Invoice", partitions) &&
                stored.KeyBytes.AsSpan().SequenceEqual(DurableKeyJson.CodeUnits("Invoice")) && stored.Creator == "root:" + stored.Actor,
                "a retained binary binding uses stable tenant/key placement and the existing creation-one wire");
            var operations = await db.Database.SqlQuery<string>($"""
                SELECT "Operation" AS "Value" FROM "FoundationKeyReceipts" WHERE "Actor" = {stored.Actor}
                """).ToListAsync(token);
            Require(operations.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "start", exactOperation, "OPAQUE\0\uD800" }
                .Select(FoundationRootCreator.OperationDigest)),
                "exactly three operations persist as digests with no raw caller token");

            await using var owner = await fixture.StartAsync(token);
            var running = await UntilAsync(async () => await fixture.Reads.GetAsync(scope, reference, token), actor =>
                actor is { Header.Status: Contract.ActorStatus.Finished });
            var journal = await fixture.Reads.TailAsync(scope, reference, cancellationToken: token);
            Require(journal.Items.Count(item => item.Entry.Contains("first", StringComparison.Ordinal)) == 1 &&
                journal.Items.Count(item => item.Entry.Contains("second", StringComparison.Ordinal)) == 1,
                "the normal owner consumes the keyed creator and each keyed occurrence once");

            var delete = JsonSerializer.Serialize(new Contract.OperatorCommand(running!.Selection, ExpectedTarget: reference));
            var removed = await SendAsync(scope, new Contract.Target.Ref(reference), "delete", Contract.Kinds.Delete, delete);
            Require(removed.Outcome == Contract.AdmissionOutcome.Accepted, "the keyed root uses the normal exact-delete route");
            await UntilAsync(async () => await fixture.Reads.GetAsync(scope, reference, token), actor => actor is null);
            await Task.Delay(reuseWindow, token);
            var blocked = await SendAsync(scope, key, "after-delete", Contract.Kinds.Start);
            var old = await SendAsync(scope, command, exactOperation, "append", "first");
            Require(blocked.Reason == Contract.RefusalReason.Repeat && old.Outcome == Contract.AdmissionOutcome.Duplicate && old.Actor == reference,
                "retirement keeps the binding and old receipts while physical generation reuse remains disabled");
            return 0;

            Task<Contract.Admission> SendAsync(Contract.BoundScope tenant, Contract.Target target, string id, string kind, string? payload = null) =>
                fixture.Ingress.SendAsync(tenant, target, id, kind, payload, keep, token).AsTask();

            async Task<T> UntilAsync<T>(Func<Task<T>> next, Func<T, bool> matches)
            {
                while (true)
                {
                    var result = await next();
                    if (matches(result)) return result;
                    await Task.Delay(25, token);
                }
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed record Fixture(Func<KernelDb> Open, Contract.IIngress Ingress, Contract.IActorReads Reads,
        Func<CancellationToken, Task<IAsyncDisposable>> StartAsync);

    private sealed record KeyedImage(long Creation, string Actor, int Partition, string Creator, byte[] KeyBytes);

    private static async Task<Fixture> OpenAsync(bool postgres, bool inBatch, string file, CancellationToken token)
    {
        static bool Authorize(Contract.BoundScope scope, Contract.ActorRef target, string kind) =>
            scope.Tenant == "keyed" && kind == Contract.Kinds.Delete;
        static void EndProcess(string reason) => throw new InvalidOperationException(reason);
        if (postgres)
        {
            var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
                ?? throw new InvalidOperationException("An empty dedicated PostgreSQL fixture database is required.");
            var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
            KernelDb Open() => PostgresPreviewHosting.CreateKeyedStartsQualificationContext(options);
            await using (var empty = Open())
                Require(await PostgresFixtureDatabase.IsEmptyAsync(empty, token), "the keyed fixture requires a fresh PostgreSQL store");
            await PostgresPreviewHosting.InstallKeyedStartsQualificationAsync(options, token);
            var requests = PostgresPreviewHosting.CreateRequests(Open, Node, ActorType);
            var foundation = await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(Open, ActorType, Authorize, requests,
                cancellationToken: token);
            return new(Open, foundation, foundation, async ct => await PostgresPreviewHosting.StartAsync(Open, Node, new Probe(),
                EndProcess, options: null, cancellationToken: ct, onFault: null, replies: requests, fenceInBatch: inBatch));
        }

        var sqlite = SqliteEmbeddedFoundation.ForKeyedStartsQualification(file, Node, ActorType);
        await sqlite.InstallShippedAsync(token);
        var ingress = await sqlite.OpenIngressWithOperatorsAsync(Authorize, token);
        var sqliteOptions = new DbContextOptionsBuilder<KernelDb>().UseSqlite(new SqliteConnectionStringBuilder
            { DataSource = file, ForeignKeys = true, Pooling = false }.ToString()).Options;
        return new(() => KernelDb.ForSqliteRootShipped(sqliteOptions, StoreFormat.RootKeyedStartsQualification), ingress, sqlite.CreateReads(),
            async ct =>
            {
                var owner = sqlite.CreateHostedService(new Probe(), EndProcess, fenceInBatch: inBatch);
                await owner.StartAsync(ct);
                return owner;
            });
    }

    private static void Require(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Console.WriteLine("ok " + message);
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => ActorType;
        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.StartSettings is not null) turn.State = "[]";
            var items = JsonSerializer.Deserialize<List<string>>(turn.State)!;
            foreach (var item in turn.Inbox.Where(item => item.Kind == "append"))
            {
                items.Add(item.Payload!);
                turn.Record(new { item.Payload });
            }
            turn.State = JsonSerializer.Serialize(items);
            if (items.Count == 2) turn.End(Contract.ActorStatus.Finished);
            return ValueTask.CompletedTask;
        }
    }
}

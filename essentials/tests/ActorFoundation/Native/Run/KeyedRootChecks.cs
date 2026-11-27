using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
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
            var stored = await ReadBindingAsync(reference);
            var partitions = await db.Partitions.CountAsync(token);
            Require(stored is { Creation: 1, Generation: 1 } && stored.KeyActor == stored.Actor &&
                stored.Partition == InstanceRef.Spread("keyed_Invoice", partitions) &&
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
            await VerifyReuseAsync();
            await VerifyHeldAsync();
            await VerifyParkArrivalAsync();
            return 0;

            Task<Contract.Admission> SendAsync(Contract.BoundScope tenant, Contract.Target target, string id, string kind, string? payload = null) =>
                fixture.Ingress.SendAsync(tenant, target, id, kind, payload, keep, token).AsTask();

            Task<KeyedImage> ReadBindingAsync(Contract.ActorRef actor) => db.Database.SqlQuery<KeyedImage>($"""
                SELECT b."Creation", k."Generation", k."Actor" AS "KeyActor", b."Actor", b."Partition", b."Creator",
                    k."KeyBytes", k."ReuseAfter"
                FROM "FoundationRootBirths" b JOIN "FoundationKeyBindings" k
                    ON k."Tenant" = b."Tenant" AND k."Reference" = b."Reference"
                WHERE b."Reference" = {actor.Id}
                """).SingleAsync(token);

            async Task VerifyReuseAsync()
            {
                // Stage a future recorded window on the retired fixture, then restore its real deadline.
                var future = await fixture.ReadClockAsync(token) + 60_000;
                await SetReuseDeadlineAsync(future);
                var tooEarly = await SendAsync(scope, key, "after-delete", Contract.Kinds.Start);
                Require(tooEarly.Reason == Contract.RefusalReason.Repeat,
                    "an exact retirement marker cannot advance a binding before its reuse deadline");
                await SetReuseDeadlineAsync(stored.ReuseAfter);
                await UntilAsync(async () => await fixture.ReadClockAsync(token), now => now >= stored.ReuseAfter);

                // Stage an outstanding ledger against the exact retired life, without scheduling its continuation.
                var children = JsonSerializer.Serialize(new[] { new CreationAddress("0/child-a", 1), new CreationAddress("0/child-b", 1) });
                FormattableString mark = postgres
                    ? (FormattableString)$"""INSERT INTO "FoundationRootChildCascades" VALUES ({reference.Id}, 1, 1, CAST({children} AS jsonb))"""
                    : $"""INSERT INTO "FoundationRootChildCascades" VALUES ({reference.Id}, 1, 1, {children})""";
                await db.Database.ExecuteSqlInterpolatedAsync(mark, token);
                var outstanding = await SendAsync(scope, key, "after-delete", Contract.Kinds.Start);
                var waiting = await RequestAsync(scope, key, "after-delete", Contract.Kinds.Start);
                Require(outstanding.Reason == Contract.RefusalReason.ObligationOutstanding && waiting is
                    { Error: "in progress", RefusalReason: Contract.RefusalReason.ObligationOutstanding, RetryAfter: not null },
                    "a predecessor with an outstanding cascade refuses both keyed start forms");
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "FoundationRootChildCascades" WHERE "Reference" = {reference.Id}
                    """, token);

                var starts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => SendAsync(scope, key, "after-delete",
                    Contract.Kinds.Start), token)));
                var next = starts.Single(item => item.Outcome == Contract.AdmissionOutcome.Accepted).Actor!.Value;
                var newer = await ReadBindingAsync(next);
                Require(starts.Count(item => item.Outcome == Contract.AdmissionOutcome.Duplicate) == 3 &&
                    starts.All(item => item.Actor == next) && next != reference &&
                    newer is { Generation: 2, Creation: 1 } && newer.KeyActor == stored.KeyActor &&
                    newer.Partition == stored.Partition && newer.Actor != stored.Actor,
                    "concurrent reuse creates one new life with the same logical actor and virtual partition");
                await UntilAsync(async () => await fixture.Reads.GetAsync(scope, next, token),
                    actor => actor is { Header.Status: Contract.ActorStatus.Running });

                var oldStart = await SendAsync(scope, key, "start", Contract.Kinds.Start);
                var oldInput = await SendAsync(scope, command, exactOperation, "append", "first");
                var oldWait = await RequestAsync(scope, command, exactOperation, "append", "first", timeoutMs: 750);
                var staleAddress = await SendAsync(scope, new Contract.Target.Ref(reference), "stale-address", "append", "old life");
                Require(oldStart.Outcome == Contract.AdmissionOutcome.Duplicate && oldStart.Actor == reference &&
                    oldInput.Outcome == Contract.AdmissionOutcome.Duplicate && oldInput.Actor == reference &&
                    oldWait is { Error: "timeout", Status: null } && staleAddress.Outcome == Contract.AdmissionOutcome.Refused &&
                    await fixture.Reads.GetAsync(scope, reference, token) is null,
                    "old operations and exact references stay bound to the retired life after reuse");

                var answer = await RequestAsync(scope, command, "new-life-input", "answer", "new life");
                var nextTail = await fixture.Reads.TailAsync(scope, next, cancellationToken: token);
                Require(answer is { Channel: "answer", Item: "new life" } &&
                    nextTail.Items.Count(item => item.Entry.Contains("new life", StringComparison.Ordinal)) == 1 &&
                    nextTail.Items.All(item => !item.Entry.Contains("first", StringComparison.Ordinal) &&
                        !item.Entry.Contains("second", StringComparison.Ordinal) && !item.Entry.Contains("old life", StringComparison.Ordinal)),
                    "fresh keyed input reaches only the new life with a new journal and no predecessor inputs");

                Task<int> SetReuseDeadlineAsync(long at) => db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "FoundationKeyBindings" SET "ReuseAfter" = {at} WHERE "Reference" = {reference.Id}
                    """, token);
            }

            async Task VerifyHeldAsync()
            {
                var keyedStart = new Contract.Target.Key("Held", start with { TypeData = "held" });
                var keyedInput = new Contract.Target.Key("Held");
                const string heldOperation = "held|early\0\uD800";
                var started = await RequestAsync(scope, keyedStart, heldOperation, Contract.Kinds.Start);
                Require(started is { Channel: "started", Item: "ready", Error: null, Actor: not null },
                    "the held keyed creator replies through its opaque registration and reports the committed actor");
                var heldActor = started.Actor!.Value;
                Require((await fixture.Reads.GetAsync(scope, heldActor, token))?.Header.Status == Contract.ActorStatus.Running,
                    "a committed held start reply names a readable exact actor");

                var answered = await RequestAsync(scope, keyedInput, heldOperation + "-input", "answer", "first response");
                Require(answered is { Channel: "answer", Item: "first response", Error: null },
                    "a held keyed input receives its own committed response from the existing exact life");

                var oneWayReplay = await SendAsync(scope, keyedStart, heldOperation, Contract.Kinds.Start);
                var conflict = await RequestAsync(scope, keyedInput, heldOperation + "-input", "answer", "changed response");
                var repeatedStart = await RequestAsync(scope, keyedStart, heldOperation + "-other", Contract.Kinds.Start);
                var foreign = await RequestAsync(other, keyedInput, heldOperation + "-input", "answer", "first response");
                Require(oneWayReplay is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: var original } && original == heldActor &&
                    conflict.Error == "conflict" && repeatedStart.Error == "repeat" && foreign.Error == "missing",
                    "held and one-way admission share exact receipts while conflicts, repeated starts and foreign keys keep their own outcomes");

                var retry = await RequestAsync(scope, keyedInput, heldOperation + "-input", "answer", "first response", timeoutMs: 750);
                var tail = await fixture.Reads.TailAsync(scope, heldActor, cancellationToken: token);
                Require(retry is { Error: "timeout", Status: Contract.ActorStatus.Running } &&
                    tail.Items.Count(item => item.Entry.Contains("first response", StringComparison.Ordinal)) == 1,
                    "a duplicate waits on its own registration and consults the original receipt without delivering twice");

                var expiredKey = new Contract.Target.Key("Expired", start with { TypeData = "held" });
                var expired = await RequestAsync(scope, expiredKey, "expired", Contract.Kinds.Start, timeoutMs: -1);
                var afterExpiry = await SendAsync(scope, expiredKey, "expired", Contract.Kinds.Start);
                Require(expired.Error == "timeout" && afterExpiry.Outcome == Contract.AdmissionOutcome.Accepted,
                    "an expired keyed wait leaves no creator or receipt");
            }

            async Task VerifyParkArrivalAsync()
            {
                var target = new Contract.Target.Key("Parking");
                var started = await SendAsync(scope, new Contract.Target.Key(target.CallerKey, start), "park-start", Contract.Kinds.Start);
                var actor = started.Actor!.Value;
                await UntilAsync(async () => await fixture.Reads.GetAsync(scope, actor, token),
                    view => view is { Header.Status: Contract.ActorStatus.Running });
                await SendAsync(scope, target, "fault", "fault");
                var parked = await UntilAsync(async () => await fixture.Reads.GetAsync(scope, actor, token),
                    view => view is { Header.Status: Contract.ActorStatus.Faulted, Selection: not null });
                var held = await RequestAsync(scope, target, "held-while-parked", "answer", "held Park input");
                var arrivals = new Contract.AddOptions(KeepFor: TimeSpan.FromMinutes(1));
                var accepted = await fixture.Ingress.SendAsync(scope, target, "park-input", "append", "keyed Park input", arrivals, token);
                var retry = await fixture.Ingress.SendAsync(scope, target, "park-input", "append", "keyed Park input", arrivals, token);
                Require(held.Error == "missing" && accepted.Outcome == Contract.AdmissionOutcome.Accepted && accepted.Actor == actor &&
                    retry.Outcome == Contract.AdmissionOutcome.Duplicate && retry.Actor == actor,
                    "a one-way keyed input joins the current Park once and a held input is refused");

                var recover = JsonSerializer.Serialize(new Contract.OperatorCommand(parked!.Selection, ExpectedTarget: actor));
                var resumed = await SendAsync(scope, new Contract.Target.Ref(actor), "recover", Contract.Kinds.Recover, recover);
                Require(resumed.Outcome == Contract.AdmissionOutcome.Accepted, "the actor accepts the selected Recover");
                var tail = await UntilAsync(async () => await fixture.Reads.TailAsync(scope, actor, cancellationToken: token),
                    page => page.Items.Any(item => item.Entry.Contains("keyed Park input", StringComparison.Ordinal)));
                Require((await fixture.Reads.GetAsync(scope, actor, token))?.Header.Status == Contract.ActorStatus.Running &&
                    tail.Items.Count(item => item.Entry.Contains("keyed Park input", StringComparison.Ordinal)) == 1 &&
                    tail.Items.All(item => !item.Entry.Contains("held Park input", StringComparison.Ordinal)),
                    "Recover releases the keyed arrival to the same life once");
            }

            async Task<Contract.Reply> RequestAsync(Contract.BoundScope tenant, Contract.Target target, string id, string kind,
                string? payload = null, int timeoutMs = 4_000) =>
                await fixture.Ingress.RequestAsync(tenant, target, id, kind, payload, keep,
                    new(null, await fixture.ReadClockAsync(token) + timeoutMs), token);

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
        Func<CancellationToken, ValueTask<long>> ReadClockAsync,
        Func<CancellationToken, Task<IAsyncDisposable>> StartAsync);

    private sealed record KeyedImage(long Creation, long Generation, string KeyActor, string Actor, int Partition, string Creator,
        byte[] KeyBytes, long ReuseAfter);

    private static async Task<Fixture> OpenAsync(bool postgres, bool inBatch, string file, CancellationToken token)
    {
        static bool Authorize(Contract.BoundScope scope, Contract.ActorRef target, string kind) =>
            scope.Tenant == "keyed" && kind is Contract.Kinds.Delete or Contract.Kinds.Recover;
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
            KernelDb OpenSeparately() => PostgresPreviewHosting.CreateKeyedStartsQualificationContext(options);
            KernelDb OpenShipped() => PostgresPreviewHosting.CreateShippedContext(options);
            await ExpectAsync<ArgumentException>(() => PostgresPreviewHosting.OpenFoundationAsync(OpenSeparately, ActorType, requests,
                cancellationToken: token), "same PostgreSQL store factory");
            await ExpectAsync<ArgumentException>(() => PostgresPreviewHosting.OpenFoundationAsync(OpenShipped, ActorType, requests,
                cancellationToken: token), "same PostgreSQL store factory");
            var protectedRequests = PostgresPreviewHosting.CreateRequestsWithRootStartProtection(Open, Node, ActorType, ProtectAsync);
            var protectedFoundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, ActorType, protectedRequests, cancellationToken: token);
            await VerifyProtectedStartRefusalAsync(protectedFoundation, protectedRequests.ReadClockAsync, token);
            await using (var drift = Open())
            {
                await drift.Database.ExecuteSqlRawAsync("""ALTER TABLE "FoundationKeyBindings" ALTER COLUMN "ReuseAfter" DROP NOT NULL""", token);
                await ExpectAsync<StoreFormatException>(() => PostgresPreviewHosting.InstallKeyedStartsQualificationAsync(options, token),
                    "caller-key tables or indexes");
                await drift.Database.ExecuteSqlRawAsync("""ALTER TABLE "FoundationKeyBindings" ALTER COLUMN "ReuseAfter" SET NOT NULL""", token);
                await drift.Database.ExecuteSqlRawAsync("""CREATE INDEX "keyed_fixture_extra" ON "FoundationKeyReceipts" ("Fingerprint")""", token);
                await ExpectAsync<StoreFormatException>(() => PostgresPreviewHosting.InstallKeyedStartsQualificationAsync(options, token),
                    "caller-key tables or indexes");
                await drift.Database.ExecuteSqlRawAsync("""DROP INDEX "keyed_fixture_extra" """, token);
                Require(true, "the PostgreSQL installer refuses binding column and receipt index drift");
            }
            Require(true, "the PostgreSQL facade rejects a crossed keyed request edge or profile");
            var foundation = await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(Open, ActorType, Authorize, requests,
                cancellationToken: token);
            return new(Open, foundation, foundation, requests.ReadClockAsync, async ct => await PostgresPreviewHosting.StartAsync(Open, Node, new Probe(),
                EndProcess, options: null, cancellationToken: ct, onFault: null, replies: requests, fenceInBatch: inBatch));
        }

        var sqlite = SqliteEmbeddedFoundation.ForKeyedStartsQualification(file, Node, ActorType);
        await sqlite.InstallShippedAsync(token);
        var protectedSqlite = SqliteEmbeddedFoundation.ForKeyedStartsQualification(file, Node, ActorType, ProtectAsync);
        await protectedSqlite.InstallShippedAsync(token);
        await VerifyProtectedStartRefusalAsync(await protectedSqlite.OpenIngressWithOperatorsAsync(Authorize, token),
            protectedSqlite.ReadClockAsync, token);
        var ingress = await sqlite.OpenIngressWithOperatorsAsync(Authorize, token);
        var sqliteOptions = new DbContextOptionsBuilder<KernelDb>().UseSqlite(new SqliteConnectionStringBuilder
            { DataSource = file, ForeignKeys = true, Pooling = false }.ToString()).Options;
        return new(() => KernelDb.ForSqliteRootShipped(sqliteOptions, StoreFormat.RootKeyedStartsQualification), ingress, sqlite.CreateReads(),
            sqlite.ReadClockAsync,
            async ct =>
            {
                var owner = sqlite.CreateHostedService(new Probe(), EndProcess, fenceInBatch: inBatch);
                await owner.StartAsync(ct);
                return owner;
            });
    }

    private static ValueTask<bool> ProtectAsync(PendingRootStart start, CancellationToken token) =>
        throw new InvalidOperationException("This fixture requires callers to stop before any uncomposed protection.");

    private static async Task VerifyProtectedStartRefusalAsync(Contract.IIngress ingress, Func<CancellationToken, ValueTask<long>> clock,
        CancellationToken token)
    {
        var scope = ContractScopes.Bind("keyed");
        var start = new Contract.Target.Key("Protected", new(ActorType, "held"));
        var deadline = await clock(token) + 4_000;
        await ExpectAsync<NotSupportedException>(() => ingress.RequestAsync(scope, start, "protected-start", Contract.Kinds.Start,
            null, null, new(null, deadline), token).AsTask(), "configured root-start protection");
        Require(true, "a protected reply edge refuses an uncomposed held keyed creator");
    }

    private static async Task ExpectAsync<TException>(Func<Task> run, string message) where TException : Exception
    {
        try { await run(); }
        catch (TException error) when (error.Message.Contains(message, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Expected a refusal for " + message);
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
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            if (turn.StartSettings is not null)
            {
                turn.State = "[]";
                if (turn.Header.TypeData == "held") turn.Emit("started", "ready");
            }
            var items = JsonSerializer.Deserialize<List<string>>(turn.State)!;
            foreach (var item in turn.Inbox)
            {
                if (item.Kind == "fault") throw new InvalidOperationException("Park the keyed test actor.");
                if (item.Kind == "append")
                {
                    items.Add(item.Payload!);
                    turn.Record(new { item.Payload });
                }
                if (item.Kind == "answer")
                {
                    turn.Record(new { item.Payload });
                    turn.Emit("answer", item.Payload);
                }
            }
            turn.State = JsonSerializer.Serialize(items);
            if (items.Count == 2) turn.End(Contract.ActorStatus.Finished);
            return ValueTask.CompletedTask;
        }
    }
}

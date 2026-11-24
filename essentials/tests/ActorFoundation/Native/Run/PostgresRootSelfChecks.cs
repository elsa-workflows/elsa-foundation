using System.Data.Common;
using System.Text;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kernel.Native.Run;

internal static class PostgresRootSelfChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch, bool timers)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use a dedicated PostgreSQL preview database.");
        var clock = new DeadlineClock();
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(clock).Options;
        KernelDb Open() => timers ? KernelDb.ForPostgresRootTimers(options) : KernelDb.ForPostgresRootSelf(options);
        EfStore Client() => new(Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stop = timeout.Token;
        var bare = Client();
        var scoped = Client();
        var ingress = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        const string tenant = "issued-content";
        await bare.CreateAsync(1);
        await ingress.InstallAsync(stop);
        KernelDb Ordinary() => new(options);
        var directOnly = new EfStore(Ordinary, postgres: true);
        await RequireFormatRefusal(() => directOnly.PartitionCountAsync(stop).AsTask());
        await RequireFormatRefusal(() => directOnly.TakeAsync(0, "old-reader", TimeSpan.FromMinutes(1), stop).AsTask());
        await RequireFormatRefusal(() => new PostgresRootAdmission(Ordinary, TimeSpan.Zero).OpenJournalCursorAsync(stop));
        var reference = (await ingress.AdmitAsync(tenant, "birth", new("native", "wait"), cancellationToken: stop)).Reference;
        var creator = (await ingress.ReadPendingCreatorAsync(tenant, reference, stop))!;
        var actor = creator.Target.Id;
        var window = TimeSpan.FromMinutes(1);
        var lease = (await bare.TakeAsync(0, "check", window, stop))!;
        await bare.NodeHeartbeatAsync("check", window, window, stop);
        await using (var db = Open())
            Check((await db.Nodes.SingleAsync(n => n.Id == "check", stop)).ReadableMessageProtocol == (timers ? "4" : "3") &&
                !await db.Nodes.AnyAsync(n => n.Id == "old-reader", stop),
                "only the configured reader holds ownership and advertises its protocol");
        Check((await bare.CommitFirstRootAsync(tenant, reference, creator, lease.For(0), window, null, 1,
            "state", new Dictionary<string, string>(), """["start"]""", stop)).Landed, "creator");
        await scoped.EnableRootSelfSendsAsync(stop);
        await scoped.BindWorkerRootsAsync([(tenant, reference)], stop);
        var row = (await scoped.LoadAsync([actor], stop)).Single();
        await ingress.AdmitDirectAsync(tenant, reference, "next", "step", cancellationToken: stop);
        var direct = (await ingress.ReadPendingDirectAsync(tenant, reference, stop)).Single();
        const string suffix = "\0\ud800\u007f\udfff\U0001f642\n";
        var payload = new string('"', FoundationMetadataLimits.MessageContentBytes - Encoding.UTF8.GetByteCount(suffix)) + suffix;
        var logical = FoundationSelfSend.Issue(scoped.RootSelfIssuer!.Handle, CreationProtocol.Origin(actor, 1), 2, 0, null);
        var sent = FoundationSelfSend.Create(actor, 1, logical, "ordinary\0step", payload);
        Message Timer(string owner, int ordinal, long due)
        {
            var handle = FoundationSelfSend.Issue(scoped.RootSelfIssuer!.Handle, CreationProtocol.Origin(owner, 1), 2, ordinal, due);
            return FoundationSelfSend.Create(owner, 1, handle, "wake", null, due);
        }
        var zero = Timer(actor, 1, 0);
        var future = Timer(actor, 2, long.MaxValue);
        const long deadline = 4_102_444_800_000; // 2100-01-01 UTC
        var boundary = Timer(actor, 3, deadline);
        var siblingTimer = Timer(actor, 4, long.MaxValue - 1);
        CommitBatch Batch(params TurnCommit[] turns) => new(0, lease.Epoch, 1, window, false, turns, [], []);
        var emit = new TurnCommit(actor, row with { Revision = 2 }, 1, [], [direct.InboxKey], [sent, zero, future, boundary, siblingTimer], [], [], null);
        if (!timers)
        {
            try { await scoped.CommitAsync(Batch(emit), stop); throw new InvalidOperationException("A protocol-3 writer accepted a timer."); }
            catch (NotSupportedException) { }
            Check((await scoped.LoadAsync([actor], stop)).Single().Revision == 1 &&
                (await ingress.ReadPendingDirectAsync(tenant, reference, stop)).Single().InboxKey == direct.InboxKey,
                "protocol 3 refuses the mixed immediate and scheduled checkpoint before consuming its input");
            emit = emit with { Sent = [sent] };
        }
        Check((await scoped.CommitAsync(Batch(emit), stop)).Landed, "silent checkpoint and full-size emitted input");
        var key = sent.InboxKey;
        var stored = key with { Scope = EfStore.RootSelfScope };
        var empty = new TurnCommit(actor, null, null, [], [], [], [], [], null);
        foreach (var change in new[]
        {
            Batch() with { Seen = [stored] }, Batch() with { Trial = [stored] }, Batch(empty with { Held = [stored] }),
            Batch(empty with { Done = [stored] }), Batch(empty with { Consumed = [stored] })
        })
        {
            bool landed;
            try { landed = (await bare.CommitAsync(change, stop)).Landed; }
            catch (StoreException error) when (error.Failure == Failure.Definite) { landed = false; }
            Check(!landed, "unqualified disposition refused");
        }

        var ordinary = new Message(key.Id, creator.Target, "!foundation-self/1", "application body").AsIssued();
        Check((await bare.CommitAsync(Batch(empty with { Sent = [ordinary] }), stop)).Landed,
            "ordinary issued input with the same ID and stored kind can coexist");

        await ingress.AdmitDirectAsync(tenant, reference, "sibling", "next", cancellationToken: stop);
        var sibling = (await ingress.ReadPendingDirectAsync(tenant, reference, stop)).Single();
        var disabled = Client();
        await disabled.BindWorkerRootAsync(tenant, reference, stop);
        foreach (var marks in new[] { Batch() with { Seen = [sibling.InboxKey, key] }, Batch() with { Trial = [sibling.InboxKey, key] } })
        {
            try { await disabled.CommitAsync(marks, stop); throw new InvalidOperationException("A disabled owner marked a private input."); }
            catch (NotSupportedException) { }
        }
        await using (var db = Open())
            Check(await db.Messages.CountAsync(m => (m.Id == sibling.InboxKey.Id || m.Id == key.Id) && !m.Done && m.Seen == 0 && m.Trial == 0, stop) == 3,
                "disabled bound Seen and Trial leave the private input, ordinary twin and direct sibling untouched");

        var cold = Client();
        await cold.EnableRootSelfSendsAsync(stop);
        await cold.BindWorkerRootsAsync([(tenant, reference)], stop);
        var pending = (await cold.PollAsync(0, lease.Epoch, TimeSpan.Zero, 1, stop)).Messages.Single();
        Check(pending is { Seen: 0, Trial: 0, Attempts: 0 } && pending.Message == sent,
            "cold poll returns the original full-size input and unchanged delivery state");
        Check((await bare.CommitAsync(Batch(empty with { Consumed = [ordinary.InboxKey] }), stop)).Landed,
            "ordinary issued input retains its generic store contract");
        await using (var db = Open())
        {
            var bytes = await db.Database.SqlQuery<int>($"""
                SELECT octet_length("FoundationSelfContent") AS "Value" FROM "Messages" WHERE "Id" = {key.Id} AND "InboxScope" = {EfStore.RootSelfScope}
                """).SingleAsync(stop);
            Check(bytes == FoundationMetadataLimits.MessageContentBytes && await db.Journal.CountAsync(stop) == 1,
                "physical content occupies its exact application-byte budget, with one journal position");
        }
        row = (await cold.LoadAsync([actor], stop)).Single();
        Check(row.Revision == 2 && (await cold.CommitAsync(Batch() with { Seen = [key] }, stop)).Landed,
            "qualified owner can mark the unchanged input");
        Check((await cold.CommitAsync(Batch(new TurnCommit(actor, row with { Revision = 3 }, 2, [key], [], [], [], [], null)), stop)).Landed,
            "qualified owner consumes the exact input with the next silent checkpoint");

        const string eventPayload = "Body\nYQ==";
        foreach (var eventKey in new[] { "event-a", "event-b", "event-c", "event-d" })
        {
            var match = Events.Route(new(eventKey, "shared", eventPayload, long.MaxValue), new(eventKey, actor, "wait"));
            Check(await bare.AddEventAsync(match, stop), "each event match inserts under the shared wire ID");
        }
        await using (var db = Open())
        {
            var alias = await db.Messages.SingleAsync(m => m.InboxScope == EfStore.RootSelfScope && m.EventMatch != null, stop);
            Check(alias.Payload == eventPayload, "the private trigger leaves a matched event in its original representation");
        }
        Check((await cold.PollAsync(0, lease.Epoch, TimeSpan.Zero, 10, stop)).Messages.All(m => m.Message.EventMatch is null),
            "a scoped root poll leaves generic event matches outside its inbox");

        if (!timers)
        {
            Check((await cold.CommitAsync(Batch() with { Seen = [sibling.InboxKey] }, stop)).Landed,
                "the qualified owner marks its direct sibling before generic event polling");
            return await CheckHistoricalEventsAsync();
        }
        var ready = (await cold.PollAsync(0, lease.Epoch, TimeSpan.Zero, 2, stop)).Messages.Select(m => m.Message).ToArray();
        Check(ready.SequenceEqual([zero, sibling]) && ready[0].Due == 0 && zero.Id != FoundationSelfSend.Issue(
            scoped.RootSelfIssuer!.Handle, CreationProtocol.Origin(actor, 1), 2, 1, null),
            "cold poll distinguishes the explicit zero deadline from an immediate send");
        row = (await cold.LoadAsync([actor], stop)).Single();
        TurnCommit Advance(Message input) => new(actor, row with { Revision = row.Revision + 1 }, row.Revision, [input.InboxKey], [], [], [], [], null);
        Check((await cold.CommitAsync(Batch(Advance(zero)), stop)).Landed, "the zero-deadline timer can settle");
        row = (await cold.LoadAsync([actor], stop)).Single();
        var settleSibling = new TurnCommit(actor, row with { Revision = 5 }, 4, [], [sibling.InboxKey], [], [], [], null);
        Check((await cold.CommitAsync(Batch(settleSibling), stop)).Landed, "the direct sibling settles before the future timer");
        row = (await cold.LoadAsync([actor], stop)).Single();
        Check(!(await cold.CommitAsync(Batch(Advance(future)), stop)).Landed, "the fenced settlement refuses the future timer");
        await using (var db = Open())
            Check((await cold.LoadAsync([actor], stop)).Single().Revision == 5 &&
                await db.Messages.CountAsync(m => m.Target == actor && !m.Done && m.InboxScope == EfStore.RootSelfScope &&
                    m.Id == future.InboxKey.Id && m.Due == long.MaxValue, stop) == 1,
                "the pending future timer and checkpoint survive the early settlement attempt");
        // Pin just the database clock used by the real poll and transactional settlement SQL.
        clock.At = "timestamptz '2099-12-31 23:59:59.999750+00'";
        var before = await cold.PollAsync(0, lease.Epoch, TimeSpan.Zero, 2, stop);
        Check(before.Now == deadline - 1 && before.Messages.Count == 0 && !(await cold.CommitAsync(Batch(Advance(boundary)), stop)).Landed,
            "a quarter millisecond before the deadline, neither poll nor settlement exposes the timer");
        clock.At = "timestamptz '2100-01-01 00:00:00+00'";
        var at = await cold.PollAsync(0, lease.Epoch, TimeSpan.Zero, 2, stop);
        Check(at.Now == deadline && at.Messages.Single().Message == boundary && (await cold.CommitAsync(Batch(Advance(boundary)), stop)).Landed,
            "at the exact deadline, poll and settlement accept the original timer");
        clock.At = null;
        row = (await cold.LoadAsync([actor], stop)).Single();
        var recallRevision = row.Revision;
        // An ordinary issued key with the same spelling stays in its separate physical scope.
        var other = new Message(future.InboxKey.Id, creator.Target, "ordinary", "retain").AsIssued();
        Check((await bare.CommitAsync(Batch(empty with { Sent = [other] }), stop)).Landed, "ordinary scoped neighbour stored");
        await ingress.AdmitDirectAsync(tenant, reference, "recall", "change", cancellationToken: stop);
        var stimulus = (await ingress.ReadPendingDirectAsync(tenant, reference, stop)).Single();
        var withdraw = new TurnCommit(actor, row with { Revision = recallRevision + 1 }, recallRevision, [], [stimulus.InboxKey], [], [], [], null)
            { FoundationSelfRecalls = new HashSet<InboxKey> { future.InboxKey } };
        var refused = false;
        try { await bare.CommitAsync(Batch(empty with { FoundationSelfRecalls = withdraw.FoundationSelfRecalls }), stop); }
        catch (StoreException error) when (error.Failure == Failure.Definite) { refused = true; }
        Check(refused, "the ordinary provider path refuses a scoped recall");
        Check(!(await cold.CommitAsync(Batch(withdraw) with { Epoch = lease.Epoch + 1 }, stop)).Landed,
            "a mismatched epoch cannot recall the timer");
        await bare.SubscribeAsync(new("recall-check", actor, "guard"), stop);
        Check(!(await cold.CommitAsync(Batch(withdraw with { Row = withdraw.Row! with { Status = InstanceStatus.Finished }, Journal = """["end"]""", Unsubscribe = true }), stop)).Landed,
            "the terminal precondition rejects the checkpoint after preparing its recall");
        await using (var db = Open())
            Check((await cold.LoadAsync([actor], stop)).Single().Revision == recallRevision &&
                (await ingress.ReadPendingDirectAsync(tenant, reference, stop)).Single() == stimulus &&
                await db.Messages.CountAsync(m => m.Id == future.InboxKey.Id && !m.Done, stop) == 2,
                "rollback keeps the private timer, ordinary neighbour, original stimulus and checkpoint");
        Check((await cold.CommitAsync(Batch(withdraw), stop)).Landed, "running checkpoint recalls the exact timer");
        await using (var db = Open())
            Check((await db.Messages.SingleAsync(m => m.Id == future.InboxKey.Id, stop)).InboxScope == -1 &&
                (await cold.LoadAsync([actor], stop)).Single().Revision == recallRevision + 1,
                "the ordinary neighbour survives the committed recall");
        Check(await PendingTimer(siblingTimer), "the other private timer on the same root survives recall");
        await ingress.AdmitDirectAsync(tenant, reference, "recall-again", "change", cancellationToken: stop);
        var again = (await ingress.ReadPendingDirectAsync(tenant, reference, stop)).Single();
        row = (await cold.LoadAsync([actor], stop)).Single();
        Check((await cold.CommitAsync(Batch(withdraw with { Row = row with { Revision = recallRevision + 2 }, Expected = recallRevision + 1, Done = [again.InboxKey] }), stop)).Landed,
            "recalling the absent private timer again is idempotent");
        Check(await PendingTimer(siblingTimer), "the same private sibling survives an absent recall replay");

        var peerRef = (await ingress.AdmitAsync(tenant, "peer", new("native", "wait"), cancellationToken: stop)).Reference;
        var peerCreator = (await ingress.ReadPendingCreatorAsync(tenant, peerRef, stop))!;
        var peer = peerCreator.Target.Id;
        Check((await bare.CommitFirstRootAsync(tenant, peerRef, peerCreator, lease.For(0) with { Position = 1 }, window,
            null, 1, "state", new Dictionary<string, string>(), """["start"]""", stop)).Landed, "second root created on the same partition");
        var peerClient = Client();
        await peerClient.EnableRootSelfSendsAsync(stop);
        await peerClient.BindWorkerRootsAsync([(tenant, peerRef)], stop);
        await ingress.AdmitDirectAsync(tenant, peerRef, "issue", "step", cancellationToken: stop);
        var peerInput = (await ingress.ReadPendingDirectAsync(tenant, peerRef, stop)).Single();
        var peerRow = (await peerClient.LoadAsync([peer], stop)).Single();
        var peerTimer = Timer(peer, 0, long.MaxValue);
        var issue = new TurnCommit(peer, peerRow with { Revision = 2 }, 1, [], [peerInput.InboxKey], [peerTimer], [], [], null);
        Check((await peerClient.CommitAsync(Batch(issue) with { Position = 2 }, stop)).Landed && await PendingTimer(peerTimer),
            "second root issues its own pending private timer");

        await ingress.AdmitDirectAsync(tenant, reference, "another-change", "step", cancellationToken: stop);
        var input = (await ingress.ReadPendingDirectAsync(tenant, reference, stop)).Single();
        row = (await cold.LoadAsync([actor], stop)).Single();
        var mismatched = withdraw with { Row = row with { Revision = recallRevision + 3 }, Expected = recallRevision + 2, Done = [input.InboxKey],
            FoundationSelfRecalls = new HashSet<InboxKey> { peerTimer.InboxKey } };
        refused = false;
        try { await cold.CommitAsync(Batch(mismatched) with { Position = 2 }, stop); }
        catch (NotSupportedException) { refused = true; }
        Check(refused && await PendingTimer(peerTimer) && await PendingTimer(siblingTimer) &&
            (await cold.LoadAsync([actor], stop)).Single().Revision == recallRevision + 2 &&
            (await peerClient.LoadAsync([peer], stop)).Single().Revision == 2 &&
            (await ingress.ReadPendingDirectAsync(tenant, reference, stop)).Single() == input,
            "a scoped recall for the other root is refused and preserves both timers, checkpoints and the input");
        var matched = mismatched with { FoundationSelfRecalls = new HashSet<InboxKey> { siblingTimer.InboxKey } };
        Check((await cold.CommitAsync(Batch(matched) with { Position = 2 }, stop)).Landed &&
            !await PendingTimer(siblingTimer) && await PendingTimer(peerTimer) && (await cold.LoadAsync([actor], stop)).Single().Revision == recallRevision + 3,
            "the same commit with this root's timer lands and leaves the second root's timer pending");
        return await CheckHistoricalEventsAsync();

        async Task<int> CheckHistoricalEventsAsync()
        {
            await using var db = Open();
            const string oldTrigger = """
                CREATE OR REPLACE FUNCTION kernel_root_self_insert_v2() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE head text := split_part(NEW."Payload", chr(10), 1);
                BEGIN
                    IF left(head, 1) = 'B' THEN
                        NEW."FoundationSelfContent" := decode(substr(NEW."Payload", length(head) + 2), 'base64');
                        NEW."Payload" := head;
                    END IF;
                    RETURN NEW;
                END $fn$;
                CREATE OR REPLACE TRIGGER kernel_root_self_insert BEFORE INSERT ON "Messages" FOR EACH ROW
                WHEN (NEW."InboxScope" = -5) EXECUTE FUNCTION kernel_root_self_insert_v2();
                """;
            await db.Database.ExecuteSqlRawAsync(oldTrigger, stop);
            await Client().EnableRootSelfSendsAsync(stop);
            Check(await db.Database.SqlQueryRaw<string>("""
                SELECT attname::text AS "Value" FROM pg_attribute
                WHERE attrelid = '"FoundationRootMessages"'::regclass AND attnum > 0 AND NOT attisdropped
                ORDER BY attnum DESC LIMIT 1
                """).SingleAsync(stop) == "JobGeneration", "the installed view retains the predecessor's column signature");
            Check(await db.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS (SELECT 1 FROM pg_trigger WHERE tgrelid = '"Messages"'::regclass AND tgname = 'kernel_root_self_insert'
                    AND tgfoid = to_regprocedure('kernel_root_self_insert_v3()')) AS "Value"
                """).SingleAsync(stop), "reopening upgrades the active trigger even when the latest function is already present");

            await db.Database.ExecuteSqlRawAsync(oldTrigger, stop);
            foreach (var eventKey in new[] { "event-a", "event-b", "event-c", "event-d" })
            {
                var match = Events.Route(new(eventKey, "previous", eventPayload, long.MaxValue), new(eventKey, actor, "wait"));
                Check(await bare.AddEventAsync(match, stop), "the previous store records each matched event");
            }
            await RequireFormatRefusal(() => Client().EnableRootSelfSendsAsync(stop), "split payload");
            await RequireFormatRefusal(() => bare.PollAsync(0, lease.Epoch, TimeSpan.Zero, 20, stop).AsTask(), "split payload");
            Check(await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM "Messages" WHERE "EventMatch" IS NOT NULL
                    AND "Payload" = 'Body' AND "FoundationSelfContent" = convert_to('a', 'UTF8')
                """).SingleAsync(stop) == 1, "reopening and generic delivery refuse the ambiguous predecessor payload and preserve its stored bytes");
            return 0;
        }

        async Task<bool> PendingTimer(Message timer)
        {
            await using var db = Open();
            return await db.Messages.AnyAsync(m => m.Id == timer.InboxKey.Id && m.Target == timer.Target.Id &&
                m.InboxScope == EfStore.RootSelfScope && m.Due == timer.Due && !m.Done, stop);
        }
    }

    private sealed class DeadlineClock : DbCommandInterceptor
    {
        internal string? At { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (At is not null && command.CommandText.Contains("\"Due\" <=", StringComparison.Ordinal))
                command.CommandText = command.CommandText.Replace("clock_timestamp()", At, StringComparison.Ordinal);
            return new(result);
        }
    }

    private static async Task RequireFormatRefusal(Func<Task> check, string? reason = null)
    {
        try { await check(); }
        catch (StoreFormatException error) when (reason is null || error.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("The ordinary reader accepted the continuation database.");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("ok " + name);
    }
}

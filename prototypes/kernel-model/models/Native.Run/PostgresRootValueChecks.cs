using System.Data.Common;
using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresRootValueChecks
{
    internal sealed record Restart(string Actor, long Epoch);
    private sealed record State(FoundationValueReference? Value, string? ReadDigest = null);
    private sealed record PhysicalValue(byte[] Payload, string Transaction, string Tuple)
    {
        internal bool SameImage(PhysicalValue other) =>
            Transaction == other.Transaction && Tuple == other.Tuple && Payload.AsSpan().SequenceEqual(other.Payload);
    }
    private const string ActorType = "root-value-qualification";
    private const string RollbackFailure = "value qualification rolled back at commit";
    private const int ValueSize = 4096;

    internal static async Task<int> RunAsync(bool inBatch, Restart? restart)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a disposable PostgreSQL database.");
        var payloadProbe = new PayloadProbe();
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(payloadProbe).Options;
        KernelDb Open() => KernelDb.ForPostgresRootValues(options);
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = CommitSql.Owned };
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var token = timeout.Token;
        if (restart is null)
        {
            await using var database = Open();
            Check(await PostgresFixtureDatabase.IsEmptyAsync(database, token), "Use a fresh disposable PostgreSQL database.");
            await store.CreateAsync(1);
            await admission.InstallAsync(token);
        }

        await store.EnableRootValuesAsync(token);
        var scope = FixedTenantScope.Bind("value-qualification");
        var api = new PostgresPreviewFoundation(admission, store, new(store, await admission.OpenJournalCursorAsync(token), 32), ActorType);
        var (roots, partitions) = await store.BindWorkerPartitionAsync(0, token);
        var node = $"values-{Environment.ProcessId}";
        var adapter = new ActorAdapter(new PostgresNativeRootOwner.Headers(roots), new ValueActor(), rootValueReader: store.ReadRootValueAsync);
        var worker = new Worker(node, 0, store, adapter, new WorkerOptions { Partitions = partitions });
        var host = new DurableHost([worker]);
        await host.HeartbeatAsync(Defaults.Lease, token);
        await using var owner = await host.ActivateAsync(0, token) ?? throw new InvalidOperationException("The fixture could not acquire the partition.");

        var actor = restart is null ? await Start() : new Contract.ActorRef(restart.Actor);
        var expected = new FoundationValue(Payload(1));
        if (restart is null)
        {
            await Drive(1);
            await Send("write", "first-write", "1");
            await Drive(2);
            var stored = await Image();
            Check(Json.Read<State>(stored.State!).Value == expected.Reference && await CountValues() == 1 &&
                (await Read(stored, expected.Reference))?.SequenceEqual(Payload(1)) == true &&
                stored.State!.Length < 256 && await AtomicValue(stored, expected.Reference),
                "The compact reference, journal and same-database value must share the fenced transaction.");
            await VerifyOldReaders();
            Check(payloadProbe.SentOnePayloadPerWrite, "Each value statement must bind one 4 KiB binary parameter.");
            Console.WriteLine($"value-ready {Environment.ProcessId} {actor.Id} {worker.Epoch}");
            return 0;
        }

        var current = await Image();
        var reference = Json.Read<State>(current.State!).Value;
        Check(worker.Epoch > restart.Epoch && current.Revision == 2 && reference == expected.Reference,
            "The cold owner must take a newer fence and restore the exact stored reference.");
        var header = Header(current);
        Check(await store.ReadRootValueAsync(header with { Tenant = "other" }, current.Actor, expected.Reference, token) is null &&
            await store.ReadRootValueAsync(header, current.Actor with { Creation = 2 }, expected.Reference, token) is null &&
            await Read(current, expected.Reference with { Length = 1 }) is null &&
            await Read(current, expected.Reference with { Sha256 = new string('\0', 64) }) is null &&
            await Read(current, new FoundationValue(Payload(2)).Reference) is null,
            "Foreign scopes, stale creations, malformed references and missing values must return no bytes.");
        await Send("read", "cold-read");
        await Drive(3);
        var read = await Image();
        var readJournal = await api.TailAsync(scope, actor, cancellationToken: token);
        Check(Json.Read<State>(read.State!) == new State(expected.Reference, expected.Reference.Sha256),
            "The cold actor must read and verify bytes from its restored reference.");

        await Send("write", "second-write", "2");
        var candidate = new FoundationValue(Payload(2));
        var (batch, pending) = await PrepareWrite(read, candidate);
        Check(!(await store.CommitAsync(batch with { Epoch = restart.Epoch }, token)).Landed, "The prior owner's stale fence must reject the exact prepared value turn.");
        await AssertNoAdvance();

        // At COMMIT, PostgreSQL has executed both writes. Its sequence records the deferred trigger even though the transaction rolls back.
        await using (var probe = Open())
        {
            await probe.Database.ExecuteSqlRawAsync($"""
                CREATE SEQUENCE "RootValueRollbackProbe";
                CREATE FUNCTION root_value_rollback_probe() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM nextval('"RootValueRollbackProbe"');
                    RAISE EXCEPTION '{RollbackFailure}' USING ERRCODE = '23514';
                END
                $fn$;
                CREATE CONSTRAINT TRIGGER root_value_rollback_probe
                    AFTER INSERT ON "FoundationRootValues" DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION root_value_rollback_probe();
                """, token);
            var refused = false;
            try { await store.CommitAsync(batch, token); }
            catch (StoreException error) when (error.Failure == Failure.Definite && error.Message == RollbackFailure) { refused = true; }
            Check(refused && await probe.Database.SqlQueryRaw<bool>("""
                SELECT is_called AS "Value" FROM "RootValueRollbackProbe"
                """).SingleAsync(token), "The deferred PostgreSQL rejection must run after the value insertion.");
            await AssertNoAdvance();
            await probe.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER root_value_rollback_probe ON "FoundationRootValues";
                DROP FUNCTION root_value_rollback_probe();
                DROP SEQUENCE "RootValueRollbackProbe";
                """, token);
        }

        await Drive(4);
        var advanced = await Image();
        Check(Json.Read<State>(advanced.State!).Value == candidate.Reference && await CountValues() == 2 &&
            (await Read(advanced, candidate.Reference))?.SequenceEqual(Payload(2)) == true &&
            await AtomicValue(advanced, candidate.Reference),
            "The current worker must retry the same pending input and land the new value and checkpoint.");
        await Send("write", "same-value", "2");
        var advancedJournal = await api.TailAsync(scope, actor, cancellationToken: token);
        var (duplicateBatch, duplicateInput) = await PrepareWrite(advanced, candidate);
        var changedBytes = Payload(3);
        Check(changedBytes.Length == candidate.Reference.Length && new FoundationValue(changedBytes).Reference != candidate.Reference,
            "The physical corruption must keep the byte length and change the digest.");
        await ReplacePhysicalValue(advanced.Actor, candidate.Reference, changedBytes);
        var corrupt = await ReadPhysicalValue(advanced.Actor, candidate.Reference);
        Check(corrupt.Payload.AsSpan().SequenceEqual(changedBytes), "The fixture must corrupt the exact actor creation and digest.");

        var coldReader = new EfStore(Open, postgres: true);
        await coldReader.EnableRootValuesAsync(token);
        await Refused(async () => { await coldReader.ReadRootValueAsync(Header(advanced), advanced.Actor, candidate.Reference, token); },
            "The persisted value does not match its digest.");
        Console.Error.WriteLine("ok The cold value read refused wrong bytes of the expected length.");
        await Refused(async () => { await store.CommitAsync(duplicateBatch, token); },
            "A stored digest must describe an immutable value.");
        await AssertTurnUnchanged(advanced, advancedJournal.Items, duplicateBatch, duplicateInput);
        Check(corrupt.SameImage(await ReadPhysicalValue(advanced.Actor, candidate.Reference)) && await CountValues() == 2,
            "The refused duplicate must leave the exact corrupt value bytes and tuple version intact.");

        await ReplacePhysicalValue(advanced.Actor, candidate.Reference, candidate.Read());
        var restored = await ReadPhysicalValue(advanced.Actor, candidate.Reference);
        await Drive(5);
        Check(await CountValues() == 2 && (await Read(await Image(), candidate.Reference))?.SequenceEqual(Payload(2)) == true &&
            restored.SameImage(await ReadPhysicalValue(advanced.Actor, candidate.Reference)),
            "An equal-byte retry must settle its checkpoint while leaving the value bytes, xmin and ctid unchanged.");
        await VerifyExactActorAndReference();
        Check(payloadProbe.SentOnePayloadPerWrite, "Each value statement must bind one 4 KiB binary parameter.");
        Console.WriteLine($"value-verified {Environment.ProcessId} {actor.Id} {worker.Epoch}");
        return 0;

        async Task VerifyExactActorAndReference()
        {
            var neighbor = await Start("neighbor-start");
            await Drive(1, neighbor);
            await Send("write", "neighbor-write", "2", neighbor);
            await Drive(2, neighbor);
            var first = await Image();
            var second = await Image(neighbor);
            var firstHeader = Header(first);
            var secondHeader = Header(second, neighbor);
            var bytes = candidate.Read();
            Check(first.Actor != second.Actor && actor != neighbor && await CountValues() == 3 &&
                (await store.ReadRootValueAsync(firstHeader, first.Actor, candidate.Reference, token))?.AsSpan().SequenceEqual(bytes) == true &&
                (await store.ReadRootValueAsync(secondHeader, second.Actor, candidate.Reference, token))?.AsSpan().SequenceEqual(bytes) == true,
                "Both same-tenant roots must resolve the same digest through their own exact reference and actor creation.");
            Check(await store.ReadRootValueAsync(firstHeader, second.Actor, candidate.Reference, token) is null &&
                await store.ReadRootValueAsync(secondHeader, first.Actor, candidate.Reference, token) is null,
                "Crossing either same-tenant reference with the other physical actor must miss.");
        }

        async Task<Contract.ActorRef> Start(string operation = "start")
        {
            var start = await api.SendAsync(scope, new Contract.Target.Start(new(ActorType, "values")), operation, Contract.Kinds.Start, cancellationToken: token);
            return start is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } opened } ? opened : throw new InvalidOperationException("The root start was refused.");
        }

        async Task Send(string kind, string operation, string? payload = null, Contract.ActorRef? target = null) =>
            Check((await api.SendAsync(scope, new Contract.Target.Ref(target ?? actor), operation, kind, payload, cancellationToken: token))
                .Outcome == Contract.AdmissionOutcome.Accepted, "The exact generated root must admit the named input.");

        async Task<EfStore.RootImage> Image(Contract.ActorRef? target = null) => await store.ReadFirstRootAsync(scope.Tenant, (target ?? actor).Id, token)
            ?? throw new InvalidOperationException("The actor has no persisted checkpoint.");

        async Task Drive(long revision, Contract.ActorRef? target = null)
        {
            while ((await store.ReadFirstRootAsync(scope.Tenant, (target ?? actor).Id, token))?.Revision != revision)
            {
                await owner.WokenAsync(null, token);
                await owner.RunAsync(token);
                token.ThrowIfCancellationRequested();
            }
        }

        async Task<int> CountValues()
        {
            await using var db = Open();
            return await db.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "FoundationRootValues" """).SingleAsync(token);
        }

        async Task<(CommitBatch Batch, Message Input)> PrepareWrite(EfStore.RootImage image, FoundationValue value)
        {
            var input = (await admission.ReadPendingDirectAsync(scope.Tenant, actor.Id, token)).Single();
            var row = (await store.LoadAsync([image.Actor.Id], token)).Single();
            var turn = new TurnCommit(row.Id, row.WithFoundationState(Json.Write(new State(value.Reference))) with
                { Revision = row.Revision + 1 }, row.Revision, [], [input.InboxKey], [], [], [], """["stored"]""")
                { FoundationValues = [value] };
            var fence = worker.Fences.Single();
            return (new(fence.Partition, fence.Epoch, fence.Position, Defaults.Lease, false, [turn], [], []), input);
        }

        async Task<PhysicalValue> ReadPhysicalValue(CreationAddress address, FoundationValueReference value)
        {
            await using var db = Open();
            return await db.Database.SqlQuery<PhysicalValue>($"""
                SELECT "Payload", xmin::text AS "Transaction", ctid::text AS "Tuple"
                FROM "FoundationRootValues" WHERE "Actor" = {address.Id} AND "Creation" = {address.Creation}
                    AND "Digest" = {value.Sha256}
                """).SingleAsync(token);
        }

        async Task ReplacePhysicalValue(CreationAddress address, FoundationValueReference value, byte[] bytes)
        {
            await using var db = Open();
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "FoundationRootValues" SET "Payload" = {bytes}
                WHERE "Actor" = {address.Id} AND "Creation" = {address.Creation} AND "Digest" = {value.Sha256}
                """, token);
            Check(affected == 1, "The fixture must replace exactly one physical value row.");
        }

        async Task<bool> AtomicValue(EfStore.RootImage image, FoundationValueReference value)
        {
            await using var db = Open();
            return await db.Database.SqlQuery<bool>($"""
                SELECT count(*) = 1 AND bool_and(v.xmin = i.xmin AND i.xmin = j.xmin) AS "Value"
                FROM "FoundationRootValues" v JOIN "Instances" i ON i."Id" = v."Actor"
                JOIN "Journal" j ON j."Instance" = i."Id" AND j."Partition" = i."Partition" AND j."Revision" = i."Revision"
                WHERE v."Actor" = {image.Actor.Id} AND v."Creation" = {image.Actor.Creation} AND v."Digest" = {value.Sha256}
                    AND i."FoundationReference" = {actor.Id} AND i."FoundationTenant" = {scope.Tenant} AND i."Revision" = {image.Revision}
                """).SingleAsync(token);
        }

        FoundationDispatchHeader Header(EfStore.RootImage image, Contract.ActorRef? target = null) =>
            new(scope.Tenant, (target ?? actor).Id, new(image.TypeData, image.Labels));
        ValueTask<byte[]?> Read(EfStore.RootImage image, FoundationValueReference value) => store.ReadRootValueAsync(Header(image), image.Actor, value, token);

        async Task AssertNoAdvance()
        {
            await AssertTurnUnchanged(read, readJournal.Items, batch, pending);
            Check(await Read(read, candidate.Reference) is null && await CountValues() == 1 &&
                (await Read(read, expected.Reference))?.SequenceEqual(Payload(1)) == true,
                "The refused transaction must preserve the prior value rows.");
        }

        async Task AssertTurnUnchanged(EfStore.RootImage prior, IReadOnlyList<Contract.JournalEntry> journal,
            CommitBatch attempted, Message input)
        {
            var checkpoint = await Image();
            await using var db = Open();
            var partition = await db.Partitions.SingleAsync(partition => partition.Id == attempted.Partition, token);
            Check(checkpoint.Actor == prior.Actor && checkpoint.Revision == prior.Revision && checkpoint.State == prior.State &&
                partition.Owner == node && partition.Epoch == attempted.Epoch && partition.Position == attempted.Position &&
                (await admission.ReadPendingDirectAsync(scope.Tenant, actor.Id, token)) is [var remaining] &&
                remaining == input && (await api.TailAsync(scope, actor, cancellationToken: token)).Items.SequenceEqual(journal),
                "The refused transaction must preserve the exact checkpoint, journal, fence and pending input.");
        }

        async Task VerifyOldReaders()
        {
            await using var db = Open();
            Check((await db.StoreVersions.SingleAsync(token)).Format == new StoreFormat(3, 8, 1),
                "The root values fixture must use its own durable marker.");
            (Func<KernelDb> Open, StoreFormat Format)[] otherProfiles =
            [
                (() => new KernelDb(options), StoreFormat.Current),
                (() => KernelDb.ForPostgresRootJobs(options), StoreFormat.RootJobs),
                (() => KernelDb.ForPostgresRootTimedJobIssuance(options), StoreFormat.RootTimedJobIssuance)
            ];
            foreach (var (open, format) in otherProfiles)
            {
                var old = new EfStore(open, postgres: true);
                var markerRefused = false;
                try { await old.TakeAsync(0, "old-profile-probe", Defaults.Lease, token); }
                catch (StoreFormatException error) when (error.Message.Contains($"Stored {StoreFormat.RootValues}. This build reads {format}.", StringComparison.Ordinal))
                {
                    markerRefused = true;
                }
                Check(markerRefused, $"The protocol {format.MessageProtocol} owner must refuse the value database marker.");
                var stateRefused = false;
                try { await old.ReadFirstRootAsync(scope.Tenant, actor.Id, token); }
                catch (StoreException error) when (error.Failure == Failure.Definite && error.Message == "Unsupported qualified actor checkpoint.")
                {
                    stateRefused = true;
                }
                Check(stateRefused, $"The protocol {format.MessageProtocol} point reader must refuse the value checkpoint family.");
            }
        }
    }

    private static byte[] Payload(int seed)
    {
        var payload = new byte[ValueSize];
        new Random(seed).NextBytes(payload);
        return payload;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.Error.WriteLine("ok " + description);
    }

    private static async Task Refused(Func<Task> operation, string reason)
    {
        try { await operation(); }
        catch (StoreException error) when (error.Failure == Failure.Definite && error.Message == reason) { return; }
        throw new InvalidOperationException("The expected refusal was absent: " + reason);
    }

    private sealed class PayloadProbe : DbCommandInterceptor
    {
        private int _writes;
        private bool _singlePayload = true;
        internal bool SentOnePayloadPerWrite => _writes > 0 && _singlePayload;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"FoundationRootValues\"", StringComparison.Ordinal))
            {
                _writes++;
                var blobs = command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).OfType<byte[]>().ToArray();
                _singlePayload &= blobs is [{ Length: ValueSize }];
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ValueActor : Contract.IActorType
    {
        public string Type => ActorType;
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var values = (IRootValueTurn)turn;
            var input = turn.Inbox.Single();
            var state = turn.State is null ? new State(null) : Json.Read<State>(turn.State);
            switch (input.Kind)
            {
                case Contract.Kinds.Start:
                    break;
                case "write":
                    state = new(values.StoreValue(Payload(Json.Read<int>(input.Payload!))));
                    turn.Record("stored");
                    break;
                case "read" when state.Value is { } reference:
                    var bytes = await values.ReadValueAsync(reference, cancellationToken);
                    state = state with { ReadDigest = bytes is null ? null : new FoundationValue(bytes).Reference.Sha256 };
                    turn.Record("read");
                    break;
                default: throw new InvalidOperationException("Unexpected value fixture input.");
            }
            turn.State = Json.Write(state);
        }
    }
}

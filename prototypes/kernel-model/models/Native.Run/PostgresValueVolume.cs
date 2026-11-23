using System.Globalization;
using System.Security.Cryptography;
using Kernel.EntityFramework;
using Kernel.Foundation.InMemory;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresValueVolume
{
    private const string ActorType = "value-volume-qualification";
    private sealed record State(int Step, string? Inline = null, FoundationValueReference? Value = null, string? Digest = null);
    private sealed record Toast(long Instances, long Values);
    private sealed record Physical(int StateBytes, int StoredStateBytes, string? StateCompression,
        int ValueRows, long ValueBytes, string? ValueCompression);

    internal static async Task<int> RunAsync(bool byReference, bool changes, int pad, int turns)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
            throw new InvalidOperationException("Supply a fresh database on a dedicated, disposable PostgreSQL server.");
        var settings = new NpgsqlConnectionStringBuilder(connection);
        if (settings.Host != "127.0.0.1" || settings.SslMode != SslMode.Disable ||
            settings.GssEncryptionMode == GssEncryptionMode.Require)
            throw new ArgumentException("The wire meter requires a disposable plaintext loopback PostgreSQL connection.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var stop = timeout.Token;
        var direct = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootValues(direct);
        await using var probe = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(probe, stop), "The sample database must be empty.");
        var store = new EfStore(Open, postgres: true, fenceInBatch: true) { CommitSql = CommitSql.Owned };
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await store.CreateAsync(1);
        await admission.InstallAsync(stop);
        await store.EnableRootValuesAsync(stop);

        var scope = FixedTenantScope.Bind("value-volume");
        var api = new PostgresPreviewFoundation(admission, new(store, await admission.OpenJournalCursorAsync(stop), turns + 2), ActorType);
        var (roots, partitions) = await store.BindWorkerPartitionAsync(0, stop);
        var actor = new ActorAdapter(new PostgresNativeRootOwner.Headers(roots), new VolumeActor(byReference, changes, pad, turns),
            store.ReadRootValueAsync);
        var worker = new Worker("volume-writer", 0, store, actor,
            new WorkerOptions { Partitions = partitions, PassTurnBudget = 1 });
        var host = new DurableHost([worker]);
        await host.HeartbeatAsync(Defaults.Lease, stop);

        Contract.ActorRef reference;
        Toast before, after;
        var wal = new long[turns];
        await using (var owner = await host.ActivateAsync(0, stop) ??
            throw new InvalidOperationException("The writer must own its partition."))
        {
            var originalPosition = worker.Fences.Single().Position;
            var born = await api.SendAsync(scope, new Contract.Target.Start(new(ActorType, "value-volume")), "start",
                Contract.Kinds.Start, cancellationToken: stop);
            reference = born is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } id }
                ? id : throw new InvalidOperationException("The generated root was refused.");
            await Drive(originalPosition + 1);
            before = await ToastSize();
            for (var step = 1; step <= turns; step++)
            {
                var advanced = await api.SendAsync(scope, new Contract.Target.Ref(reference), $"step-{step}",
                    "advance", Json.Write(step), cancellationToken: stop);
                Check(advanced is { Outcome: Contract.AdmissionOutcome.Accepted } && advanced.Actor == reference,
                    "Every sample input must be independently admitted to the same actor.");
                var start = await WalPosition();
                await Drive(originalPosition + 1 + step);
                wal[step - 1] = await WalPosition() - start;
            }
            after = await ToastSize();

            async Task Drive(long expectedPosition)
            {
                await owner.WokenAsync(null, stop);
                for (var pass = 0; pass < 20 && worker.Fences.Single().Position < expectedPosition; pass++)
                    await owner.RunAsync(stop);
                Check(worker.Fences.Single().Position == expectedPosition, "Every scheduled turn must commit one journal position.");
            }
        }

        var checkpoint = await store.ReadFirstRootAsync(scope.Tenant, reference.Id, stop);
        Check(checkpoint is { Status: InstanceStatus.Finished } && checkpoint.Revision == turns + 1 &&
            checkpoint.TerminalRevision == checkpoint.Revision, "The measured schedule must finish at its final revision.");
        var journal = await api.TailAsync(scope, reference, cancellationToken: stop);
        var expected = new List<Contract.JournalEntry> { new(1, Json.Write("start")) };
        var uniqueDigests = new HashSet<string>(StringComparer.Ordinal);
        for (var step = 1; step <= turns; step++)
        {
            var digest = Hash(Payload(pad, changes ? step : 1));
            uniqueDigests.Add(digest);
            expected.Add(new(step + 1, Json.Write(Entry(step, digest))));
        }
        expected.Add(new(turns + 1, Json.Write("end")));
        Check(journal.Items.SequenceEqual(expected) && journal.Next is { } bookmark &&
            (await api.TailAsync(scope, reference, bookmark, stop)).Items.Count == 0,
            "The durable journal must contain the same ordered step digests and one terminal entry.");
        Check((await admission.ReadPendingDirectAsync(scope.Tenant, reference.Id, stop)).Count == 0,
            "All scheduled inputs must be settled.");

        var address = checkpoint!.Actor;
        var physical = await probe.Database.SqlQuery<Physical>($"""
            SELECT octet_length(i."State") AS "StateBytes", pg_column_size(i."State") AS "StoredStateBytes",
                pg_column_compression(i."State") AS "StateCompression", v."ValueRows", v."ValueBytes", v."ValueCompression"
            FROM "Instances" i CROSS JOIN LATERAL (
                SELECT count(*)::integer AS "ValueRows", coalesce(sum(octet_length("Payload")), 0)::bigint AS "ValueBytes",
                    max(pg_column_compression("Payload")) AS "ValueCompression"
                FROM "FoundationRootValues" WHERE "Actor" = i."Id" AND "Creation" = {address.Creation}
            ) v WHERE i."Id" = {address.Id}
            """).SingleAsync(stop);
        var expectedRows = byReference ? uniqueDigests.Count : 0;
        Check(physical.ValueRows == expectedRows && physical.ValueBytes == (long)expectedRows * pad &&
            wal.All(bytes => bytes > 0), "Every measured turn must write WAL and retain the expected physical value rows.");

        var wire = Wire.Start("postgres", $"Host=127.0.0.1;Port={settings.Port}");
        settings.Port = wire.LocalPort;
        settings.GssEncryptionMode = GssEncryptionMode.Disable;
        settings.Pooling = true;
        settings.MaxPoolSize = 1;
        var measured = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(settings.ConnectionString).Options;
        KernelDb OpenCold() => KernelDb.ForPostgresRootValues(measured);
        var cold = new EfStore(OpenCold, postgres: true);
        // These queries also warm the one physical connection. Start counting with no loaded actor state.
        await cold.EnableRootValuesAsync(stop);
        await cold.BindWorkerPartitionAsync(0, stop);
        var wireBefore = (wire.Trips, wire.ProtocolRequests, wire.Connections);
        var row = (await cold.LoadAsync([address.Id], stop)).Single();
        var saved = Json.Read<State>(row.State);
        var bytesAtRest = saved.Value is { } value && row.FoundationDispatchHeader is { } header
            ? await cold.ReadRootValueAsync(header, address, value, stop)
            : Convert.FromBase64String(saved.Inline!);
        var loadTrips = wire.Trips - wireBefore.Trips;
        var loadGroups = wire.ProtocolRequests - wireBefore.ProtocolRequests;
        Check(wire.Connections == wireBefore.Connections && loadTrips > 0 && loadGroups > 0,
            "The cold load must do real work over its already warmed connection.");
        var finalBytes = Payload(pad, changes ? turns : 1);
        Check(row.Revision == checkpoint.Revision && row.Creation == address.Creation && row.Status == InstanceStatus.Finished &&
            saved.Step == turns && saved.Digest == Hash(finalBytes) && (saved.Value is not null) == byReference &&
            bytesAtRest is not null && bytesAtRest.AsSpan().SequenceEqual(finalBytes),
            "A fresh store must recover the exact finished actor output and final payload.");

        var server = await probe.Database.SqlQueryRaw<string>("""
            SELECT ('PostgreSQL ' || current_setting('server_version') || ', block size ' || current_setting('block_size') ||
                ', full_page_writes=' || current_setting('full_page_writes') || ', wal_compression=' || current_setting('wal_compression') ||
                ', autovacuum=' || current_setting('autovacuum')) AS "Value"
            """).SingleAsync(stop);
        var walTotal = wal.Sum();
        Console.WriteLine(server);
        Console.WriteLine(FormattableString.Invariant(
            $"mode={(byReference ? "reference" : "inline")} pad={pad} pattern={(changes ? "changing" : "reused")} turns={turns} partitions={partitions} history=full writer=owned-in-batch"));
        Console.WriteLine(FormattableString.Invariant(
            $"wal_bytes={walTotal} wal_per_turn={walTotal / (double)turns:F1} first_turn_wal={wal[0]} later_wal_per_turn={(walTotal - wal[0]) / (double)(turns - 1):F1}"));
        Console.WriteLine(FormattableString.Invariant(
            $"instances_toast_before={before.Instances} instances_toast_after={after.Instances} values_toast_before={before.Values} values_toast_after={after.Values}"));
        Console.WriteLine(FormattableString.Invariant(
            $"physical_state_bytes={physical.StateBytes} stored_state_bytes={physical.StoredStateBytes} state_compression={physical.StateCompression ?? "none"} value_rows={physical.ValueRows} value_bytes={physical.ValueBytes} value_compression={physical.ValueCompression ?? "none"}"));
        Console.WriteLine(FormattableString.Invariant(
            $"cold_load_wire_trips={loadTrips} cold_load_protocol_groups={loadGroups} new_connections=0 final_revision={row.Revision} final_digest={saved.Digest}"));
        return 0;

        Task<long> WalPosition() => probe.Database.SqlQueryRaw<long>("""
            SELECT (pg_current_wal_insert_lsn() - '0/0'::pg_lsn)::bigint AS "Value"
            """).SingleAsync(stop);

        Task<Toast> ToastSize() => probe.Database.SqlQueryRaw<Toast>("""
            SELECT pg_total_relation_size(i.reltoastrelid) AS "Instances", pg_total_relation_size(v.reltoastrelid) AS "Values"
            FROM pg_class i CROSS JOIN pg_class v
            WHERE i.oid = '"Instances"'::regclass AND v.oid = '"FoundationRootValues"'::regclass
            """).SingleAsync(stop);
    }

    private static byte[] Payload(int pad, int seed)
    {
        var bytes = new byte[pad];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Entry(int step, string digest) => $"value:{step.ToString(CultureInfo.InvariantCulture)}:{digest}";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class VolumeActor(bool byReference, bool changes, int pad, int turns) : Contract.IActorType
    {
        public string Type => ActorType;
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var input = turn.Inbox.Single();
            if (input.Kind == Contract.Kinds.Start)
            {
                turn.State = Json.Write(new State(0));
                return default;
            }
            var old = Json.Read<State>(turn.State!);
            var step = Json.Read<int>(input.Payload!);
            Check(input.Kind == "advance" && step == old.Step + 1 && step <= turns, "The actor must advance once per admitted sample.");
            var next = old with { Step = step };
            if (changes || step == 1)
            {
                var bytes = Payload(pad, changes ? step : 1);
                next = byReference
                    ? next with { Value = ((IRootValueTurn)turn).StoreValue(bytes), Digest = Hash(bytes) }
                    : next with { Inline = Convert.ToBase64String(bytes), Digest = Hash(bytes) };
            }
            turn.State = Json.Write(next);
            turn.Record(Entry(step, next.Digest!));
            if (step == turns) turn.End(Contract.ActorStatus.Finished, next.Digest);
            return default;
        }
    }
}

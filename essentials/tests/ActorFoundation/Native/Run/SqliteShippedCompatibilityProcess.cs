using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// The same fixture driver is compiled against each archived shipped build. It does not supply a store marker.
internal static class SqliteShippedCompatibilityProcess
{
    private const string ActorType = "sqlite-format-rehearsal";
    private static readonly string[] SeedEntries = ["\"start\"", "\"seed\""];
    private static readonly string[] ContinuedEntries = [.. SeedEntries, "\"after-lock-refusal\""];

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args is not [var role, var folder, var file and ("prior" or "candidate"), var fence and ("checked" or "in-batch")] ||
            !File.Exists(Path.Combine(folder, ".sqlite-compatibility-rehearsal")))
            throw new ArgumentException("Use the SQLite shipped compatibility rehearsal script with a disposable directory.");

        var path = Path.Combine(folder, file + ".sqlite");
        var scope = EmbeddedTenant.Bind("compatibility");
        var options = new LoopOptions
        {
            BalanceEvery = TimeSpan.FromMilliseconds(100),
            PollInterval = TimeSpan.FromMilliseconds(25),
            Window = TimeSpan.FromMilliseconds(400)
        };
        var inBatch = fence == "in-batch";
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        var foundation = new SqliteEmbeddedFoundation(path, role + "-" + file, ActorType);
        Console.WriteLine($"process {Environment.ProcessId} {role} reads {StoreFormat.RootShipped}");

        try
        {
            switch (role)
            {
                case "seed":
                    Check(!File.Exists(path), "The seed owner requires an unused rehearsal file.");
                    await RunOwnerAsync(seed: true);
                    break;
                case "resume":
                    Check(File.Exists(path), "The rollback floor requires the candidate owner's file.");
                    await RunOwnerAsync(seed: false);
                    break;
                case "install":
                    Check(File.Exists(path), "The installer probe requires an existing rehearsal file.");
                    await foundation.InstallShippedAsync(token);
                    break;
                case "cold-owner":
                    await using (var owner = await SqliteEmbeddedHosting.StartFoundationAsync(path, "incompatible-owner",
                        new Probe(), Escalate, options, inBatch, token))
                        await owner.StopAsync(token);
                    break;
                case "sweeper":
                    // The script runs these probes on closed files. This is the SQLite shipped store's actual sweep path.
                    var format = new SqliteAdmissionDefinitions();
                    var dbOptions = new DbContextOptionsBuilder<KernelDb>().UseSqlite(Connection(readOnly: false)).Options;
                    var store = new EfStore(() => KernelDb.ForSqliteRootRuntime(dbOptions, format), postgres: false, fenceInBatch: inBatch);
                    Console.WriteLine($"swept {await store.SweepAsync(token)}");
                    break;
                case "projected-list":
                    var list = await SqliteEmbeddedHosting.CreateFoundationReads(path)
                        .ListAsync(scope, new Contract.ActorQuery(Type: ActorType), cancellationToken: token);
                    Check(list.Items is [var item] && item.Header.Ref == ReadReference(), "The transactional projection names the committed root.");
                    Console.WriteLine("projected list read the exact actor");
                    break;
                case "journal":
                    Check(await EntriesAsync(SqliteEmbeddedHosting.CreateFoundationReads(path), ReadReference()) is { Length: > 0 },
                        "The shipped journal read returns the actor's committed entries.");
                    Console.WriteLine("journal read the committed actor");
                    break;
                case "snapshot":
                    Console.WriteLine(await FingerprintAsync(live: false));
                    break;
                default:
                    throw new ArgumentException($"Unknown rehearsal role '{role}'.");
            }
            return 0;
        }
        catch (StoreFormatException error)
        {
            Console.WriteLine($"refused format {role} {error.Message}");
            return 8;
        }
        catch (SqliteHostLockException error)
        {
            Console.WriteLine($"refused host lock {role} {error.Message}");
            return 9;
        }

        async Task RunOwnerAsync(bool seed)
        {
            await foundation.InstallShippedAsync(token);
            await using var owner = foundation.CreateHostedService(new Probe(), Escalate, options, inBatch);
            await owner.StartAsync(token);
            // Retain an already-settled direct occurrence for the cold sweeper probe.
            var ingress = await SqliteEmbeddedHosting.OpenFoundationAsync(path, ActorType, TimeSpan.Zero, token);
            var reads = foundation.CreateReads();
            await WaitAsync(async () => (await OwnershipAsync()).Owner == role + "-" + file);
            var owned = (await OwnershipAsync()).Epoch;
            if (seed)
            {
                var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(new Contract.StartSpec(ActorType, "{}")),
                    "seed", Contract.Kinds.Start, cancellationToken: token);
                Check(admitted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "The shipped edge admits a fresh root.");
                var reference = admitted.Actor!.Value;
                await File.WriteAllTextAsync(ReferenceFile(), reference.Id, token);
                await ExpectEntriesAsync(reads, reference, SeedEntries);
                Console.WriteLine($"installed {await ReadVersionRowsAsync()}");
                var before = await FingerprintAsync(live: true);
                Console.WriteLine($"ready {file} epoch {owned}");

                await GateAsync("continue-" + file);
                Check(before == await FingerprintAsync(live: true), "The live refusal changed a schema, row, or ownership shape.");
                Console.WriteLine("live snapshot unchanged except lease timestamps");
                await AdvanceAsync(ingress, reads, reference, "after-lock-refusal", ContinuedEntries);
                Check(await OwnershipAsync() == (role + "-" + file, owned), "The same live owner and epoch commit after the refused attach.");
                Console.WriteLine($"continued {file} epoch {owned}");

                await GateAsync("stop-" + file);
            }
            else
            {
                var reference = ReadReference();
                var prefix = await EntriesAsync(reads, reference);
                Check(prefix.SequenceEqual(ContinuedEntries), "The floor must read the candidate's exact actor and journal prefix.");
                var journal = await JournalPrefixAsync();
                await AdvanceAsync(ingress, reads, reference, "floor-turn", [.. prefix, "\"floor-turn\""]);
                Check(journal == await JournalPrefixAsync(), "Reopening with the floor changed earlier physical journal rows.");
                Console.WriteLine($"resumed exact actor at {await ReadVersionRowsAsync()} epoch {owned} with its physical journal prefix");
            }
            await owner.StopAsync(token);
            Check(owner.LastError is null && (await OwnershipAsync()).Owner is null, "A clean stop releases the partition and file lock.");
            Console.WriteLine($"stopped {file} epoch {owned}");
        }

        async Task AdvanceAsync(Contract.IIngress ingress, Contract.IActorReads reads, Contract.ActorRef reference, string operation,
            string[] expected)
        {
            var admitted = await ingress.SendAsync(scope, new Contract.Target.Ref(reference), operation, operation, cancellationToken: token);
            Check(admitted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } target } && target == reference,
                "The shipped edge accepts the exact actor's advance.");
            await ExpectEntriesAsync(reads, reference, expected);
        }

        Task ExpectEntriesAsync(Contract.IActorReads reads, Contract.ActorRef reference, string[] expected) =>
            WaitAsync(async () =>
            {
                var actual = await EntriesAsync(reads, reference);
                Check(actual.Length <= expected.Length && actual.SequenceEqual(expected.Take(actual.Length)),
                    $"Unexpected journal entries {string.Join(", ", actual)}.");
                return actual.Length == expected.Length;
            });

        async Task<string[]> EntriesAsync(Contract.IActorReads reads, Contract.ActorRef reference) =>
            (await reads.TailAsync(scope, reference, cancellationToken: token)).Items.Select(entry => entry.Entry).ToArray();

        async Task WaitAsync(Func<Task<bool>> condition)
        {
            while (!await condition()) await Task.Delay(25, token);
        }

        async Task GateAsync(string gate)
        {
            while (!File.Exists(Path.Combine(folder, gate))) await Task.Delay(25, token);
        }

        string ReferenceFile() => Path.Combine(folder, file + ".actor");
        Contract.ActorRef ReadReference() => new(File.ReadAllText(ReferenceFile()));
        string Connection(bool readOnly) => new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true
        }.ToString();

        async Task<(string? Owner, long Epoch)> OwnershipAsync()
        {
            await using var db = new SqliteConnection(Connection(readOnly: true));
            await db.OpenAsync(token);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """SELECT "Owner", "Epoch" FROM "Partitions" WHERE "Id" = 0""";
            await using var reader = await cmd.ExecuteReaderAsync(token);
            Check(await reader.ReadAsync(token), "The shipped runtime has partition zero.");
            return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt64(1));
        }

        async Task<string> ReadVersionRowsAsync()
        {
            await using var db = new SqliteConnection(Connection(readOnly: true));
            await db.OpenAsync(token);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                SELECT group_concat("Id" || '=' || "StoreSchema" || '/' || "MessageProtocol" || '/' || "JournalCodec", ' ')
                FROM (SELECT * FROM "KernelStoreVersions" ORDER BY "Id")
                """;
            return (string)(await cmd.ExecuteScalarAsync(token))!;
        }

        async Task<string> JournalPrefixAsync()
        {
            await using var db = new SqliteConnection(Connection(readOnly: true));
            await db.OpenAsync(token);
            return await HashRowsAsync(db, null, """SELECT * FROM "Journal" WHERE "Partition" = 0 AND "Position" <= 2""");
        }

        async Task<string> FingerprintAsync(bool live)
        {
            await using var db = new SqliteConnection(Connection(readOnly: true));
            await db.OpenAsync(token);
            await using var read = db.BeginTransaction(deferred: true);
            var schema = await HashRowsAsync(db, read, "SELECT * FROM sqlite_schema");
            await using var cmd = db.CreateCommand();
            cmd.Transaction = read;
            cmd.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table' ORDER BY name";
            var names = new List<string>();
            await using (var reader = await cmd.ExecuteReaderAsync(token))
                while (await reader.ReadAsync(token)) names.Add(reader.GetString(0));
            var rows = new List<string> { schema };
            foreach (var table in names)
            {
                // Heartbeats may advance the lease while the same process owns the file. The node and readable versions still count.
                var skipped = live && table == "Nodes" ? new[] { "SeenAt", "LeaseUntil" } : [];
                rows.Add(await HashRowsAsync(db, read, $"SELECT * FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"", skipped));
            }
            return Digest(rows);
        }

        async Task<string> HashRowsAsync(SqliteConnection db, SqliteTransaction? transaction, string sql, string[]? skipped = null)
        {
            await using var cmd = db.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync(token);
            var columns = Enumerable.Range(0, reader.FieldCount).Where(index => skipped?.Contains(reader.GetName(index)) != true).ToArray();
            var rows = new List<string>();
            while (await reader.ReadAsync(token))
                rows.Add(JsonSerializer.Serialize(columns.Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i))));
            rows.Sort(StringComparer.Ordinal);
            return Digest(rows);
        }
    }

    private static string Digest(IEnumerable<string> rows) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows))));

    private static void Escalate(string reason) => throw new InvalidOperationException(reason);

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => ActorType;
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var message = turn.Inbox.Single();
            var prior = turn.State is null ? 0 : int.Parse(turn.State, CultureInfo.InvariantCulture);
            Check(prior == (message.Kind switch
            {
                Contract.Kinds.Start => 0,
                "after-lock-refusal" => 1,
                "floor-turn" => 2,
                _ => -1
            }), "The shipped owner must see the previous committed checkpoint.");
            turn.State = (prior + 1).ToString(CultureInfo.InvariantCulture);
            turn.Record(message.Kind == Contract.Kinds.Start ? "seed" : message.Kind);
            return ValueTask.CompletedTask;
        }
    }
}

using System.Globalization;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// Built against each archive's own shipped format. All installation, admission and owner turns go through that runtime.
internal static class PostgresShippedCompatibilityProcess
{
    private const string ActorType = "postgres-format-rehearsal";
    private static readonly string[] SeedEntries = ["\"start\"", "\"seed\""];
    private static readonly string[] ContinuedEntries = [.. SeedEntries, "\"after-refusal\""];

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args is not [var role, var directory, var file and ("prior" or "candidate"), var fence and ("checked" or "in-batch")] ||
            !File.Exists(Path.Combine(directory, ".postgres-compatibility-rehearsal")))
            throw new ArgumentException("Run the PostgreSQL shipped compatibility script with its disposable container.");

        var port = Environment.GetEnvironmentVariable("KERNEL_COMPAT_POSTGRES_PORT");
        var password = Environment.GetEnvironmentVariable("KERNEL_COMPAT_POSTGRES_PASSWORD");
        Check(int.TryParse(port, out var portNumber) && portNumber is > 1024 and <= 65535 &&
            password is { Length: 48 } && password.All(Uri.IsHexDigit), "A disposable PostgreSQL container is required.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = portNumber,
            Database = DatabaseName(fence, file),
            Username = "postgres",
            Password = password,
            Timeout = 5
        }.ConnectionString).Options;
        var scope = EmbeddedTenant.Bind("compatibility");
        var inBatch = fence == "in-batch";
        var loop = new LoopOptions { BalanceEvery = TimeSpan.FromMilliseconds(500), PollInterval = TimeSpan.FromMilliseconds(100),
            Window = TimeSpan.FromSeconds(3) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        KernelDb Open() => PostgresPreviewHosting.CreateShippedContext(options);
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch);
        Console.WriteLine($"process {Environment.ProcessId} {role} reads {StoreFormat.RootShipped}");

        try
        {
            switch (role)
            {
                case "seed":
                    await using (var empty = Open())
                        Check(await PostgresFixtureDatabase.IsEmptyAsync(empty, token), "The seed owner requires an empty rehearsal database.");
                    await InstallSeedAsync(options, token);
                    await RunOwnerAsync(seed: true);
                    break;
                case "resume":
                    await RunOwnerAsync(seed: false);
                    break;
                case "install":
                    await PostgresPreviewHosting.InstallShippedAsync(options, token);
                    break;
                case "cold-owner":
                    await using (var node = await StartAsync("incompatible-owner"))
                        await node.StopAsync(token);
                    break;
                case "take":
                    await store.TakeAsync(ReadPartition(), "incompatible-take", TimeSpan.FromSeconds(30), token);
                    break;
                case "sweeper":
                    Console.WriteLine($"swept {await store.SweepAsync(token)}");
                    break;
                case "projector":
                    var partition = ReadPartition();
                    await new Projector(store, partition).CatchUpAsync(token);
                    var projected = await store.ProjectedAsync(partition, token);
                    Check(projected.Position == projected.Written && projected.Position > 0 &&
                        (await store.ReadViewsAsync([(await AddressAsync()).Instance], token)) is [{ Revision: > 0 }],
                        "The shipped projector reaches the real actor's journal position.");
                    Console.WriteLine($"projected through {projected.Position}");
                    break;
                case "edge":
                    var edge = await OpenEdgeAsync();
                    var list = await edge.ListAsync(scope, new Contract.ActorQuery(Type: ActorType), cancellationToken: token);
                    Check(list.Items is [var item] && item.Header.Ref == ReadReference(), "The shipped edge lists the exact actor.");
                    Console.WriteLine("listed the exact actor");
                    break;
                case "journal":
                    var journal = await store.ReadRootJournalAsync(scope.Tenant, ReadReference().Id, 0, 64, token);
                    Check(journal.Rows.Count > 0, "The shipped journal reads the exact actor.");
                    Console.WriteLine("read the exact actor journal");
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
        catch (InvalidOperationException error) when (role == "install" && error.InnerException is StoreFormatException format)
        {
            Console.WriteLine($"refused format {role} {format.Message}");
            return 8;
        }

        static Task InstallSeedAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken)
        {
            // This same fixture also compiles against archives that predate the typed Event host install.
            var typedInstall = typeof(PostgresPreviewHosting).GetMethod(nameof(PostgresPreviewHosting.InstallShippedAsync),
                [typeof(DbContextOptions<KernelDb>), typeof(string), typeof(bool), typeof(CancellationToken)]);
            return typedInstall is null
                ? PostgresPreviewHosting.InstallShippedAsync(options, cancellationToken)
                : typedInstall.CreateDelegate<Func<DbContextOptions<KernelDb>, string, bool, CancellationToken, Task>>()
                    (options, ActorType, false, cancellationToken);
        }

        Task<PostgresPreviewFoundation> OpenEdgeAsync(PostgresDirectRequests? requests = null) =>
            PostgresPreviewHosting.OpenFoundationAsync(Open, ActorType, requests, TimeSpan.Zero, cancellationToken: token);

        Task<RuntimeNode> StartAsync(string name, PostgresDirectRequests? requests = null) =>
            PostgresPreviewHosting.StartAsync(Open, name, new Probe(), Escalate, loop, token, onFault: null,
                replies: requests ?? PostgresPreviewHosting.CreateRequests(Open, name, ActorType, TimeSpan.Zero), fenceInBatch: inBatch);

        async Task RunOwnerAsync(bool seed)
        {
            var name = role + "-" + file;
            var requests = PostgresPreviewHosting.CreateRequests(Open, name, ActorType, TimeSpan.Zero);
            await using var owner = await StartAsync(name, requests);
            var edge = await OpenEdgeAsync(requests);
            Contract.ActorRef reference;
            if (seed)
            {
                var admitted = await edge.SendAsync(scope, new Contract.Target.Start(new Contract.StartSpec(ActorType, "{}")),
                    "seed", Contract.Kinds.Start, cancellationToken: token);
                Check(admitted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "The shipped edge admits the seed.");
                reference = admitted.Actor!.Value;
                await File.WriteAllTextAsync(ReferenceFile(), reference.Id, token);
                await ExpectEntriesAsync(edge, reference, SeedEntries);
            }
            else
                reference = ReadReference();

            var (partition, _) = await AddressAsync();
            var owned = await OwnershipAsync(partition);
            while (owned.Owner != name)
            {
                await Task.Delay(25, token);
                owned = await OwnershipAsync(partition);
            }
            if (seed)
            {
                await new Projector(store, partition).CatchUpAsync(token);
                await File.WriteAllTextAsync(PartitionFile(), partition.ToString(CultureInfo.InvariantCulture), token);
                Console.WriteLine($"ready {file} partition {partition} epoch {owned.Epoch}");
                await GateAsync("continue-" + file);
                await AdvanceAsync(edge, reference, "after-refusal", ContinuedEntries);
                Check(await OwnershipAsync(partition) == owned, "The same live owner continues at the same epoch.");
                Console.WriteLine($"continued {file} partition {partition} epoch {owned.Epoch}");
                await GateAsync("stop-" + file);
            }
            else
            {
                var prefix = await EntriesAsync(edge, reference);
                Check(prefix.SequenceEqual(ContinuedEntries), "The compatible floor reads the candidate's exact journal prefix.");
                await AdvanceAsync(edge, reference, "floor-turn", [.. prefix, "\"floor-turn\""]);
                Console.WriteLine($"resumed {file} partition {partition} epoch {owned.Epoch}");
            }
            await owner.StopAsync(token);
            Check(owner.LastError is null && (await OwnershipAsync(partition)).Owner is null, "A clean stop releases the original actor's partition.");
            Console.WriteLine($"stopped {file} partition {partition} epoch {owned.Epoch}");
        }

        async Task AdvanceAsync(PostgresPreviewFoundation edge, Contract.ActorRef reference, string operation, string[] expected)
        {
            var admitted = await edge.SendAsync(scope, new Contract.Target.Ref(reference), operation, operation, cancellationToken: token);
            Check(admitted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } target } && target == reference,
                "The shipped edge accepts the exact actor's advance.");
            await ExpectEntriesAsync(edge, reference, expected);
        }

        async Task ExpectEntriesAsync(PostgresPreviewFoundation edge, Contract.ActorRef reference, string[] expected)
        {
            while (true)
            {
                var actual = await EntriesAsync(edge, reference);
                Check(actual.Length <= expected.Length && actual.SequenceEqual(expected.Take(actual.Length)),
                    $"Unexpected journal entries {string.Join(", ", actual)}.");
                if (actual.Length == expected.Length) return;
                await Task.Delay(25, token);
            }
        }

        async Task<string[]> EntriesAsync(PostgresPreviewFoundation edge, Contract.ActorRef reference) =>
            (await edge.TailAsync(scope, reference, cancellationToken: token)).Items.Select(entry => entry.Entry).ToArray();

        async Task<(int Partition, string Instance)> AddressAsync()
        {
            await using var db = Open();
            var id = ReadReference().Id;
            return (await db.Database.SqlQuery<ActorAddress>($"""
                SELECT "Partition", "Id" AS "Instance" FROM "Instances" WHERE "FoundationReference" = {id}
                """).ToListAsync(token)).Select(row => (row.Partition, row.Instance)).Single();
        }

        async Task<(string? Owner, long Epoch)> OwnershipAsync(int partition)
        {
            await using var db = Open();
            var row = await db.Partitions.AsNoTracking().SingleAsync(item => item.Id == partition, token);
            return (row.Owner, row.Epoch);
        }

        async Task GateAsync(string gate)
        {
            while (!File.Exists(Path.Combine(directory, gate))) await Task.Delay(25, token);
        }

        string ReferenceFile() => Path.Combine(directory, file + ".actor");
        Contract.ActorRef ReadReference() => new(File.ReadAllText(ReferenceFile()));
        string PartitionFile() => Path.Combine(directory, file + ".partition");
        int ReadPartition() => int.Parse(File.ReadAllText(PartitionFile()), CultureInfo.InvariantCulture);
    }

    private static string DatabaseName(string fence, string file) => $"kernel_compat_{fence.Replace('-', '_')}_{file}";
    private static void Escalate(string reason) => throw new InvalidOperationException(reason);

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    private sealed record ActorAddress(int Partition, string Instance);

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
                "after-refusal" => 1,
                "floor-turn" => 2,
                _ => -1
            }), "The owner must see the previous committed checkpoint.");
            turn.State = (prior + 1).ToString(CultureInfo.InvariantCulture);
            turn.Record(message.Kind == Contract.Kinds.Start ? "seed" : message.Kind);
            return ValueTask.CompletedTask;
        }
    }
}

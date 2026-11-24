using System.Globalization;
using Kernel.EntityFramework;
using Kernel.Native.Run;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Ef.Conformance;

// This driver is compiled against each revision's actual EF store, Worker, and Projector.
internal static class FormatRehearsalProcess
{
    private static readonly InstanceRef Actor = InstanceRef.In(0, "format-rehearsal");
    private const string Model = "format-fixture";

    internal static async Task<int> RunAsync(string[] args, Func<EfStore, CancellationToken, Task<bool>>? advance = null)
    {
        if (args is not [var role and ("old-owner" or "new-owner" or "floor-owner" or "cold-owner" or "take" or "sweeper" or "projector" or "migrate")])
            throw new ArgumentException("Supply a rehearsal role.");
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use the dedicated rehearsal database.");
        var gates = Environment.GetEnvironmentVariable("KERNEL_REHEARSAL_GATES");
        Check(Directory.Exists(gates), "Supply the existing rehearsal gate directory.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => new(options);
        var store = new EfStore(Open, postgres: true, fenceInBatch: true) { CommitSql = CommitSql.Owned };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        var stop = timeout.Token;
        Console.WriteLine($"process {Environment.ProcessId} {role} reads {StoreFormat.Current}");
        try
        {
            switch (role)
            {
                case "old-owner":
                case "new-owner":
                case "floor-owner":
                    await HostAsync(role);
                    break;
                case "cold-owner":
                    const string node = "rehearsal-cold";
                    var runtime = new PartitionRuntime(node, store, 2, Defaults.Lease);
                    await runtime.StartAsync(DurableHost.ForNode(node, store, new Probe(), 2), stop);
                    break;
                case "take":
                    var lease = await store.TakeAsync(1, "rehearsal-take", Defaults.Lease, cancellationToken: stop);
                    if (lease is not null) await store.ReleaseAsync(1, lease.Epoch, stop);
                    break;
                case "sweeper":
                    Console.WriteLine($"swept {await store.SweepAsync(stop)}");
                    break;
                case "projector":
                    await new Projector(store, 0).CatchUpAsync(stop);
                    break;
                case "migrate":
                    if (advance is null) throw new InvalidOperationException("Use the upgraded coordinator to migrate.");
                    Console.WriteLine(await advance(store, stop) ? "advanced" : "already current");
                    break;
            }
            return 0;
        }
        catch (StoreFormatException error)
        {
            Console.WriteLine($"refused {role} {error.Message}");
            return 8;
        }

        async Task HostAsync(string name)
        {
            var old = name == "old-owner";
            if (old)
            {
                await using var db = Open();
                Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop),
                    "Use an empty database dedicated to the format rehearsal.");
                await store.CreateAsync(2);
            }
            await using var worker = new Worker(name, 0, store, new Probe(),
                new() { Lease = TimeSpan.FromSeconds(old ? 18 : 60) });
            Check(ReferenceEquals(await new DurableHost([worker]).ActivateAsync(0, stop), worker),
                "The fixture worker must acquire partition zero.");
            var epoch = worker.Fences.Single().Epoch;
            var (first, revision) = name switch
            {
                "old-owner" => ("seed", 1),
                "new-owner" => ("first-new", 2),
                _ => ("floor-turn", 4)
            };
            await ApplyAsync(first, revision, birth: old, keep: name != "floor-owner");
            Console.WriteLine($"ready {name} epoch {epoch}");
            if (name == "new-owner")
            {
                await WaitForGateAsync("continue-new");
                await ApplyAsync("second-new", 3, birth: false, keep: false);
                Check(worker.Fences.Single().Epoch == epoch, "The upgraded Worker must keep its ownership across the refusal probes.");
                Console.WriteLine($"continued {name} epoch {epoch}");
            }
            await WaitForGateAsync($"stop-{name}");

            async Task ApplyAsync(string id, long revision, bool birth, bool keep)
            {
                Check(await store.AddAsync(new(id, Actor, birth ? Kinds.Start : "advance",
                    Start: birth ? new(Model, Model) : null, KeepUntil: keep ? 1 : null), stop), "Admission must be unique.");
                await using var observer = Open();
                while (await observer.Instances.AsNoTracking().Where(row => row.Id == Actor.Id)
                    .Select(row => row.Revision).SingleOrDefaultAsync(stop) < revision)
                {
                    await worker.WokenAsync(null, stop);
                    await worker.RunAsync(stop);
                    Check(worker.Owns && worker.Fences.Single().Epoch == epoch, "The same Worker must apply the turn.");
                    await Task.Delay(10, stop);
                }
            }

            async Task WaitForGateAsync(string gate)
            {
                while (!File.Exists(Path.Combine(gates!, gate))) await Task.Delay(20, stop);
            }
        }
    }

    private sealed class Probe : IActorType
    {
        public bool Withholds(InstanceRow row, ITurnServices services) => false;
        public TurnOutcome? Command(TurnInput input) => null;
        public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error) =>
            throw new InvalidOperationException(error);

        public ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The rehearsal does no job work.");

        public ValueTask<TurnOutcome> TurnAsync(TurnInput input)
        {
            var turn = new DurableTurn(input);
            var id = turn.Inbox.Single().Id;
            var row = input.Row ?? new(input.Actor, Model, Model, InstanceStatus.Running, "0", 0, null);
            Check(row.State == row.Revision.ToString(CultureInfo.InvariantCulture), "The decoded checkpoint must match its stored revision.");
            row = row with { State = turn.Revision.ToString(CultureInfo.InvariantCulture), Revision = turn.Revision };
            return ValueTask.FromResult(turn.Seal(turn.Outcome(row.Status), row, row.State, Json.Write(new[] { id })));
        }
    }

    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}

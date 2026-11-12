using System.Collections.Concurrent;
using System.Diagnostics;
using Kernel;
using Kernel.Sim;

// Usage:
//   dotnet run -c Release --project sim -- --seeds 10000 [--from 1] [--actions 300] [--flaw Name[,Name]]
//   dotnet run -c Release --project sim -- --seed 42 [--actions 300] [--flaw Name]      replay one seed; prints a shrunk trace if it fails
//   dotnet run -c Release --project sim -- --mutations [--seeds 2000]                   break the kernel on purpose and see who notices

string? Arg(string name) => Array.IndexOf(args, name) is >= 0 and var i && i + 1 < args.Length ? args[i + 1] : null;
var actions = int.Parse(Arg("--actions") ?? "300");
var flaws = (Arg("--flaw") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Aggregate(Flaw.None, (all, name) => all | Enum.Parse<Flaw>(name, true));

if (args.Contains("--mutations")) return Mutations(int.Parse(Arg("--seeds") ?? "2000"));
if (Arg("--seed") is { } one) return Replay(int.Parse(one), flaws) ? 0 : 1;
return Sweep(int.Parse(Arg("--from") ?? "1"), int.Parse(Arg("--seeds") ?? "10000"), flaws) ? 0 : 1;

static Config ConfigOf(int seed, Flaw flaws) => new(2 + seed % 2, 2, flaws);

(string? Violation, List<Act> Acts) Run(int seed, Flaw flaws)
{
    var world = new World(ConfigOf(seed, flaws));
    var generator = new Generator(seed, world);
    var acts = new List<Act>();
    while (acts.Count < actions)
    {
        var act = generator.Next();
        if (world.Apply(act) is null) continue;     // not enabled now: choose again
        acts.Add(act);
        generator.Applied(act);
        if (world.Check() is { } violation) return (violation, acts);
    }
    return (world.Settle(), acts);
}

// The same acts again, without the generator. Acts that are not enabled any more (because others were removed) are skipped.
static string? Again(Config config, List<Act> acts, Action<string>? log = null)
{
    var world = new World(config) { Log = log };
    var n = 0;
    foreach (var act in acts)
    {
        if (world.Apply(act) is not { } outcome) continue;
        log?.Invoke($"{++n,4}  {act}{(outcome == "" ? "" : "  =>  " + outcome)}");
        if (world.Check() is { } violation) return violation;
    }
    log?.Invoke("      (faults stop; the system runs until idle)");
    return world.Settle();
}

// Remove acts while the same invariant still breaks: chunks first, then one by one.
static List<Act> Shrink(Config config, List<Act> acts, string code)
{
    for (var size = Math.Max(1, acts.Count / 2); ; size /= 2)
    {
        for (var removed = true; removed;)
        {
            removed = false;
            for (var i = 0; i + size <= acts.Count;)
            {
                var fewer = acts.Take(i).Concat(acts.Skip(i + size)).ToList();
                if (Again(config, fewer)?.Split(' ')[0] == code) (acts, removed) = (fewer, true);
                else i += size;
            }
        }
        if (size == 1) return acts;
    }
}

bool Replay(int seed, Flaw flaws)
{
    var (violation, acts) = Run(seed, flaws);
    if (violation is null)
    {
        Console.WriteLine($"seed {seed}: {acts.Count} actions, then idle: all invariants held");
        return true;
    }
    Console.WriteLine($"seed {seed} (flaws: {flaws}) fails after {acts.Count} actions:\n  {violation}\n");
    var config = ConfigOf(seed, flaws);
    var small = Shrink(config, acts, violation.Split(' ')[0]);
    Console.WriteLine($"shrunk to {small.Count} acts ({config.Nodes} nodes, {config.Partitions} partitions; actor = node * {config.Partitions} + partition until a restart adds one):");
    var again = Again(config, small, Console.WriteLine);
    Console.WriteLine($"\n  {again}");
    return false;
}

bool Sweep(int from, int seeds, Flaw flaws)
{
    var clock = Stopwatch.StartNew();
    var failures = new ConcurrentBag<(int Seed, string Violation)>();
    long acts = 0;
    Parallel.For(from, from + seeds, seed =>
    {
        var (violation, done) = Run(seed, flaws);
        Interlocked.Add(ref acts, done.Count);
        if (violation is not null) failures.Add((seed, violation));
    });
    Console.WriteLine($"{seeds} seeds from {from}, {acts} actions, {clock.Elapsed.TotalSeconds:F1} s, flaws: {flaws}: {failures.Count} failed");
    foreach (var group in failures.GroupBy(f => f.Violation.Split(' ')[0]).OrderByDescending(g => g.Count()))
    {
        var first = group.MinBy(f => f.Seed);
        Console.WriteLine($"  {group.Key}: {group.Count()} seeds, first {first.Seed}: {first.Violation}");
    }
    return failures.IsEmpty;
}

int Mutations(int seeds)
{
    Flaw[] mutations =
    [
        Flaw.FenceWithoutEpoch, Flaw.FenceWithoutPosition, Flaw.FenceWithoutPosition | Flaw.NoResetAfterFailedCommit, Flaw.CommitDespiteFence,
        Flaw.NoResetAfterFailedCommit, Flaw.DeliverBeforeCommit, Flaw.PollWithoutSkip, Flaw.KeepConsumedMessages, Flaw.DeleteWithoutCount,
        Flaw.WallClockTimers, Flaw.DeleteOutsideCommands, Flaw.RunBeforeMark, Flaw.VolatileRunTicket, Flaw.MeetWithoutTicket, Flaw.PollRace,
    ];
    var missed = 0;
    foreach (var mutation in mutations)
    {
        var clock = Stopwatch.StartNew();
        (int Seed, string Violation)? caught = null;
        long acts = 0;
        for (var from = 1; caught is null && from <= seeds; from += 64)
        {
            var found = new ConcurrentBag<(int Seed, string Violation)>();
            Parallel.For(from, Math.Min(from + 64, seeds + 1), seed =>
            {
                var (violation, done) = Run(seed, mutation);
                Interlocked.Add(ref acts, done.Count);
                if (violation is not null) found.Add((seed, violation));
            });
            if (!found.IsEmpty) caught = found.MinBy(f => f.Seed);
        }
        if (caught is null) missed++;
        Console.WriteLine(caught is { } c
            ? $"{mutation}: CAUGHT at seed {c.Seed} after {acts} actions, {clock.ElapsedMilliseconds} ms: {c.Violation}"
            : $"{mutation}: NOT CAUGHT in {seeds} seeds ({acts} actions, {clock.ElapsedMilliseconds} ms)");
    }
    return missed;
}

using System.Collections.Concurrent;
using System.Diagnostics;
using Kernel.Sim;

if (args is ["--park-operator-demo"]) return await ParkOperatorDemo.RunAsync();

// Usage:
//   dotnet run -c Release --project sim -- --seeds 10000 [--from 1] [--actions 300] [--flaw Name[,Name]] [--count 1024|4096]
//   dotnet run -c Release --project sim -- --seed 42 [--actions 300] [--flaw Name]      replay one seed; prints a shrunk trace if it fails
//   dotnet run -c Release --project sim -- --mutations [--seeds 2000]                   break the kernel on purpose and see who notices
//   dotnet run -c Release --project sim -- --node-lease-mutations                       the node lease's mutations, each against its witnesses

// A named mutation must select exactly its own bit. Aliases make unrelated controls run together.
var namedFlaws = Enum.GetNames<Flaw>().Where(name => name != nameof(Flaw.None)).ToArray();
foreach (var name in namedFlaws)
{
    var bit = unchecked((ulong)(long)Enum.Parse<Flaw>(name));
    if (bit == 0 || (bit & (bit - 1)) != 0)
        throw new InvalidOperationException($"Mutation {name} must have one nonzero bit");
}
var aliases = namedFlaws.GroupBy(name => Enum.Parse<Flaw>(name)).FirstOrDefault(group => group.Count() > 1);
if (aliases is not null) throw new InvalidOperationException($"Mutations share a bit: {string.Join(", ", aliases)}");
if (args.Contains("--check-flaws"))
{
    Console.WriteLine($"{namedFlaws.Length} named kernel mutations have distinct individual bits");
    return 0;
}

string? Arg(string name) => Array.IndexOf(args, name) is >= 0 and var i && i + 1 < args.Length ? args[i + 1] : null;
var actions = int.Parse(Arg("--actions") ?? "300");
// K1 slice 7: the store's virtual partitions, a fence row and a journal each. A world places the first few of them.
var count = int.Parse(Arg("--count") ?? VirtualPartitions.Provisional.ToString(System.Globalization.CultureInfo.InvariantCulture));
Generator.Faults = double.Parse(Arg("--faults") ?? "1", System.Globalization.CultureInfo.InvariantCulture);
var flaws = (Arg("--flaw") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Aggregate(Flaw.None, (all, name) => all | Enum.Parse<Flaw>(name, true));

NativeSwitch.On = args.Contains("--native");
NativeSwitch.Flaws = (Arg("--native-flaw") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Aggregate(Kernel.Native.NativeFlaw.None, (all, name) => all | Enum.Parse<Kernel.Native.NativeFlaw>(name, true));
if (args.Contains("--contract-http"))
    return await ContractHttpWorkload.Run(int.Parse(Arg("--seed") ?? Arg("--from") ?? "1"),
        Arg("--seed") is not null ? 1 : int.Parse(Arg("--seeds") ?? "100"), count, flaws);
if (args.Contains("--contract-evolution"))
    return await ContractActorWorkload.RunEvolution(int.Parse(Arg("--seed") ?? Arg("--from") ?? "1"),
        Arg("--seed") is not null ? 1 : int.Parse(Arg("--seeds") ?? "100"), count, flaws);
if (args.Contains("--contract-actors"))
    return await ContractActorWorkload.Run(int.Parse(Arg("--seed") ?? Arg("--from") ?? "1"),
        Arg("--seed") is not null ? 1 : int.Parse(Arg("--seeds") ?? "100"), actions, count, flaws);
if (args.Contains("--contract-native"))
    return await ContractNativeWorkload.Run(int.Parse(Arg("--seed") ?? Arg("--from") ?? "1"),
        Arg("--seed") is not null ? 1 : int.Parse(Arg("--seeds") ?? "100"), count, flaws, NativeSwitch.Flaws);
if (args.Contains("--contract-events"))
    return await ContractEventWorkload.Run(int.Parse(Arg("--seed") ?? Arg("--from") ?? "1"),
        Arg("--seed") is not null ? 1 : int.Parse(Arg("--seeds") ?? "100"), count, flaws);
if (args.Contains("--contract"))
    return await ContractWorkload.Run(int.Parse(Arg("--seed") ?? Arg("--from") ?? "1"),
        Arg("--seed") is not null ? 1 : int.Parse(Arg("--seeds") ?? "100"), actions, count, flaws);
if (args.Contains("--inline-ephemeral-check"))
{
    var failed = false;
    await InlineEphemeralChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; }, flaws);
    return failed ? 1 : 0;
}
if (args.Contains("--job-result-size-check"))
{
    var failed = false;
    await JobResultSizeChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; }, flaws);
    return failed ? 1 : 0;
}
if (args.Contains("--held-job-result-check"))
{
    var failed = false;
    await HeldJobResultChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; }, flaws);
    return failed ? 1 : 0;
}
if (args.Contains("--park-control-identity-check"))
{
    var failed = false;
    await ParkHandoffCreditChecks.CheckControlIdentities((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-rotating-waits-check"))
{
    await ParkRotatingWaitChecks.Run();
    return 0;
}
if (args.Contains("--park-coverage-budget-check"))
{
    var failed = false;
    await ParkCoverageBudgetChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-page-bounds-check"))
{
    var failed = false;
    await ParkPageBoundsChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-large-drain-check"))
{
    var failed = false;
    await ParkLargeDrainChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-cached-release-check"))
{
    var failed = false;
    await ParkCachedReleaseChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-kept-check"))
{
    var failed = false;
    await ParkKeptChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-recovery-check"))
{
    var failed = false;
    await ParkKeptChecks.CheckRecoverArrivals((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-retry-check"))
{
    var failed = false;
    await ParkRetryChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-recovery-native-check"))
{
    var failed = false;
    await ContractNativeChecks.CheckRecoveryTimers((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    await ParkKeptChecks.CheckInvalidTimerRecoveryPlan((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-recovery-kept-event-check"))
{
    var failed = false;
    await ParkKeptChecks.CheckRecoverKeptEvents((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-recovery-subscription-check"))
{
    var failed = false;
    await ContractNativeChecks.CheckRecoveredSubscription((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-recovery-job-check"))
{
    var failed = false;
    await ParkKeptChecks.CheckRecoverJobResult((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--private-kinds-check"))
{
    var failed = false;
    await PrivateKindsChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--sequential-cascade-check"))
{
    var failed = false;
    await SequentialCascadeChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--public-http-edge-check"))
{
    try { return await NativeHttpEdgeChecks.RunAsync(flaws); }
    catch (InvalidOperationException error)
    {
        Console.Error.WriteLine(error.Message);
        return 1;
    }
}
if (args.Contains("--public-http-remote-check"))
{
    try { return await NativeHttpEdgeChecks.RunRemoteAsync(flaws); }
    catch (InvalidOperationException error)
    {
        Console.Error.WriteLine(error.Message);
        return 1;
    }
}
if (args.Contains("--native-detached-handles-check"))
{
    var failed = false;
    await NativeDetachedHandleChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--cascade-pages-check"))
{
    var failed = false;
    await CascadePageChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-cohort-tuple-check"))
{
    var failed = false;
    await ParkCohortTupleChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--tenant-pass-check"))
{
    var failed = false;
    await TenantPassChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--scope-age-check"))
{
    var failed = false;
    await ScopeAgeChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--backlog-worker-check"))
{
    var failed = false;
    await BacklogWorkerChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--backlog-store-check"))
{
    var failed = false;
    await BacklogStoreChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--pass-turn-budget-check"))
{
    var failed = false;
    await PassTurnBudgetChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-handoff-credit-check"))
{
    var failed = false;
    await ParkHandoffCreditChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-drain-cascade-check"))
{
    var failed = false;
    await ParkDrainCascadeChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--foundation-value-check"))
{
    var failed = false;
    await FoundationValueChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--workflow-run-history-check"))
{
    var failed = false;
    await WorkflowRunHistoryChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--retention-pass-check"))
{
    var failed = false;
    await RetentionPassChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--retention-delete-check"))
{
    var failed = false;
    await RetentionDeleteChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--park-delete-check"))
{
    var failed = false;
    await ParkDeleteChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--retirement-cascade-check"))
{
    var failed = false;
    await RetirementCascadeChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--metadata-envelope-check"))
{
    var failed = false;
    await MetadataEnvelopeChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--event-byte-budget-check"))
{
    var failed = false;
    await EventByteBudgetChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--broadcast-cardinality-check"))
{
    var failed = false;
    await EventIdentityChecks.CheckLargeBroadcast((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--event-identity-check"))
{
    var failed = false;
    await EventIdentityChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--paged-publication-check"))
{
    var failed = false;
    await FoundationEventSendChecks.CheckPagedPublication((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}

if (args.Contains("--contract-jobs"))
    return await ContractActorWorkload.RunJobTree(int.Parse(Arg("--seed") ?? Arg("--from") ?? "1"),
        Arg("--seed") is not null ? 1 : int.Parse(Arg("--seeds") ?? "100"), count, flaws, args.Contains("--tree-stall"));
if (args.Contains("--foundation-tenant-check"))
{
    var failed = false;
    await FoundationTenantChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--host-drain-check"))
    return await HostDrainChecks.RunAsync();
if (args.Contains("--foundation-migration-dispatch-check"))
{
    var failed = false;
    await FoundationMigrationDispatchChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--migration-plan-dispatch"))
{
    try { await MigrationPlanDispatchChecks.RunAsync(); return 0; }
    catch (InvalidOperationException error) { Console.Error.WriteLine(error.Message); return 1; }
}

if (args.Contains("--native-migration-validation"))
{
    var failed = false;
    await StructuredMigrationChecks.RunAsync((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}

if (args.Contains("--native-migration-apply"))
{
    var failed = false;
    await NativeMigrationApplyChecks.RunAsync((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}

if (args.Contains("--migration-plan-actor"))
{
    var failed = false;
    await MigrationPlanActorChecks.RunAsync((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}

if (args.Contains("--migration-plan-runner"))
{
    var failed = false;
    await MigrationPlanRunnerChecks.RunAsync((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}

if (args.Contains("--migration-plan-reads"))
{
    try { await MigrationPlanReadChecks.RunAsync(); return 0; }
    catch (InvalidOperationException error) { Console.Error.WriteLine(error.Message); return 1; }
}


if (args.Contains("--native-direct-preview-check"))
{
    var failed = false;
    await DirectApprovalPreviewChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; });
    return failed ? 1 : 0;
}
if (args.Contains("--public-owner-check"))
{
    var failed = false;
    await PublicOwnerChecks.Run((ok, name) => { Console.WriteLine($"{(ok ? "ok" : "FAIL")} {name}"); failed |= !ok; }, flaws);
    return failed ? 1 : 0;
}

if (args.Contains("--lease-check")) return await LeaseChecks.Run(flaws);
if (args.Contains("--job-trial-check")) return await ContractActorWorkload.RunJobTrialChecks(count, flaws);
if (args.Contains("--landed-check")) return LandedChecks.Run();
if (args.Contains("--placement-check")) return PlacementChecks.Run(flaws);
if (args.Contains("--native-mutations")) return NativeMutations(int.Parse(Arg("--seeds") ?? "2000"));
if (args.Contains("--mutations")) return Mutations(int.Parse(Arg("--seeds") ?? "2000"));
if (args.Contains("--node-lease-mutations")) return await NodeLeaseMutations();
if (Arg("--backlog") is { } backlog) return Backlog(int.Parse(backlog));
if (Arg("--seed") is { } one) return Replay(int.Parse(one), flaws) ? 0 : 1;
return Sweep(int.Parse(Arg("--from") ?? "1"), int.Parse(Arg("--seeds") ?? "10000"), flaws) ? 0 : 1;

// Every fifth seed tries suspects in groups of four that halve, with a threshold to match; the others one at a time.
// Every third seed is a rolling upgrade: counters start at state version 1 and each node moves to 2 when it restarts.
// K1 slice 7: the store has `count` virtual partitions, and the world places four of them on its two or three nodes. Each
// node runs one worker for its owned set, through DurableHost.ForNode; every seventh seed runs a worker per partition
// instead, the host of a store without owned sets (commit=ef), so both shapes stay under the mutations.
Config ConfigOf(int seed, Flaw flaws) => (flaws.HasFlag(Flaw.StableNodeName) ? new(1, Placed, flaws) : seed % 5 == 0 ? new(2 + seed % 2, Placed, flaws, Attempts: 5, Group: 4) : new Config(2 + seed % 2, Placed, flaws))
    with { Upgrade = seed % 3 == 0, Native = NativeSwitch.On, NativeFlaws = NativeSwitch.Flaws, Count = count, Set = seed % 7 != 0 };

(string? Violation, List<Act> Acts) Run(int seed, Flaw flaws, int? length = null)
{
    var world = new World(ConfigOf(seed, flaws));
    var generator = new Generator(seed, world);
    var acts = new List<Act>();
    while (acts.Count < (length ?? actions))
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
    Console.WriteLine(config.Set
        ? $"shrunk to {small.Count} acts ({config.Nodes} nodes, {config.Partitions} of {config.Rows} virtual partitions; actor = node, one worker for its set, until a restart adds one):"
        : $"shrunk to {small.Count} acts ({config.Nodes} nodes, {config.Partitions} of {config.Rows} virtual partitions; actor = node * {config.Partitions} + partition until a restart adds one):");
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
    Console.WriteLine($"{seeds} seeds from {from} at {count} virtual partitions, {acts} actions, {clock.Elapsed.TotalSeconds:F1} s, flaws: {flaws}: {failures.Count} failed; {World.Bystanders} innocent instances parked at the threshold"
        + (NativeSwitch.On ? $"; native: {World.NativeInstances} finished instances matched a run without faults, {World.NativeReruns} External effects ran again under the same committed attempt ({World.NativeRerunsFlagged} of them told that their turn may have run before), {World.NativeSameKeyReruns} outbound effects were sent again with the key of their first sending, {World.NativeReusedFrames} under a frame number another activation had sent under, {World.NativeRevisits} from a node that had sent before under another iteration path" : ""));
    if (args.Contains("--coverage")) Console.WriteLine("  coverage (seeds that reached the end): " + string.Join("; ", World.Seen.OrderBy(s => s.Key).Select(s => $"{s.Key}: {s.Value}")));
    foreach (var group in failures.GroupBy(f => f.Violation.Split(' ')[0]).OrderByDescending(g => g.Count()))
    {
        var first = group.MinBy(f => f.Seed);
        Console.WriteLine($"  {group.Key}: {group.Count()} seeds, first {first.Seed}: {first.Violation}");
    }
    return failures.IsEmpty;
}

// What it costs to recover the messages a dead owner held: commits until idle, for trial groups of different sizes.
int Backlog(int messages)
{
    foreach (var group in new[] { 1, 8, 64 })
    {
        var time = 1_000_000L;
        var store = new InMemoryStore(1, () => time);
        var commits = 0;
        var counting = new CountingStore(store, () => commits++);
        var options = new WorkerOptions { TrialGroup = group, MaxAttempts = 10, Monotonic = () => time };
        var first = new Worker("first", 0, counting, new WorkflowInstance([new Counter()]), options);
        new DurableHost([first]).ActivateAsync(0).AsTask().Wait();
        for (var i = 0; i < messages; i++) store.AddAsync(new Message("x" + i, InstanceRef.In(0, "c" + i), "add", Json.Write(new Add("t" + i)), Start: Counter.Spec)).AsTask().Wait();
        first.PollAsync().Wait();
        first.FlushAsync().Wait();      // marked, and then the owner dies with all of them in memory
        time += 60_000;
        commits = 0;
        var second = new Worker("second", 0, counting, new WorkflowInstance([new Counter()]), options);
        new DurableHost([second]).ActivateAsync(0).AsTask().Wait();
        while (store.Messages.Count > 0)
        {
            second.PollAsync().Wait();
            second.LoadAsync().Wait();
            foreach (var id in second.Ready.ToList()) second.RunTurn(id);
            second.FlushAsync().Wait();
        }
        Console.WriteLine($"{messages} messages held by a dead owner, trial group {group}: {commits} commits to recover them");
    }
    return 0;
}

// The native model broken on purpose, one flaw at a time, with native instances in the mix (NATIVE.md, section 5).
int NativeMutations(int seeds)
{
    NativeSwitch.On = true;
    var missed = 0;
    foreach (var mutation in Enum.GetValues<Kernel.Native.NativeFlaw>().Where(f => f != Kernel.Native.NativeFlaw.None))
    {
        NativeSwitch.Flaws = mutation;
        var clock = Stopwatch.StartNew();
        (int Seed, string Violation)? caught = null;
        long acts = 0;
        // The old partition-lease schedule caught this at 3589. The node-lease nightly reaches 14396 unaided.
        const int witness = 14396;
        if (mutation == Kernel.Native.NativeFlaw.FreshTurnSwapsLoopKeys && seeds < witness)
        {
            var (violation, done) = Run(witness, Flaw.None);
            acts += done.Count;
            if (violation is not null) caught = (witness, violation);
        }
        for (var from = 1; caught is null && from <= seeds; from += 64)
        {
            var found = new ConcurrentBag<(int Seed, string Violation)>();
            Parallel.For(from, Math.Min(from + 64, seeds + 1), seed =>
            {
                var (violation, done) = Run(seed, Flaw.None);
                Interlocked.Add(ref acts, done.Count);
                if (violation is not null) found.Add((seed, violation));
            });
            if (!found.IsEmpty) caught = found.MinBy(f => f.Seed);
        }
        // The graph engine's flaws must be caught by the check each is for (NATIVE.md section 6), not by another first.
        var expected = NativeSwitch.ExpectedCheck.GetValueOrDefault(mutation);
        var wrong = caught is { } w && expected is not null && !w.Violation.StartsWith(expected + " ", StringComparison.Ordinal);
        if (caught is null || wrong) missed++;
        Console.WriteLine(caught is { } c
            ? $"{mutation}: CAUGHT at seed {c.Seed} after {acts} actions, {clock.ElapsedMilliseconds} ms: {c.Violation}{(wrong ? $" -- BY THE WRONG CHECK, expected {expected}" : "")}"
            : $"{mutation}: NOT CAUGHT in {seeds} seeds ({acts} actions, {clock.ElapsedMilliseconds} ms)");
    }
    return missed;
}

int Mutations(int seeds)
{
    Flaw[] mutations =
    [
        Flaw.FenceWithoutEpoch, Flaw.FenceWithoutPosition, Flaw.FenceWithoutPosition | Flaw.NoResetAfterFailedCommit, Flaw.CommitDespiteFence,
        Flaw.NoResetAfterFailedCommit, Flaw.DeliverBeforeCommit, Flaw.PollWithoutSkip, Flaw.KeepConsumedMessages, Flaw.DeleteWithoutCount,
        Flaw.DeleteWithoutCount | Flaw.PollWithoutSkip, Flaw.EveryTakeCounts,
        // behaviours of draft 1, or of a literal reading of draft 2: the findings of FINDINGS.md
        Flaw.WallClockTimers, Flaw.DeleteOutsideCommands, Flaw.HandOverWhileBehind, Flaw.BumpAllInherited, Flaw.RunBeforeMark,
        Flaw.VolatileJobTicket, Flaw.PollRace, Flaw.MeetWithoutTicket,
        // round 3: the rule of each new piece
        Flaw.GreedyBalancer, Flaw.TrialPerPartition, Flaw.ProjectorBlindWrite, Flaw.EmitBeforeCommit, Flaw.ReplyTwice, Flaw.NoDeadline, Flaw.DeliverWhileSuspended, Flaw.NoShelf, Flaw.SuspendDuringTrial, Flaw.TerminateRootOnly, Flaw.HoldLoses, Flaw.HoldKeepsTrial, Flaw.SkipUpgrade, Flaw.TurnNewerState, Flaw.RebindForgotten, Flaw.StartWithoutRead, Flaw.CancelKnownChildren, Flaw.EvictBusy, Flaw.DeleteLeavesJournal, Flaw.ProjectorStopsAtGap,
        // round 4 (FINDINGS 25)
        Flaw.KeepExpiredCallers, Flaw.ForgetShortWaits, Flaw.NoWriteFirst, Flaw.JobPoisonAsError,
        // K1 slice 7: the rules of a worker that owns a set, which the set worlds reach
        Flaw.FenceSubset, Flaw.SeenAtNodeEpoch, Flaw.SuspectAcrossPartitions, Flaw.ChildIssuerPerWorker, Flaw.TrialSparesOtherPartitions,
    ];
    var missed = 0;
    // A deploy in progress meets an older owner only now and then: this one runs 1,000 actions a seed.
    Flaw[] Longer = [Flaw.TurnNewerState];
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
                var (violation, done) = Run(seed, mutation, Longer.Contains(mutation) ? 1000 : null);
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

// K1b slice 6a: the node lease's mutations (NL-2, NL-3, NL-5, NL-8, NL-10), each caught by a node-lease witness of --lease-check.
async Task<int> NodeLeaseMutations()
{
    Flaw[] mutations = [Flaw.TakeIgnoresNodeLease, Flaw.HeartbeatIgnoresAnswer, Flaw.PlainReadEmptyFence, Flaw.KeepUnheldRows, Flaw.ReleaseDuringTake,
        Flaw.RenewRegardlessOfPasses, Flaw.TakeRenewsWedgedHost];
    var missed = 0;
    foreach (var mutation in mutations)
    {
        List<string> failed = [];
        NodeLeaseChecks.Flaws = mutation;
        try { await NodeLeaseChecks.Run((ok, name) => { if (!ok) failed.Add(name); }); }
        catch (Exception e) { failed.Clear(); Console.WriteLine($"  {mutation}: the witnesses threw {e.GetType().Name}: no catch"); }
        finally { NodeLeaseChecks.Flaws = Flaw.None; }
        if (failed.Count == 0) missed++;
        Console.WriteLine(failed.Count > 0 ? $"{mutation}: CAUGHT by {failed.Count} node-lease witness(es): {failed[0]}" : $"{mutation}: NOT CAUGHT by the node-lease witnesses");
    }
    return missed;
}

// The virtual partitions a world places: four, so a node of two or three holds several in its set.
partial class Program { const int Placed = 4; }

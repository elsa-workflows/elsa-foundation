using Kernel.VpExperiment;

// Round 5 A: virtual partitions with range ownership, the two fence granularities measured side by side.
//
//   dotnet run -c Release -- measure sqlite [count=1024,4096] [nodes=4] [fence=batch|checked|both] [variant=vp|range|both]
//   dotnet run -c Release -- measure postgres "<connection>" [the same] [proxy=1]
//
// Let Docker pick the PostgreSQL port (README, Databases). Every row prints what it cost; the checks print ok or FAIL.

//   dotnet run -c Release -- sim [seeds=2000] [actions=300] [nodes=4] [count=1024,4096]     the ownership protocol of both variants under a seed
//   dotnet run -c Release -- mutations [seeds=300] [actions=300] [nodes=4]                 each rule switched off: which seed catches it

//   dotnet run -c Release -- replay seed=23 count=1024 variant=vp|range [mutation=FenceWithoutPosition,...]   one seed, traced

if (args.Length > 0 && args[0] is "sim" or "mutations" or "replay")
{
    var simOptions = args.Skip(1).Select(a => a.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "1");
    var (seeds, simActions, simNodes) = (int.Parse(simOptions.GetValueOrDefault("seeds", args[0] == "sim" ? "2000" : "300")), int.Parse(simOptions.GetValueOrDefault("actions", "300")), int.Parse(simOptions.GetValueOrDefault("nodes", "4")));
    if (args[0] == "mutations") return OwnershipSim.Mutations(seeds, simActions, simNodes) == 0 ? 0 : 1;
    if (args[0] == "replay")
        return OwnershipSim.Replay(int.Parse(simOptions["seed"]), simActions, int.Parse(simOptions.GetValueOrDefault("count", "1024")), simNodes,
            simOptions.GetValueOrDefault("variant", "vp") == "vp" ? Fence.PerVirtualPartition : Fence.PerRange,
            simOptions.GetValueOrDefault("mutation", "None").Split(',').Aggregate(Mutation.None, (all, m) => all | Enum.Parse<Mutation>(m, true)));
    var failed = 0;
    foreach (var count in simOptions.GetValueOrDefault("count", $"{VirtualPartitions.Small},{VirtualPartitions.Large}").Split(',').Select(int.Parse))
        foreach (var variant in new[] { Fence.PerVirtualPartition, Fence.PerRange })
            if (OwnershipSim.Sweep(seeds, simActions, count, simNodes, variant) != 0) failed++;
    return failed == 0 ? 0 : 1;
}
if (args.Length == 0 || args[0] != "measure" || args.Length < 2)
{
    Console.Error.WriteLine("usage: measure sqlite|postgres [connection] [count=1024,4096] [nodes=4] [fence=batch|checked|both] [variant=vp|range|both] [proxy=1] | sim [...] | mutations [...]");
    return 2;
}
var engine = args[1];
var connection = engine == "postgres" ? args[2] : null;
var options = args.Skip(engine == "postgres" ? 3 : 2).Select(a => a.Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "1");
var counts = options.GetValueOrDefault("count", $"{VirtualPartitions.Small},{VirtualPartitions.Large}").Split(',').Select(int.Parse).ToList();
var nodes = int.Parse(options.GetValueOrDefault("nodes", "4"));
var fences = options.GetValueOrDefault("fence", "both") switch { "batch" => [true], "checked" => [false], _ => new[] { true, false } };
var variants = options.GetValueOrDefault("variant", "both") switch { "vp" => [Fence.PerVirtualPartition], "range" => [Fence.PerRange], _ => new[] { Fence.PerVirtualPartition, Fence.PerRange } };
var proxy = options.ContainsKey("proxy");

var ok = true;
foreach (var count in counts)
    foreach (var inBatch in fences)
        foreach (var variant in variants)
            ok &= await Measure.RunAsync(new Measure.Options(engine, connection, count, nodes, variant, inBatch, proxy));
Console.WriteLine(ok ? "\nevery check passed" : "\nsome check FAILED");
return ok ? 0 : 1;

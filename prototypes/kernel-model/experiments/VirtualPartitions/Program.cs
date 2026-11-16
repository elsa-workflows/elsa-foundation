using Kernel.VpExperiment;

// Round 5 A: virtual partitions with range ownership, the two fence granularities measured side by side.
//
//   dotnet run -c Release -- measure sqlite [count=1024,4096] [nodes=4] [fence=batch|checked|both] [variant=vp|range|both]
//   dotnet run -c Release -- measure postgres "<connection>" [the same] [proxy=1]
//
// Let Docker pick the PostgreSQL port (README, Databases). Every row prints what it cost; the checks print ok or FAIL.

if (args.Length == 0 || args[0] != "measure" || args.Length < 2)
{
    Console.Error.WriteLine("usage: measure sqlite|postgres [connection] [count=1024,4096] [nodes=4] [fence=batch|checked|both] [variant=vp|range|both] [proxy=1]");
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

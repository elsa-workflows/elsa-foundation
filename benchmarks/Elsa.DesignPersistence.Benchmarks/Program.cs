using Elsa.DesignPersistence.Benchmarks;
using Elsa.DesignPersistence.Benchmarks.Harness;
using Elsa.DesignPersistence.Benchmarks.Workload;

// Temporary same-provider design-persistence performance harness for Spec 093 US4 (T067/T068).
// See PrintHelp for usage. T069 (running the full matrix and writing the report) is the coordinator's.

var arguments = new ArgList(args);
var command = arguments.Positional(0) ?? "help";

try
{
    return command switch
    {
        "run" => await RunOneAsync(arguments),
        "matrix" => await RunMatrixAsync(arguments),
        "compare" => RunCompare(arguments),
        "gate" => RunGate(arguments),
        "help" or "--help" or "-h" => PrintHelp(),
        _ => Fail($"Unknown command '{command}'.")
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(Environment.GetEnvironmentVariable("ELSA_BENCH_DEBUG") is not null
        ? $"error: {ex}"
        : $"error: {ex.Message}");
    return 1;
}

static async Task<int> RunOneAsync(ArgList args)
{
    var scale = WorkloadScale.Parse(args.Positional(1) ?? throw new ArgumentException("run requires <scale>."));
    var adapter = args.Positional(2) ?? throw new ArgumentException("run requires <adapter> (ef|groundwork).");
    var form = args.Positional(3) ?? throw new ArgumentException("run requires <form> (store|shared|dedicated|entity).");
    var outDir = args.Option("--out");
    var seed = int.Parse(args.Option("--seed") ?? "24301");
    var run = int.Parse(args.Option("--run") ?? "1");
    var warmup = args.Flag("--warmup");
    var settings = args.Flag("--smoke") ? ProtocolSettings.Smoke : ProtocolSettings.Acceptance;

    var workload = new DesignWorkload(scale, seed);
    await using var target = TargetFactory.Create(adapter, form, workload);

    Console.WriteLine($"[run] {target.Key} @ {scale.Name} (seed {seed}, {(warmup ? "warmup" : $"run {run}")}, {(settings.IsSmoke ? "smoke" : "acceptance")})");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var result = await MeasuredRun.RunAsync(target, scale, seed, run, warmup, settings, outDir, CancellationToken.None);
    sw.Stop();

    Console.WriteLine($"[run] correctness read hashes: {(result.CorrectnessPassed ? "computed" : "ERROR")} ({result.CorrectnessHashes.Count} ops)");
    foreach (var op in result.Operations)
        Console.WriteLine($"  {op.Operation,-32} n={op.Count,-6} p50={op.P50Ms,7:F3}ms p95={op.P95Ms,7:F3}ms p99={op.P99Ms,7:F3}ms tput={op.ThroughputPerSecond,9:F1}/s");
    if (result.QueryPlans.Count > 0)
    {
        Console.WriteLine("[run] query plans:");
        foreach (var plan in result.QueryPlans)
            Console.WriteLine($"  {plan.Route,-28} indexed={plan.UsesIndexedAccess}: {plan.Detail}");
    }

    Console.WriteLine($"[run] wall {sw.Elapsed.TotalSeconds:F1}s -> {(outDir is null ? "(no file)" : outDir)}");
    return result.CorrectnessPassed ? 0 : 1;
}

static async Task<int> RunMatrixAsync(ArgList args)
{
    var scale = WorkloadScale.Parse(args.Positional(1) ?? throw new ArgumentException("matrix requires <scale>."));
    var outDir = args.Option("--out") ?? Path.Combine(Directory.GetCurrentDirectory(), "bench-out");
    var seed = int.Parse(args.Option("--seed") ?? "24301");
    var inProcess = args.Flag("--in-process");
    var settings = args.Flag("--smoke") ? ProtocolSettings.Smoke : ProtocolSettings.Acceptance;

    var output = await Orchestrator.RunAsync(scale, outDir, settings, inProcess, seed, CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine($"=== matrix {scale.Name} ({(settings.IsSmoke ? "smoke" : "acceptance")}) ===");
    PrintComparison(output);
    Console.WriteLine($"comparison -> {Path.Combine(outDir, $"comparison.{scale.Name}.json")}");
    return output.CorrectnessPassed ? 0 : 1;
}

static int RunCompare(ArgList args)
{
    var scale = WorkloadScale.Parse(args.Positional(1) ?? throw new ArgumentException("compare requires <scale>."));
    var outDir = args.Option("--out") ?? Path.Combine(Directory.GetCurrentDirectory(), "bench-out");
    var output = Orchestrator.Compare(scale, outDir, smoke: args.Flag("--smoke"), inProcess: false);

    Console.WriteLine($"=== compare {scale.Name} (from run files in {outDir}) ===");
    PrintComparison(output);
    Console.WriteLine($"comparison -> {Path.Combine(outDir, $"comparison.{scale.Name}.json")}");
    return output.CorrectnessPassed ? 0 : 1;
}

// Prints correctness, the ratified budget gate (gate 5), the recorded EF-ratio evidence table (no
// longer a gate), and the form-selection gate (gate 6) for one scale's comparison output.
static void PrintComparison(Orchestrator.MatrixOutput output)
{
    Console.WriteLine($"correctness: {(output.CorrectnessPassed ? "PASS" : "FAIL")}");
    foreach (var row in output.Correctness.Where(r => !r.AllMatch))
        Console.WriteLine($"  MISMATCH {row.Operation}");

    if (output.Budget is { Count: > 0 })
    {
        Console.WriteLine($"budget gate (gate 5, ratified 2026-07-22 — absolute 100K budgets): {(output.BudgetGatePassed ? "PASS" : "FAIL")}");
        foreach (var row in output.Budget)
            Console.WriteLine($"  {row.Operation,-32} [{row.Class,-18}] p95={row.P95Ms,8:F3}/{row.P95BudgetMs,-7:F1} p99={row.P99Ms,9:F3}/{row.P99BudgetMs,-6:F1} tput={row.ThroughputPerSecond,9:F1}>={row.ThroughputFloor,8:F1} -> {(row.Pass ? "pass" : "FAIL")}");
    }

    if (output.EfRatio.Count > 0)
    {
        Console.WriteLine($"EF-ratio (recorded evidence, NOT a gate — semantically unequal work): all-rows-pass={output.EfRatioPassed}");
        foreach (var row in output.EfRatio)
            Console.WriteLine($"  {row.Operation,-32} p95x={row.P95Ratio,6:F2} tput%={row.ThroughputRatio * 100,5:F0} p99x={row.P99Ratio,6:F2} -> {(row.Pass ? "pass" : "fail")}");
    }

    if (output.Form.Count > 0)
    {
        Console.WriteLine($"form-selection gate (gate 6, this scale, * = gated): {(output.FormPassedThisScale ? "PASS" : "FAIL")}");
        foreach (var row in output.Form)
            Console.WriteLine($"  {(row.Discriminating ? "*" : " ")}{row.Operation,-27} vs {row.Alternative,-20} p95+={row.MedianP95ImprovementPct,7:F1}% tput+={row.MedianThroughputImprovementPct,7:F1}% ci=[{row.CiLow,6:F3},{row.CiHigh,6:F3}] -> {(row.Pass ? "pass" : "FAIL")}");
    }
}

static int RunGate(ArgList args)
{
    var outDir = args.Option("--out") ?? Path.Combine(Directory.GetCurrentDirectory(), "bench-out");
    var hundredKPath = Path.Combine(outDir, "comparison.100k.json");
    var oneMPath = Path.Combine(outDir, "comparison.1m.json");
    if (!File.Exists(hundredKPath) || !File.Exists(oneMPath))
        return Fail($"gate needs both {hundredKPath} and {oneMPath}. Run 'matrix 100k' and 'matrix 1m' first.");

    var hundredK = Json.Read<Orchestrator.MatrixOutput>(hundredKPath);
    var oneM = Json.Read<Orchestrator.MatrixOutput>(oneMPath);

    // Gate 5 (ratified amendment 2026-07-22, program-owner decision, T079 review validating): absolute
    // operational budgets on the 100K Benchmark Acceptance Catalog rows replace the per-row EF ratio,
    // which compared semantically unequal work (Groundwork ledger/replay/scope/atomic-staging per op vs
    // bare EF SaveChanges). The EF ratio remains recorded as evidence below but is no longer a gate.
    var gate5 = hundredK.BudgetGatePassed;
    var budgetRows = hundredK.Budget ?? [];

    // Form gate (gate 6, unchanged): entity must beat each alternative at BOTH 100K and 1M.
    var oneMByKey = oneM.Form.ToDictionary(r => (r.Operation, r.Alternative));
    var formRows = new List<(string Operation, string Alternative, bool Discriminating, bool Pass)>();
    foreach (var row in hundredK.Form)
    {
        var passBoth = row.Pass && oneMByKey.TryGetValue((row.Operation, row.Alternative), out var m) && m.Pass;
        formRows.Add((row.Operation, row.Alternative, row.Discriminating, passBoth));
    }

    var discriminating = formRows.Where(r => r.Discriminating).ToList();
    var gate6 = discriminating.Count > 0 && discriminating.All(r => r.Pass);

    var verdict = new
    {
        BudgetGatePassed = gate5,
        FormSelectionGatePassed = gate6,
        BudgetRows = budgetRows.Select(r => new
        {
            r.Operation, r.Class,
            r.P95Ms, r.P95BudgetMs, r.P95Pass,
            r.P99Ms, r.P99BudgetMs, r.P99Pass,
            r.ThroughputPerSecond, r.ThroughputFloor, r.ThroughputPass,
            r.Pass
        }),
        EfRatioEvidence = new
        {
            Note = "Recorded evidence only, NOT a gate (ratified amendment 2026-07-22): EF oracle runs bare SaveChanges while Groundwork runs ledger/replay/scope/atomic-staging per op; its LegacyEfOracle profile declares those scenarios N/A.",
            AllRowsWouldPass = hundredK.EfRatioPassed
        },
        FormRows = formRows.Select(r => new { r.Operation, r.Alternative, r.Discriminating, PassesAt100KAnd1M = r.Pass }),
        Note = "Gate 5 = absolute 100K budget rows; gate 6 = form selection at 100K & 1M. Correctness must be green in every comparison before timing is trusted."
    };
    Json.Write(Path.Combine(outDir, "gates.json"), verdict);

    Console.WriteLine($"budget gate (gate 5, 100K absolute budgets): {(gate5 ? "PASS" : "FAIL")}  ({budgetRows.Count(r => r.Pass)}/{budgetRows.Count} rows)");
    foreach (var row in budgetRows.Where(r => !r.Pass))
        Console.WriteLine($"  FAIL {row.Operation} [{row.Class}] p95={row.P95Ms:F3}/{row.P95BudgetMs:F1} p99={row.P99Ms:F3}/{row.P99BudgetMs:F1} tput={row.ThroughputPerSecond:F1}>={row.ThroughputFloor:F1}");
    Console.WriteLine($"EF-ratio (recorded evidence, NOT a gate): all-rows-pass={hundredK.EfRatioPassed}");
    Console.WriteLine($"form-selection gate (gate 6, 100K & 1M): {(gate6 ? "PASS" : "FAIL")}");
    foreach (var row in discriminating.Where(r => !r.Pass))
        Console.WriteLine($"  FAIL {row.Operation} vs {row.Alternative}");
    Console.WriteLine($"gates -> {Path.Combine(outDir, "gates.json")}");
    return gate5 && gate6 ? 0 : 2;
}

static int PrintHelp()
{
    Console.WriteLine(
        """
        Elsa design-persistence benchmark (Spec 093 US4, T067/T068).

        Commands:
          run <scale> <adapter> <form> [--out DIR] [--run N] [--warmup] [--smoke] [--seed S]
              Run one process's workload and (with --out) write raw-sample evidence.
              scale:   1k | 100k | 1m | smoke
              adapter: ef | groundwork
              form:    store          (real composed stores; ef requires this)
                       shared|dedicated|entity  (modeled Groundwork physical forms; groundwork only)

          matrix <scale> [--out DIR] [--smoke] [--in-process] [--seed S]
              Run the warm-up + three-run matrix for every target valid at the scale, then write
              comparison.<scale>.json with correctness, EF-ratio, and form-selection rows.
              Measured runs are independent child processes unless --in-process is given.

          compare <scale> [--out DIR] [--smoke]
              Re-aggregate comparison.<scale>.json from run files already on disk (no re-measuring).

          gate [--out DIR]
              Merge comparison.100k.json and comparison.1m.json into gates.json (final EF-ratio +
              form-selection verdicts). Run 'matrix 100k' and 'matrix 1m' first.

        Coordinator (T069) recipe:
          dotnet run -c Release --project benchmarks/Elsa.DesignPersistence.Benchmarks -- matrix 1k   --out bench-out
          dotnet run -c Release --project benchmarks/Elsa.DesignPersistence.Benchmarks -- matrix 100k --out bench-out
          dotnet run -c Release --project benchmarks/Elsa.DesignPersistence.Benchmarks -- matrix 1m   --out bench-out
          dotnet run -c Release --project benchmarks/Elsa.DesignPersistence.Benchmarks -- gate        --out bench-out
        Requires 'dotnet tool restore' (Groundwork schema tool) before running groundwork targets.
        """);
    return 0;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"error: {message}");
    return 1;
}

internal sealed class ArgList(string[] args)
{
    public string? Positional(int index)
    {
        var positionals = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                if (!IsFlag(args[i]) && i + 1 < args.Length)
                    i++;
                continue;
            }

            positionals.Add(args[i]);
        }

        return index < positionals.Count ? positionals[index] : null;
    }

    public string? Option(string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name)
                return args[i + 1];
        return null;
    }

    public bool Flag(string name) => args.Contains(name);

    private static bool IsFlag(string arg) => arg is "--warmup" or "--smoke" or "--in-process";
}

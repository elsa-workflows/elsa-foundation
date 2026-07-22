using System.Diagnostics;
using Elsa.DesignPersistence.Benchmarks.Harness;
using Elsa.DesignPersistence.Benchmarks.Workload;

namespace Elsa.DesignPersistence.Benchmarks;

/// <summary>Runs the full warm-up + three-run matrix for a scale and emits a comparison JSON with the
/// EF-ratio and physical-form gate rows for that scale. Measured runs default to independent child
/// processes (true process independence); <c>--in-process</c> runs them sequentially for quick local
/// verification.</summary>
public static class Orchestrator
{
    public sealed record CorrectnessRow(string Operation, bool AllMatch, IReadOnlyDictionary<string, string> HashesByTarget);

    public sealed record MatrixOutput(
        string Scale,
        bool Smoke,
        bool InProcess,
        IReadOnlyList<string> Targets,
        IReadOnlyList<CorrectnessRow> Correctness,
        bool CorrectnessPassed,
        IReadOnlyList<Gates.EfRatioRow> EfRatio,
        bool EfRatioPassed,
        IReadOnlyList<Gates.BudgetRow> Budget,
        bool BudgetGatePassed,
        IReadOnlyList<Gates.FormRow> Form,
        bool FormPassedThisScale);

    public static async Task<MatrixOutput> RunAsync(
        WorkloadScale scale,
        string outDir,
        ProtocolSettings settings,
        bool inProcess,
        int seed,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outDir);
        var targets = TargetFactory.MatrixTargets(scale);

        foreach (var (adapter, form) in targets)
        {
            Console.WriteLine($"[matrix] {adapter}/{form} @ {scale.Name}: warm-up");
            await RunOneAsync(scale, adapter, form, outDir, settings, inProcess, seed, run: 0, warmup: true, cancellationToken);
            for (var run = 1; run <= 3; run++)
            {
                Console.WriteLine($"[matrix] {adapter}/{form} @ {scale.Name}: run {run}");
                await RunOneAsync(scale, adapter, form, outDir, settings, inProcess, seed, run, warmup: false, cancellationToken);
            }
        }

        return Compare(scale, outDir, settings.IsSmoke, inProcess);
    }

    /// <summary>Re-aggregates the gate rows from run files already on disk, without re-measuring.</summary>
    public static MatrixOutput Compare(WorkloadScale scale, string outDir, bool smoke, bool inProcess)
    {
        var targets = TargetFactory.MatrixTargets(scale);
        var aggregates = targets.ToDictionary(
            t => TargetFactory.TargetKey(t.Adapter, t.Form),
            t => Gates.Aggregate(TargetFactory.TargetKey(t.Adapter, t.Form), scale, outDir),
            StringComparer.Ordinal);

        var correctness = EvaluateCorrectness(scale, targets, outDir);
        var correctnessPassed = correctness.All(c => c.AllMatch);

        var efRatio = aggregates.TryGetValue(Gates.EfKey, out var ef) && aggregates.TryGetValue(Gates.GroundworkStoreKey, out var gw)
            ? Gates.EvaluateEfRatio(ef, gw)
            : [];
        var efRatioPassed = efRatio.Count > 0 && efRatio.All(r => r.Pass);

        // Gate 5 (ratified amendment 2026-07-22): absolute operational budgets on the groundwork.store
        // rows, evaluated at the 100K acceptance scale only. EF ratios above stay recorded as evidence.
        var budget = scale.Name == "100k" && aggregates.TryGetValue(Gates.GroundworkStoreKey, out var gwStore)
            ? Gates.EvaluateBudget(gwStore)
            : [];
        var budgetPassed = budget.Count > 0 && budget.All(r => r.Pass);

        var formRows = aggregates.TryGetValue(Gates.EntityKey, out var entity)
            ? Gates.EvaluateForm(entity,
                aggregates.TryGetValue(Gates.SharedKey, out var shared) ? shared : new Gates.TargetAggregate(Gates.SharedKey, new Dictionary<string, Gates.OperationAggregate>()),
                aggregates.TryGetValue(Gates.DedicatedKey, out var dedicated) ? dedicated : new Gates.TargetAggregate(Gates.DedicatedKey, new Dictionary<string, Gates.OperationAggregate>()))
            : [];
        var discriminating = formRows.Where(r => r.Discriminating).ToList();
        var formPassed = discriminating.Count > 0 && discriminating.All(r => r.Pass);

        var output = new MatrixOutput(
            scale.Name, smoke, inProcess,
            targets.Select(t => TargetFactory.TargetKey(t.Adapter, t.Form)).ToList(),
            correctness, correctnessPassed,
            efRatio, efRatioPassed,
            budget, budgetPassed,
            formRows, formPassed);

        Json.Write(Path.Combine(outDir, $"comparison.{scale.Name}.json"), output);
        return output;
    }

    private static async Task RunOneAsync(
        WorkloadScale scale, string adapter, string form, string outDir, ProtocolSettings settings,
        bool inProcess, int seed, int run, bool warmup, CancellationToken cancellationToken)
    {
        if (inProcess)
        {
            var workload = new DesignWorkload(scale, seed);
            await using var target = TargetFactory.Create(adapter, form, workload);
            await MeasuredRun.RunAsync(target, scale, seed, run, warmup, settings, outDir, cancellationToken);
            return;
        }

        await SpawnChildAsync(scale, adapter, form, outDir, settings.IsSmoke, seed, run, warmup, cancellationToken);
    }

    private static async Task SpawnChildAsync(
        WorkloadScale scale, string adapter, string form, string outDir, bool smoke, int seed, int run, bool warmup, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        start.ArgumentList.Add(typeof(Orchestrator).Assembly.Location);
        start.ArgumentList.Add("run");
        start.ArgumentList.Add(scale.Name);
        start.ArgumentList.Add(adapter);
        start.ArgumentList.Add(form);
        start.ArgumentList.Add("--out");
        start.ArgumentList.Add(outDir);
        start.ArgumentList.Add("--run");
        start.ArgumentList.Add(run.ToString());
        start.ArgumentList.Add("--seed");
        start.ArgumentList.Add(seed.ToString());
        if (warmup) start.ArgumentList.Add("--warmup");
        if (smoke) start.ArgumentList.Add("--smoke");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start a benchmark child process.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Benchmark child process for {adapter}/{form} run {run} failed with exit code {process.ExitCode}.");
    }

    private static IReadOnlyList<CorrectnessRow> EvaluateCorrectness(
        WorkloadScale scale, IReadOnlyList<(string Adapter, string Form)> targets, string outDir)
    {
        var byTarget = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (adapter, form) in targets)
        {
            var key = TargetFactory.TargetKey(adapter, form);
            var path = Path.Combine(outDir, $"{key}.{scale.Name}.run1.json");
            if (File.Exists(path))
                byTarget[key] = Json.Read<ProcessRunResult>(path).CorrectnessHashes;
        }

        var operations = byTarget.Values.SelectMany(h => h.Keys).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal);
        var rows = new List<CorrectnessRow>();
        foreach (var operation in operations)
        {
            var hashes = byTarget
                .Where(t => t.Value.ContainsKey(operation))
                .ToDictionary(t => t.Key, t => t.Value[operation], StringComparer.Ordinal);
            var allMatch = hashes.Values.Distinct(StringComparer.Ordinal).Count() == 1;
            rows.Add(new CorrectnessRow(operation, allMatch, hashes));
        }

        return rows;
    }
}

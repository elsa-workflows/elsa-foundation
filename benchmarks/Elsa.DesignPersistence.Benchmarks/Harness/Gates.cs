using Elsa.DesignPersistence.Benchmarks.Workload;

namespace Elsa.DesignPersistence.Benchmarks.Harness;

/// <summary>Aggregates the three measured runs per target and encodes the accepted performance gates.
///
/// Gate 5 (ratified amendment 2026-07-22, program-owner interactive decision, T079 review validating):
/// the per-row same-provider EF ratio was REPLACED by absolute operational budgets on the Benchmark
/// Acceptance Catalog rows, because the ratio compared semantically unequal work — the Groundwork write
/// path runs the ratified operation-ledger marker, replay preflight, scope-bound sessions, and atomic
/// multi-document staging per operation, while the temporary EF oracle performs bare SaveChanges (its
/// LegacyEfOracle conformance profile declares the ledger/replay/scope scenarios N/A). The EF ratio
/// table is still computed and printed, but only as RECORDED EVIDENCE — it is no longer a gate.
///
/// Gate 6 (unchanged) is the physical-form selection gate (entity improves median p95 or throughput by
/// &gt;= 10% over both other forms, same direction in all three runs, 95% bootstrap CI excluding zero).</summary>
public static class Gates
{
    public const string EfKey = "ef.normalized";
    public const string GroundworkStoreKey = "groundwork.store";
    public const string EntityKey = "groundwork.entity";
    public const string SharedKey = "groundwork.shared";
    public const string DedicatedKey = "groundwork.dedicated";

    public sealed record OperationAggregate(
        string Operation,
        bool ScaleBearing,
        IReadOnlyList<double> PerRunP95,
        IReadOnlyList<double> PerRunP99,
        IReadOnlyList<double> PerRunThroughput,
        double MedianP95,
        double MedianP99,
        double MedianThroughput,
        IReadOnlyList<double> PooledLatencies);

    public sealed record TargetAggregate(string Target, IReadOnlyDictionary<string, OperationAggregate> Operations);

    public sealed record EfRatioRow(
        string Operation, double EfP95, double GwP95, double P95Ratio, bool P95Pass,
        double EfThroughput, double GwThroughput, double ThroughputRatio, bool ThroughputPass,
        double EfP99, double GwP99, double P99Ratio, bool P99Pass, bool Pass);

    public sealed record FormRow(
        string Operation, string Alternative, bool Discriminating, double MedianP95ImprovementPct, double MedianThroughputImprovementPct,
        bool DirectionConsistentAllRuns, double CiLow, double CiHigh, bool CiExcludesZero, bool Pass);

    /// <summary>Absolute operational budget for one Benchmark Acceptance Catalog class (ratified gate-5
    /// amendment 2026-07-22). <paramref name="MinThroughput"/> is <see cref="double.NaN"/> for the
    /// write@c16 class, whose floor is resolved per row to the same row's @c1 throughput (write scaling
    /// must not invert).</summary>
    public sealed record BudgetThreshold(double P95Ms, double P99Ms, double MinThroughput);

    public sealed record BudgetRow(
        string Operation, string Class,
        double P95Ms, double P95BudgetMs, bool P95Pass,
        double P99Ms, double P99BudgetMs, bool P99Pass,
        double ThroughputPerSecond, double ThroughputFloor, bool ThroughputPass,
        bool Pass);

    /// <summary>
    /// The scale-bearing routes whose cost depends on the physical form. Pure primary-key identity
    /// lookups (<c>*.identity.get</c>, <c>wf.identity.version-exact</c>) resolve through the same
    /// (tenant,id) key in every form and therefore tie by construction; the form-selection gate
    /// (data-model Decision 3) is about the projected-column query routes, so only these are gated.
    /// The others are still measured and reported for transparency.
    /// </summary>
    public static readonly IReadOnlySet<string> DiscriminatingOps = new HashSet<string>(StringComparer.Ordinal)
    {
        "wf.identity.version-latest",
        "wf.version.exists",
        "wf.catalog.filter-page",
        "wf.catalog.count",
        "wf.catalog.projection",
        "act.identity.version-exact",
        "act.catalog.filter-page",
        "act.catalog.versions-batch"
    };

    public sealed record ScaleComparison(
        string Scale, bool Smoke,
        IReadOnlyList<TargetAggregate> Targets,
        IReadOnlyList<EfRatioRow> EfRatio,
        IReadOnlyList<FormRow> Form);

    public static TargetAggregate Aggregate(string target, WorkloadScale scale, string outDir)
    {
        var runs = new List<ProcessRunResult>();
        for (var run = 1; run <= 3; run++)
        {
            var path = Path.Combine(outDir, $"{target}.{scale.Name}.run{run}.json");
            if (File.Exists(path))
                runs.Add(Json.Read<ProcessRunResult>(path));
        }

        if (runs.Count == 0)
            return new TargetAggregate(target, new Dictionary<string, OperationAggregate>());

        var operationNames = runs[0].Operations.Select(o => o.Operation).ToList();
        var operations = new Dictionary<string, OperationAggregate>(StringComparer.Ordinal);
        foreach (var name in operationNames)
        {
            var samples = runs
                .Select(r => r.Operations.FirstOrDefault(o => o.Operation == name))
                .Where(o => o is not null)
                .Cast<OperationSamples>()
                .ToList();
            if (samples.Count == 0)
                continue;

            var pooled = samples.SelectMany(s => s.RawLatenciesMs).ToList();
            operations[name] = new OperationAggregate(
                name,
                samples[0].ScaleBearing,
                samples.Select(s => s.P95Ms).ToList(),
                samples.Select(s => s.P99Ms).ToList(),
                samples.Select(s => s.ThroughputPerSecond).ToList(),
                Statistics.Median(samples.Select(s => s.P95Ms)),
                Statistics.Median(samples.Select(s => s.P99Ms)),
                Statistics.Median(samples.Select(s => s.ThroughputPerSecond)),
                pooled);
        }

        return new TargetAggregate(target, operations);
    }

    public static IReadOnlyList<EfRatioRow> EvaluateEfRatio(TargetAggregate ef, TargetAggregate groundwork)
    {
        var rows = new List<EfRatioRow>();
        foreach (var (operation, gw) in groundwork.Operations)
        {
            if (!ef.Operations.TryGetValue(operation, out var efOp))
                continue;
            var p95Ratio = Ratio(gw.MedianP95, efOp.MedianP95);
            var tputRatio = Ratio(gw.MedianThroughput, efOp.MedianThroughput);
            var p99Ratio = Ratio(gw.MedianP99, efOp.MedianP99);
            var p95Pass = p95Ratio <= 1.25;
            var tputPass = tputRatio >= 0.80;
            var p99Pass = p99Ratio <= 2.0;
            rows.Add(new EfRatioRow(
                operation, efOp.MedianP95, gw.MedianP95, p95Ratio, p95Pass,
                efOp.MedianThroughput, gw.MedianThroughput, tputRatio, tputPass,
                efOp.MedianP99, gw.MedianP99, p99Ratio, p99Pass,
                p95Pass && tputPass && p99Pass));
        }

        return rows.OrderBy(r => r.Operation, StringComparer.Ordinal).ToList();
    }

    // --- Ratified gate-5 budget table (100K acceptance scale) ---
    // Absolute operational budgets that bound the product-relevant authoring envelope (interactive-save
    // perception thresholds, point-lookup latencies, catalog page responsiveness). See the design
    // persistence contract "Performance and Removal Gate" item 5 for the decision record.
    private static readonly IReadOnlySet<string> PointReadOps = new HashSet<string>(StringComparer.Ordinal)
    {
        "wf.identity.get", "wf.identity.version-exact", "wf.identity.version-latest",
        "wf.version.exists", "act.identity.get", "act.identity.version-exact"
    };
    private static readonly IReadOnlySet<string> BatchProjectionOps = new HashSet<string>(StringComparer.Ordinal)
    {
        "act.catalog.versions-batch", "wf.catalog.projection"
    };
    private static readonly IReadOnlySet<string> CatalogPageOps = new HashSet<string>(StringComparer.Ordinal)
    {
        "wf.catalog.filter-page", "act.catalog.filter-page", "wf.catalog.count"
    };

    private static readonly BudgetThreshold PointReadBudget = new(P95Ms: 0.8, P99Ms: 2.5, MinThroughput: 2000);
    private static readonly BudgetThreshold BatchProjectionBudget = new(P95Ms: 5, P99Ms: 20, MinThroughput: 200);
    private static readonly BudgetThreshold CatalogPageBudget = new(P95Ms: 400, P99Ms: 800, MinThroughput: 4);
    private static readonly BudgetThreshold WriteC1Budget = new(P95Ms: 3, P99Ms: 25, MinThroughput: 400);
    private static readonly BudgetThreshold WriteC16Budget = new(P95Ms: 100, P99Ms: 500, MinThroughput: double.NaN);

    /// <summary>Evaluates the ratified absolute-budget gate (gate 5) against the composed
    /// <c>groundwork.store</c> rows only. EF measurements stay recorded as evidence and are not consulted
    /// here. The write@c16 throughput floor is the same row's measured @c1 throughput.</summary>
    public static IReadOnlyList<BudgetRow> EvaluateBudget(TargetAggregate groundworkStore)
    {
        var rows = new List<BudgetRow>();
        foreach (var (operation, op) in groundworkStore.Operations)
        {
            string className;
            BudgetThreshold budget;
            double throughputFloor;

            if (PointReadOps.Contains(operation))
            {
                className = "point-read";
                budget = PointReadBudget;
                throughputFloor = budget.MinThroughput;
            }
            else if (BatchProjectionOps.Contains(operation))
            {
                className = "batch/projection";
                budget = BatchProjectionBudget;
                throughputFloor = budget.MinThroughput;
            }
            else if (CatalogPageOps.Contains(operation))
            {
                className = "catalog page/count";
                budget = CatalogPageBudget;
                throughputFloor = budget.MinThroughput;
            }
            else if (operation.EndsWith("@c1", StringComparison.Ordinal))
            {
                className = "write@c1";
                budget = WriteC1Budget;
                throughputFloor = budget.MinThroughput;
            }
            else if (operation.EndsWith("@c16", StringComparison.Ordinal))
            {
                className = "write@c16";
                budget = WriteC16Budget;
                var sibling = string.Concat(operation.AsSpan(0, operation.Length - "@c16".Length), "@c1");
                throughputFloor = groundworkStore.Operations.TryGetValue(sibling, out var c1)
                    ? c1.MedianThroughput
                    : double.PositiveInfinity;
            }
            else
            {
                // Unknown route: fail loudly rather than silently passing an unbudgeted operation.
                className = "unclassified";
                budget = new BudgetThreshold(0, 0, double.PositiveInfinity);
                throughputFloor = double.PositiveInfinity;
            }

            var p95Pass = op.MedianP95 <= budget.P95Ms;
            var p99Pass = op.MedianP99 <= budget.P99Ms;
            var throughputPass = op.MedianThroughput >= throughputFloor;
            rows.Add(new BudgetRow(
                operation, className,
                op.MedianP95, budget.P95Ms, p95Pass,
                op.MedianP99, budget.P99Ms, p99Pass,
                op.MedianThroughput, throughputFloor, throughputPass,
                p95Pass && p99Pass && throughputPass));
        }

        return rows.OrderBy(r => r.Operation, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<FormRow> EvaluateForm(TargetAggregate entity, params TargetAggregate[] alternatives)
    {
        var rows = new List<FormRow>();
        foreach (var (operation, entityOp) in entity.Operations.Where(o => o.Value.ScaleBearing))
        {
            foreach (var alternative in alternatives)
            {
                if (!alternative.Operations.TryGetValue(operation, out var altOp))
                    continue;

                // Positive = entity is better.
                var p95Improvement = (altOp.MedianP95 - entityOp.MedianP95) / altOp.MedianP95;
                var tputImprovement = (entityOp.MedianThroughput - altOp.MedianThroughput) / altOp.MedianThroughput;

                var p95DirectionAllRuns = entityOp.PerRunP95.Zip(altOp.PerRunP95, (e, a) => e < a).All(x => x);
                var tputDirectionAllRuns = entityOp.PerRunThroughput.Zip(altOp.PerRunThroughput, (e, a) => e > a).All(x => x);

                var (ciLow, ciHigh) = Statistics.BootstrapRelativeImprovementCi(
                    entityOp.PooledLatencies, altOp.PooledLatencies, lowerIsBetter: true,
                    samples => Statistics.Percentile(samples, 95));
                var ciExcludesZero = ciLow > 0 || ciHigh < 0;

                var p95Path = p95Improvement >= 0.10 && p95DirectionAllRuns && ciExcludesZero;
                var tputPath = tputImprovement >= 0.10 && tputDirectionAllRuns && ciExcludesZero;

                rows.Add(new FormRow(
                    operation,
                    alternative.Target,
                    DiscriminatingOps.Contains(operation),
                    p95Improvement * 100.0,
                    tputImprovement * 100.0,
                    p95DirectionAllRuns || tputDirectionAllRuns,
                    ciLow, ciHigh, ciExcludesZero,
                    p95Path || tputPath));
            }
        }

        return rows.OrderBy(r => r.Operation, StringComparer.Ordinal).ThenBy(r => r.Alternative, StringComparer.Ordinal).ToList();
    }

    private static double Ratio(double value, double baseline) => baseline > 0 ? value / baseline : double.PositiveInfinity;
}

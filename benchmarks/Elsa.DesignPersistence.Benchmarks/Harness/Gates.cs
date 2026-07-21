using Elsa.DesignPersistence.Benchmarks.Workload;

namespace Elsa.DesignPersistence.Benchmarks.Harness;

/// <summary>Aggregates the three measured runs per target and encodes the accepted performance gates:
/// the same-provider EF-ratio gate (p95 &lt;= 1.25x, throughput &gt;= 80%, p99 &lt;= 2x) and the
/// physical-form selection gate (entity improves median p95 or throughput by &gt;= 10% over both other
/// forms, same direction in all three runs, 95% bootstrap CI excluding zero).</summary>
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

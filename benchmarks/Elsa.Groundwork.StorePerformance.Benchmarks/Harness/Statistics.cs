namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

public static class Statistics
{
    public static double Percentile(IReadOnlyList<double> samples, double percentile)
    {
        if (samples.Count == 0) return double.NaN;
        var ordered = samples.Order().ToArray();
        var rank = percentile / 100d * (ordered.Length - 1);
        var low = (int)Math.Floor(rank); var high = (int)Math.Ceiling(rank);
        return low == high ? ordered[low] : ordered[low] + (ordered[high] - ordered[low]) * (rank - low);
    }
    public static double Median(IEnumerable<double> values) => Percentile(values.ToArray(), 50);
    /// <summary>Capped percentile bootstrap (95% CI). Capping only bounds each draw; it never manufactures samples.</summary>
    public static (double Low, double High) BootstrapPercentileCi(IReadOnlyList<double> samples, double percentile, int resamples = 1000, int maximumDraw = 4000, int seed = 646)
    {
        if (samples.Count == 0 || resamples < 100 || maximumDraw < 1) return (double.NaN, double.NaN);
        var random = new Random(seed); var draw = new double[Math.Min(samples.Count, maximumDraw)]; var results = new double[resamples];
        for (var index = 0; index < results.Length; index++) { for (var sample = 0; sample < draw.Length; sample++) draw[sample] = samples[random.Next(samples.Count)]; results[index] = Percentile(draw, percentile); }
        return (Percentile(results, 2.5), Percentile(results, 97.5));
    }

    /// <summary>Deterministic, paired-independent hierarchical percentile bootstrap for target/oracle
    /// ratios. Every replicate resamples within each measured process, computes that process percentile,
    /// takes the median of the three process percentiles, and only then forms target/oracle. Raw samples
    /// are never flattened across independent processes.</summary>
    public static (double Low, double High) BootstrapPercentileRatioCi(IReadOnlyDictionary<int, IReadOnlyList<double>> oracleByProcess, IReadOnlyDictionary<int, IReadOnlyList<double>> targetByProcess, double percentile, int resamples = 1000, int maximumDraw = 4000, int seed = 646)
    {
        var indices = new[] { 1, 2, 3 };
        if (resamples < 100 || maximumDraw < 1 || !oracleByProcess.Keys.Order().SequenceEqual(indices) || !targetByProcess.Keys.Order().SequenceEqual(indices) || indices.Any(index => oracleByProcess[index].Count == 0 || targetByProcess[index].Count == 0)) return (double.NaN, double.NaN);
        var random = new Random(seed);
        var ratios = new double[resamples];
        for (var index = 0; index < ratios.Length; index++)
        {
            var oraclePercentiles = new double[3];
            var targetPercentiles = new double[3];
            for (var process = 0; process < indices.Length; process++)
            {
                oraclePercentiles[process] = ResampledPercentile(oracleByProcess[indices[process]], percentile, maximumDraw, random);
                targetPercentiles[process] = ResampledPercentile(targetByProcess[indices[process]], percentile, maximumDraw, random);
            }
            var baseline = Median(oraclePercentiles);
            ratios[index] = baseline > 0 ? Median(targetPercentiles) / baseline : double.NaN;
        }
        var finite = ratios.Where(double.IsFinite).ToArray();
        return finite.Length == 0 ? (double.NaN, double.NaN) : (Percentile(finite, 2.5), Percentile(finite, 97.5));
    }

    private static double ResampledPercentile(IReadOnlyList<double> source, double percentile, int maximumDraw, Random random)
    {
        var draw = new double[Math.Min(source.Count, maximumDraw)];
        for (var index = 0; index < draw.Length; index++) draw[index] = source[random.Next(source.Count)];
        return Percentile(draw, percentile);
    }
}

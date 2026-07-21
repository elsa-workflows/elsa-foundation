namespace Elsa.DesignPersistence.Benchmarks.Harness;

/// <summary>Percentiles, throughput, and a deterministic percentile-bootstrap for the form-selection
/// confidence-interval gate. Latency samples are in milliseconds.</summary>
public static class Statistics
{
    /// <summary>Linear-interpolation percentile over a copy that this method sorts.</summary>
    public static double Percentile(IReadOnlyList<double> samples, double percentile)
    {
        if (samples.Count == 0)
            return double.NaN;
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        return PercentileSorted(sorted, percentile);
    }

    private static double PercentileSorted(double[] sorted, double percentile)
    {
        if (sorted.Length == 1)
            return sorted[0];
        var rank = percentile / 100.0 * (sorted.Length - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high)
            return sorted[low];
        var weight = rank - low;
        return sorted[low] * (1 - weight) + sorted[high] * weight;
    }

    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.ToArray();
        Array.Sort(sorted);
        if (sorted.Length == 0)
            return double.NaN;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>
    /// 95% bootstrap confidence interval for the relative improvement of the entity form's metric
    /// over an alternative form's metric, resampling per-operation samples. "Relative improvement"
    /// is defined so that a positive value always means the entity form is better: for a latency
    /// metric (lower is better) it is (alt - entity)/alt; for throughput (higher is better) it is
    /// (entity - alt)/alt. Returns the 2.5% and 97.5% percentiles of the resampled statistic.
    /// </summary>
    public static (double Low, double High) BootstrapRelativeImprovementCi(
        IReadOnlyList<double> entitySamples,
        IReadOnlyList<double> alternativeSamples,
        bool lowerIsBetter,
        Func<IReadOnlyList<double>, double> statistic,
        int resamples = 1000,
        int maxResampleSize = 4000,
        int seed = 0x0B007)
    {
        if (entitySamples.Count == 0 || alternativeSamples.Count == 0)
            return (double.NaN, double.NaN);

        // Each bootstrap draw is bounded so pooled sample sets of hundreds of thousands stay tractable;
        // a fixed draw size with replacement remains a valid percentile-bootstrap for the CI.
        var rng = new Random(seed);
        var entityDraw = Math.Min(entitySamples.Count, maxResampleSize);
        var altDraw = Math.Min(alternativeSamples.Count, maxResampleSize);
        var improvements = new double[resamples];
        var entityBuffer = new double[entityDraw];
        var altBuffer = new double[altDraw];
        for (var r = 0; r < resamples; r++)
        {
            var entityStat = statistic(Resample(entitySamples, rng, entityBuffer));
            var altStat = statistic(Resample(alternativeSamples, rng, altBuffer));
            improvements[r] = lowerIsBetter
                ? (altStat - entityStat) / altStat
                : (entityStat - altStat) / altStat;
        }

        Array.Sort(improvements);
        return (PercentileSorted(improvements, 2.5), PercentileSorted(improvements, 97.5));
    }

    private static double[] Resample(IReadOnlyList<double> source, Random rng, double[] buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = source[rng.Next(source.Count)];
        return buffer;
    }
}

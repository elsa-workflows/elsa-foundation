using System.Diagnostics;

namespace Elsa.DesignPersistence.Benchmarks.Harness;

/// <summary>
/// Executes the required measurement protocol for one target in one process: an untimed per-operation
/// warm-up, then a measured steady state that runs until BOTH at least <c>MinOperations</c> completed
/// operations AND at least <c>MinSteadyStateSeconds</c> have elapsed, honoring the operation's
/// requested concurrency. Every completed operation's latency is captured as a raw sample.
/// </summary>
public static class RunProtocol
{
    public static async Task<OperationSamples> MeasureAsync(
        BenchmarkOperation operation,
        ProtocolSettings settings,
        CancellationToken cancellationToken)
    {
        // Untimed warm-up (negative indices keep warm-up write identities disjoint from measured ones).
        for (var i = 0; i < settings.WarmupOperations; i++)
            await operation.InvokeAsync(-1 - i, cancellationToken);

        var latencies = new List<double>(1024);
        var addLock = new object();
        var invocation = 0L;
        var completed = 0;
        var stopwatch = Stopwatch.StartNew();

        async Task WorkerAsync()
        {
            while (Volatile.Read(ref completed) < settings.MinOperations ||
                   stopwatch.Elapsed.TotalSeconds < settings.MinSteadyStateSeconds)
            {
                var index = Interlocked.Increment(ref invocation);
                var opStart = Stopwatch.GetTimestamp();
                await operation.InvokeAsync(index, cancellationToken);
                var elapsedMs = Stopwatch.GetElapsedTime(opStart).TotalMilliseconds;
                lock (addLock)
                    latencies.Add(Math.Round(elapsedMs, 4)); // 0.1us precision keeps raw-sample files bounded
                Interlocked.Increment(ref completed);
            }
        }

        var workers = Enumerable.Range(0, Math.Max(1, operation.Concurrency))
            .Select(_ => Task.Run(WorkerAsync, cancellationToken))
            .ToArray();
        await Task.WhenAll(workers);
        stopwatch.Stop();

        var seconds = stopwatch.Elapsed.TotalSeconds;
        var throughput = seconds > 0 ? latencies.Count / seconds : 0;
        return new OperationSamples(
            operation.Name,
            operation.Kind.ToString(),
            operation.Concurrency,
            operation.ScaleBearing,
            latencies.Count,
            seconds,
            throughput,
            Statistics.Percentile(latencies, 50),
            Statistics.Percentile(latencies, 95),
            Statistics.Percentile(latencies, 99),
            latencies);
    }
}

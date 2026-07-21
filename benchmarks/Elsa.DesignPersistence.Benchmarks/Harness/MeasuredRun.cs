using Elsa.DesignPersistence.Benchmarks.Workload;

namespace Elsa.DesignPersistence.Benchmarks.Harness;

/// <summary>Runs one target's full workload in the current process and writes its raw-sample evidence
/// file. Correctness read hashes are always captured so the orchestrator can prove cross-target
/// parity before it trusts any timing.</summary>
public static class MeasuredRun
{
    public static async Task<ProcessRunResult> RunAsync(
        IBenchmarkTarget target,
        WorkloadScale scale,
        int seed,
        int runIndex,
        bool warmup,
        ProtocolSettings settings,
        string? outDir,
        CancellationToken cancellationToken)
    {
        await target.SeedAsync(cancellationToken);

        // Correctness: one deterministic invocation of every read operation, recorded as a hash.
        var correctnessHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var correctnessPassed = true;
        foreach (var operation in target.Operations.Where(o => o.Kind == OperationKind.Read))
        {
            try
            {
                correctnessHashes[operation.Name] = await operation.InvokeAsync(0, cancellationToken);
            }
            catch (Exception ex)
            {
                correctnessPassed = false;
                correctnessHashes[operation.Name] = $"ERROR:{ex.GetType().Name}";
            }
        }

        var operations = new List<OperationSamples>();
        if (!warmup)
        {
            foreach (var operation in target.Operations)
                operations.Add(await RunProtocol.MeasureAsync(operation, settings, cancellationToken));
        }
        else
        {
            // A warm-up process still exercises every operation once, untimed and discarded.
            foreach (var operation in target.Operations)
                await operation.InvokeAsync(-1, cancellationToken);
        }

        var plans = await target.CaptureQueryPlansAsync(cancellationToken);

        var result = new ProcessRunResult(
            Target: target.Key,
            Scale: scale.Name,
            Seed: seed,
            RunIndex: runIndex,
            Warmup: warmup,
            Smoke: settings.IsSmoke,
            CorrectnessChecked: true,
            CorrectnessPassed: correctnessPassed,
            CorrectnessHashes: correctnessHashes,
            Environment: EnvironmentMetadata.Capture(),
            Operations: operations,
            QueryPlans: plans);

        if (outDir is not null)
        {
            var suffix = warmup ? "warmup" : $"run{runIndex}";
            var path = Path.Combine(outDir, $"{target.Key}.{scale.Name}.{suffix}.json");
            Json.Write(path, result);
        }

        return result;
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;

internal static class BenchmarkFingerprint
{
    public static string Input(
        int seed,
        int traceCount,
        IReadOnlyCollection<OpenTelemetryBatch> batches,
        OpenTelemetryTraceFilter filter)
    {
        var canonical = JsonSerializer.Serialize(new CanonicalInput(seed, traceCount, batches, filter));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string OrderedTraceIds(OpenTelemetryTraceResult result)
    {
        var canonical = string.Join(
            '\n',
            result.Items.Select((trace, index) => $"{index}:{trace.TraceId}"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private sealed record CanonicalInput(
        int Seed,
        int TraceCount,
        IReadOnlyCollection<OpenTelemetryBatch> Batches,
        OpenTelemetryTraceFilter Filter);
}

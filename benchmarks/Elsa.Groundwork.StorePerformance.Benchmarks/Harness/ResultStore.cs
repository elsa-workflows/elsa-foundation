using System.Security.Cryptography;
using System.Text.Json;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

public sealed record ResultEnvelope<T>(int SchemaVersion, T Payload, string PayloadSha256);
public sealed record GateReport(GateResult Verdict, string? ReviewedReplacementSha256);

public static class ResultStore
{
    public static string Write<T>(string path, T payload)
    {
        ArtifactSafety.Validate(payload!);
        var canonical = JsonSerializer.SerializeToUtf8Bytes(payload, ArtifactStore.JsonOptions);
        var envelope = new ResultEnvelope<T>(1, payload, Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant());
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(envelope, ArtifactStore.JsonOptions));
        return path;
    }
}

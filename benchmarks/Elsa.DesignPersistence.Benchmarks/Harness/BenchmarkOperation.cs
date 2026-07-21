using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Elsa.DesignPersistence.Benchmarks.Harness;

public enum OperationKind
{
    Read,
    Write
}

/// <summary>
/// One gated ordinary-store operation from the Benchmark Acceptance Catalog. <see cref="InvokeAsync"/>
/// performs exactly one operation for the supplied monotonically increasing invocation index (writes
/// derive a fresh, collision-free identity from it) and returns a canonical result token. For read
/// operations the token is a domain-normalized result hash that MUST match across every adapter and
/// physical form; for write operations it is a stable outcome token used for liveness only.
/// </summary>
public sealed class BenchmarkOperation(
    string name,
    OperationKind kind,
    int concurrency,
    bool scaleBearing,
    Func<long, CancellationToken, Task<string>> invokeAsync)
{
    /// <summary>Catalog operation identity, e.g. <c>wf.identity.get</c>. Includes the concurrency
    /// suffix (<c>@c16</c>) for write variants.</summary>
    public string Name { get; } = name;

    public OperationKind Kind { get; } = kind;

    /// <summary>Requested worker concurrency (1 or 16 per the catalog).</summary>
    public int Concurrency { get; } = concurrency;

    /// <summary>Scale-bearing reads are the operations repeated at the 1M form-comparison scale.</summary>
    public bool ScaleBearing { get; } = scaleBearing;

    public Task<string> InvokeAsync(long invocation, CancellationToken cancellationToken) =>
        invokeAsync(invocation, cancellationToken);
}

/// <summary>Canonical, order-independent JSON hashing shared by every adapter so identical domain
/// results hash identically regardless of storage form. Mirrors the conformance fixture's hasher.</summary>
public static class CanonicalHash
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Of<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, Options);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            Write(element, writer);
        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    Write(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}

/// <summary>Provider-native query-plan evidence for one exercised scale-bearing route.</summary>
public sealed record QueryPlanEvidence(string Route, string Detail, bool UsesIndexedAccess);

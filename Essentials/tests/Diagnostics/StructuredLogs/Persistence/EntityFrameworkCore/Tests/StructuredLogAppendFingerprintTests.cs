using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;
using Xunit;
using System.Text.Json;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="StructuredLogAppendFingerprint"/> in isolation: the idempotency comparison
/// <c>EfStructuredLogStore.CommitBatchAsync</c> uses to tell a legitimate replay of the same batch from a batch id
/// reused with different content (#2140).
/// </summary>
public sealed class StructuredLogAppendFingerprintTests
{
    private static readonly StructuredLogStoreBinding Binding = new("tenant-a", "scope-a", "stream-a");

    public static IEnumerable<object[]> HashVectors()
    {
        using var source = typeof(StructuredLogAppendFingerprintTests).Assembly.GetManifestResourceStream("structured-log-hash-vectors.json")!;
        using var document = JsonDocument.Parse(source);
        return document.RootElement.EnumerateArray().Select(item => new object[] { item.Clone() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(HashVectors))]
    public void Append_fingerprint_pins_independent_frame_and_canonical_payload(JsonElement vector)
    {
        EfPendingAppend[] items = vector.TryGetProperty("items", out var batch)
            ? batch.EnumerateArray().Select(item => new EfPendingAppend(item.GetProperty("token").GetString()!, item.GetProperty("payload").GetString()!)).ToArray()
            : [new("token-1", vector.GetProperty("payload").GetString()!)];
        var actual = StructuredLogAppendFingerprint.Compute(new("tenant-𐀀", "scope-a", "stream-a"), items);
        Assert.True(actual == vector.GetProperty("sha256").GetString(),
            vector.GetProperty("name").GetString() + ": First-stable hash freeze: changes require an explicit identity/migration decision.");
    }

    [Fact]
    public void Record_order_and_frame_boundaries_remain_significant()
    {
        EfPendingAppend[] items = [new("token-1", "{\"x\":1}"), new("token-2", "{\"x\":2,\"nil\":null}")];
        Assert.NotEqual(StructuredLogAppendFingerprint.Compute(Binding, items), StructuredLogAppendFingerprint.Compute(Binding, items.Reverse().ToArray()));
        Assert.NotEqual(StructuredLogAppendFingerprint.Compute(new("ab", "c", "stream"), items),
            StructuredLogAppendFingerprint.Compute(new("a", "bc", "stream"), items));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("NaN")]
    [InlineData("[1,]")]
    public void Malformed_payload_is_refused_before_a_digest_is_returned(string payload) =>
        Assert.Throws<JsonException>(() => StructuredLogAppendFingerprint.Compute(Binding, [new("token-1", payload)]));

    [Fact]
    public void The_same_binding_and_payloads_produce_the_same_fingerprint()
    {
        var first = StructuredLogAppendFingerprint.Compute(
            Binding, [new EfPendingAppend("token-1", """{"Message":"first"}"""), new EfPendingAppend("token-2", """{"Message":"second"}""")]);
        var second = StructuredLogAppendFingerprint.Compute(
            Binding, [new EfPendingAppend("token-1", """{"Message":"first"}"""), new EfPendingAppend("token-2", """{"Message":"second"}""")]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_different_payload_produces_a_different_fingerprint()
    {
        var original = StructuredLogAppendFingerprint.Compute(Binding, [new EfPendingAppend("token-1", """{"Message":"first"}""")]);
        var changed = StructuredLogAppendFingerprint.Compute(Binding, [new EfPendingAppend("token-1", """{"Message":"different"}""")]);

        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void A_different_record_token_produces_a_different_fingerprint()
    {
        var original = StructuredLogAppendFingerprint.Compute(Binding, [new EfPendingAppend("token-1", """{"Message":"first"}""")]);
        var changed = StructuredLogAppendFingerprint.Compute(Binding, [new EfPendingAppend("token-2", """{"Message":"first"}""")]);

        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void A_different_binding_produces_a_different_fingerprint()
    {
        var items = new[] { new EfPendingAppend("token-1", """{"Message":"first"}""") };

        var original = StructuredLogAppendFingerprint.Compute(Binding, items);
        var changed = StructuredLogAppendFingerprint.Compute(new StructuredLogStoreBinding("tenant-b", "scope-a", "stream-a"), items);

        Assert.NotEqual(original, changed);
    }
}

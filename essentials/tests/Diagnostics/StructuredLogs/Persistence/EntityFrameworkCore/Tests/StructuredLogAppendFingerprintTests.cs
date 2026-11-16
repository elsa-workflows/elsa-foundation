using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="StructuredLogAppendFingerprint"/> in isolation: the idempotency comparison
/// <c>EfStructuredLogStore.CommitBatchAsync</c> uses to tell a legitimate replay of the same batch from a batch id
/// reused with different content (#2140).
/// </summary>
public sealed class StructuredLogAppendFingerprintTests
{
    private static readonly StructuredLogStoreBinding Binding = new("tenant-a", "scope-a", "stream-a");

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

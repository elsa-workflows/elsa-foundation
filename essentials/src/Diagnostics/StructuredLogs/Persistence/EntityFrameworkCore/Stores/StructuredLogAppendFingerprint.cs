using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// The idempotency fingerprint of one append batch: the binding it was appended under and every item's record token
/// and payload, hashed so two batches with the same content under the same binding compare equal (#2140). Public so
/// <see cref="EfStructuredLogStore"/>'s replay comparison is unit-testable directly, alongside the store's own
/// integration tests.
/// </summary>
public static class StructuredLogAppendFingerprint
{
    public static string Compute(
        StructuredLogStoreBinding binding,
        IReadOnlyList<EfPendingAppend> items)
    {
        var builder = new StringBuilder();
        Append(builder, binding.TenantId);
        Append(builder, binding.ScopeId);
        Append(builder, binding.StreamId);
        foreach (var item in items)
        {
            Append(builder, item.RecordToken);
            Append(builder, item.PayloadJson);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length).Append(':').Append(value);
    }
}

/// <summary>One item of an append batch as <see cref="StructuredLogAppendFingerprint.Compute"/> fingerprints it.</summary>
public sealed record EfPendingAppend(string RecordToken, string PayloadJson);

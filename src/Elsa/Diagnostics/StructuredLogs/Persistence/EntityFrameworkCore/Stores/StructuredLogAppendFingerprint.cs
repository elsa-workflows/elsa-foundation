using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;

internal static class StructuredLogAppendFingerprint
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

internal sealed record EfPendingAppend(string RecordToken, string PayloadJson);

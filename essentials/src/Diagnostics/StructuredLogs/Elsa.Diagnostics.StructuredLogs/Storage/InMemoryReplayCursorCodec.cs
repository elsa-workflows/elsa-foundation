using System.Security.Cryptography;
using System.Text;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Storage;

internal static class InMemoryReplayCursorCodec
{
    private const string Version = "slrc1";
    private const string Source = "memory";

    public static StructuredLogReplayCursor Encode(
        StructuredLogStoreBinding binding,
        string entrySourceId,
        string recordToken,
        string providerPosition) =>
        ReplayCursorEnvelope.Encode(Version, Source, binding, entrySourceId, recordToken, providerPosition);

    public static bool TryDecode(
        StructuredLogReplayCursor cursor,
        StructuredLogStoreBinding binding,
        out ReplayCursorParts parts) =>
        ReplayCursorEnvelope.TryDecode(cursor, Version, Source, binding, out parts);
}

internal static class ReplayCursorEnvelope
{
    public static StructuredLogReplayCursor Encode(
        string version,
        string source,
        StructuredLogStoreBinding binding,
        string entrySourceId,
        string recordToken,
        string providerPosition)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(entrySourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPosition);

        return new(string.Join(
            '.',
            version,
            source,
            Base64Url(Encoding.UTF8.GetBytes(entrySourceId)),
            BindingHash(binding),
            Base64Url(Encoding.UTF8.GetBytes(recordToken)),
            Base64Url(Encoding.UTF8.GetBytes(providerPosition))));
    }

    public static bool TryDecode(
        StructuredLogReplayCursor cursor,
        string expectedVersion,
        string expectedSource,
        StructuredLogStoreBinding expectedBinding,
        out ReplayCursorParts parts)
    {
        ArgumentNullException.ThrowIfNull(expectedBinding);
        parts = default;
        if (!cursor.IsValid)
            return false;
        var segments = cursor.Value?.Split('.');
        if (segments is not [var version, var source, var entrySource, var binding, var record, var provider] ||
            !StringComparer.Ordinal.Equals(version, expectedVersion) ||
            !StringComparer.Ordinal.Equals(source, expectedSource) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(binding),
                Encoding.ASCII.GetBytes(BindingHash(expectedBinding))))
            return false;

        try
        {
            var entrySourceId = Encoding.UTF8.GetString(FromBase64Url(entrySource));
            var recordToken = Encoding.UTF8.GetString(FromBase64Url(record));
            var providerPosition = Encoding.UTF8.GetString(FromBase64Url(provider));
            if (string.IsNullOrWhiteSpace(entrySourceId) ||
                string.IsNullOrWhiteSpace(recordToken) ||
                string.IsNullOrWhiteSpace(providerPosition))
                return false;

            parts = new(entrySourceId, recordToken, providerPosition);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string BindingHash(StructuredLogStoreBinding binding)
    {
        var canonical = $"{binding.TenantId.Length}:{binding.TenantId}{binding.ScopeId.Length}:{binding.ScopeId}{binding.StreamId.Length}:{binding.StreamId}";
        return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}

internal readonly record struct ReplayCursorParts(
    string EntrySourceId,
    string RecordToken,
    string ProviderPosition);

using System.Security.Cryptography;
using System.Text;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;

internal static class GroundworkReplayCursorCodec
{
    private const string Version = "slrc1";
    private const string Source = "groundwork";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static StructuredLogReplayCursor Encode(
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
            Version,
            Source,
            Base64Url(Encoding.UTF8.GetBytes(entrySourceId)),
            BindingHash(binding),
            Base64Url(Encoding.UTF8.GetBytes(recordToken)),
            Base64Url(Encoding.UTF8.GetBytes(providerPosition))));
    }

    public static bool TryDecode(
        StructuredLogReplayCursor cursor,
        StructuredLogStoreBinding expectedBinding,
        out GroundworkReplayCursorParts parts)
    {
        ArgumentNullException.ThrowIfNull(expectedBinding);
        parts = default;
        if (!cursor.IsValid)
            return false;

        var segments = cursor.Value?.Split('.');
        if (segments is not [Version, Source, var entrySource, var binding, var record, var provider] ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(binding),
                Encoding.ASCII.GetBytes(BindingHash(expectedBinding))))
            return false;

        try
        {
            var entrySourceId = DecodeCanonical(entrySource);
            var recordToken = DecodeCanonical(record);
            var providerPosition = DecodeCanonical(provider);
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
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string BindingHash(StructuredLogStoreBinding binding)
    {
        var canonical = StructuredLogsGroundworkStorageSchema.CanonicalBinding(binding);
        return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string DecodeCanonical(string value)
    {
        var bytes = FromBase64Url(value);
        if (!StringComparer.Ordinal.Equals(value, Base64Url(bytes)))
            throw new FormatException("The replay cursor contains a noncanonical Base64URL segment.");
        return StrictUtf8.GetString(bytes);
    }

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}

internal readonly record struct GroundworkReplayCursorParts(
    string EntrySourceId,
    string RecordToken,
    string ProviderPosition);

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Stores;

internal static class EfReplayCursorCodec
{
    private const string Version = "slrc1";
    private const string Source = "efcore";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static StructuredLogReplayCursor Encode(
        StructuredLogStoreBinding binding,
        string entrySourceId,
        string replayToken,
        long position)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentException.ThrowIfNullOrWhiteSpace(entrySourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replayToken);
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 1);

        return new(string.Join(
            '.',
            Version,
            Source,
            EncodeText(entrySourceId),
            BindingHash(binding),
            EncodeText(replayToken),
            EncodeText(position.ToString(CultureInfo.InvariantCulture))));
    }

    public static bool TryDecode(
        StructuredLogReplayCursor cursor,
        StructuredLogStoreBinding binding,
        out EfReplayCursorParts parts)
    {
        parts = default;
        if (!cursor.IsValid || binding is null)
            return false;

        var segments = cursor.Value.Split('.');
        if (segments is not [var version, var source, var entrySource, var bindingHash, var token, var position] ||
            !StringComparer.Ordinal.Equals(version, Version) ||
            !StringComparer.Ordinal.Equals(source, Source) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(bindingHash),
                Encoding.ASCII.GetBytes(BindingHash(binding))) ||
            !TryDecodeText(entrySource, out var entrySourceId) ||
            !TryDecodeText(token, out var replayToken) ||
            !TryDecodeText(position, out var positionText) ||
            string.IsNullOrWhiteSpace(entrySourceId) ||
            string.IsNullOrWhiteSpace(replayToken) ||
            !long.TryParse(positionText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPosition) ||
            parsedPosition < 1)
            return false;

        parts = new(entrySourceId, replayToken, parsedPosition);
        return true;
    }

    public static string BindingHash(StructuredLogStoreBinding binding)
    {
        var canonical = $"{binding.TenantId.Length}:{binding.TenantId}{binding.ScopeId.Length}:{binding.ScopeId}{binding.StreamId.Length}:{binding.StreamId}";
        return Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string EncodeText(string value) => Base64Url(StrictUtf8.GetBytes(value));

    private static bool TryDecodeText(string value, out string decoded)
    {
        decoded = "";
        if (string.IsNullOrEmpty(value) || value.Contains('='))
            return false;

        try
        {
            var bytes = FromBase64Url(value);
            decoded = StrictUtf8.GetString(bytes);
            return StringComparer.Ordinal.Equals(Base64Url(bytes), value);
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

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        if (value.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new FormatException();

        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}

internal readonly record struct EfReplayCursorParts(string EntrySourceId, string ReplayToken, long Position);

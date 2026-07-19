using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Services.Coalescing;

internal sealed record CoalescingRuntimeStoreCursor(
    string LastIdentity,
    string? InnerContinuation,
    bool InnerExhausted);

internal static class CoalescingRuntimeStoreContinuation
{
    public static string Encode(string binding, CoalescingRuntimeStoreCursor cursor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentException.ThrowIfNullOrWhiteSpace(cursor.LastIdentity);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new Payload(binding, cursor));
        return $"crsp1.{Encode(payload)}.{Encode(SHA256.HashData(payload))}";
    }

    public static CoalescingRuntimeStoreCursor? Decode(string? token, string binding, string parameterName)
    {
        if (token is null)
            return null;

        try
        {
            var parts = token.Split('.');
            if (parts is not ["crsp1", var encodedPayload, var encodedChecksum])
                throw new FormatException();
            var bytes = Decode(encodedPayload);
            var suppliedChecksum = Decode(encodedChecksum);
            var expectedChecksum = SHA256.HashData(bytes);
            if (suppliedChecksum.Length != expectedChecksum.Length ||
                !CryptographicOperations.FixedTimeEquals(suppliedChecksum, expectedChecksum))
                throw new FormatException();

            var payload = JsonSerializer.Deserialize<Payload>(bytes) ?? throw new FormatException();
            if (!StringComparer.Ordinal.Equals(payload.Binding, binding) ||
                string.IsNullOrWhiteSpace(payload.Cursor.LastIdentity))
                throw new FormatException();
            return payload.Cursor;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            throw new ArgumentException(
                "The runtime store continuation token is invalid or belongs to another query.",
                parameterName,
                exception);
        }
    }

    private static string Encode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch { 0 => "", 2 => "==", 3 => "=", _ => throw new FormatException() };
        return Convert.FromBase64String(base64);
    }

    private sealed record Payload(string Binding, CoalescingRuntimeStoreCursor Cursor);
}

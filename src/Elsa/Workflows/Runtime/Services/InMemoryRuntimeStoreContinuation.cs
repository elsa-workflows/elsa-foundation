using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Runtime.Core.Services;

internal static class InMemoryRuntimeStoreContinuation
{
    public static string Encode(string queryBinding, string lastIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryBinding);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastIdentity);
        var payload = Encoding.UTF8.GetBytes($"{queryBinding}\0{lastIdentity}");
        var checksum = SHA256.HashData(payload);
        return $"imrs1.{Base64UrlEncode(payload)}.{Base64UrlEncode(checksum)}";
    }

    public static string? Decode(string? continuationToken, string queryBinding, string parameterName)
    {
        if (continuationToken is null)
            return null;

        try
        {
            var parts = continuationToken.Split('.');
            if (parts is not ["imrs1", var encodedPayload, var encodedChecksum])
                throw new FormatException();

            var payload = Base64UrlDecode(encodedPayload);
            var suppliedChecksum = Base64UrlDecode(encodedChecksum);
            var expectedChecksum = SHA256.HashData(payload);
            if (suppliedChecksum.Length != expectedChecksum.Length ||
                !CryptographicOperations.FixedTimeEquals(suppliedChecksum, expectedChecksum))
            {
                throw new FormatException();
            }

            var decoded = Encoding.UTF8.GetString(payload);
            var separator = decoded.IndexOf('\0');
            if (separator <= 0 ||
                separator == decoded.Length - 1 ||
                !StringComparer.Ordinal.Equals(decoded[..separator], queryBinding))
            {
                throw new FormatException();
            }

            return decoded[(separator + 1)..];
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException(
                "The runtime store continuation token is invalid or belongs to another query.",
                parameterName,
                exception);
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            0 => "",
            2 => "==",
            3 => "=",
            _ => throw new FormatException()
        };
        return Convert.FromBase64String(base64);
    }
}

using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>HMAC-SHA256 protector for opaque runtime recovery continuation tokens.</summary>
public sealed class HmacRuntimeRecoveryContinuationCodec : IRuntimeRecoveryContinuationCodec
{
    private readonly byte[] _key;

    public HmacRuntimeRecoveryContinuationCodec(IOptions<RuntimeRecoveryContinuationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!options.Value.AllowEphemeralDevelopmentKey)
            {
                throw new InvalidOperationException(
                    "Runtime recovery continuation signing key must be configured for durable recovery paging.");
            }

            _key = RandomNumberGenerator.GetBytes(32);
        }
        else
        {
            _key = Encoding.UTF8.GetBytes(configured);
        }

        if (_key.Length < 32)
            throw new InvalidOperationException("Runtime recovery continuation signing key must contain at least 32 UTF-8 bytes.");
    }

    public string Encode(string purpose, ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var payloadBytes = payload.ToArray();
        var signature = HMACSHA256.HashData(_key, SigningInput(purpose, payloadBytes));
        return $"{purpose}.{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    public byte[] Decode(string purpose, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        try
        {
            var parts = token.Split('.', StringSplitOptions.None);
            if (parts is not [var tokenPurpose, var encodedPayload, var encodedSignature] ||
                !StringComparer.Ordinal.Equals(tokenPurpose, purpose))
            {
                throw new FormatException();
            }

            var payload = Base64UrlDecode(encodedPayload);
            var suppliedSignature = Base64UrlDecode(encodedSignature);
            var expectedSignature = HMACSHA256.HashData(_key, SigningInput(purpose, payload));
            if (suppliedSignature.Length != expectedSignature.Length ||
                !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                throw new FormatException();
            }

            return payload;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new ArgumentException("The runtime recovery continuation token is invalid.", nameof(token), exception);
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] SigningInput(string purpose, ReadOnlySpan<byte> payload)
    {
        var purposeBytes = Encoding.UTF8.GetBytes(purpose);
        var input = new byte[purposeBytes.Length + 1 + payload.Length];
        purposeBytes.CopyTo(input, 0);
        input[purposeBytes.Length] = (byte)'.';
        payload.CopyTo(input.AsSpan(purposeBytes.Length + 1));
        return input;
    }

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

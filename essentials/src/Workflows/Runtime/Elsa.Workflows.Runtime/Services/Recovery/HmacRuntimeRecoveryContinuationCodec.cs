using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Services.Recovery;

/// <summary>HMAC-SHA256 protector for opaque runtime recovery continuation tokens.</summary>
public sealed class HmacRuntimeRecoveryContinuationCodec : IRuntimeRecoveryContinuationCodec
{
    private readonly byte[] _key;
    private readonly byte[] _jsonKey;

    public HmacRuntimeRecoveryContinuationCodec(IOptions<RuntimeRecoveryContinuationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!options.Value.AllowEphemeralDevelopmentKey)
            {
                throw new InvalidOperationException(
                    "Runtime recovery continuation signing key must be configured for durable recovery paging. " +
                    "Set RuntimeRecoveryContinuationOptions.SigningKey to at least 32 UTF-8 bytes, shared by every " +
                    "node that consumes recovery pages; shell hosts configure it through the durable runtime " +
                    "persistence feature's RecoveryContinuationSigningKey setting.");
            }

            _key = RandomNumberGenerator.GetBytes(32);
        }
        else
        {
            _key = Encoding.UTF8.GetBytes(configured);
        }

        if (_key.Length < 32)
            throw new InvalidOperationException("Runtime recovery continuation signing key must contain at least 32 UTF-8 bytes.");
        // Binary callers accept arbitrary purposes and bytes, so a purpose prefix alone cannot create a
        // disjoint signing domain. Derive a separate JSON key without changing any binary token contract.
        _jsonKey = HMACSHA256.HashData(_key, "Elsa.RuntimeRecoveryContinuation.JsonHashInput.v1"u8);
    }

    public string Encode(string purpose, ReadOnlySpan<byte> payload) => EncodeCore(purpose, payload, json: false);

    /// <summary>Signs canonical JSON in an explicit domain while retaining its original payload bytes.</summary>
    public string EncodeJson(string purpose, ReadOnlySpan<byte> payload) => EncodeCore(purpose, payload, json: true);

    private string EncodeCore(string purpose, ReadOnlySpan<byte> payload, bool json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var payloadBytes = payload.ToArray();
        var signature = Signature(purpose, payloadBytes, json);
        return $"{purpose}.{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    public byte[] Decode(string purpose, string token) => DecodeCore(purpose, token, json: false);

    /// <summary>Verifies the JSON domain and returns the retained representation without re-encoding it.</summary>
    public byte[] DecodeJson(string purpose, string token) => DecodeCore(purpose, token, json: true);

    private byte[] DecodeCore(string purpose, string token, bool json)
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
            var expectedSignature = Signature(purpose, payload, json);
            if (suppliedSignature.Length != expectedSignature.Length ||
                !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                throw new FormatException();
            }

            return payload;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or JsonException or InvalidOperationException)
        {
            throw new ArgumentException("The runtime recovery continuation token is invalid.", nameof(token), exception);
        }
    }

    private byte[] Signature(string purpose, byte[] payload, bool json)
    {
        if (!json) return HMACSHA256.HashData(_key, SigningInput(purpose, payload));
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 1000 });
        var canonical = Elsa.Serialization.Core.JsonHashInput.CanonicalBytes(document.RootElement);
        return Elsa.Serialization.Core.JsonHashInput.HashKeyedFramed(SigningInput("json:" + purpose, canonical), _jsonKey);
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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>
/// Signing key for the opaque tokens the Activities design API hands to clients: dependency cursors, management
/// cursors and fork candidate ids. Hosts set it through the feature's <c>DependencyCursorSigningKey</c> setting.
/// </summary>
public sealed class ActivityTokenSigningOptions
{
    public string SigningKey { get; set; } = null!;
}

/// <summary>
/// The signed token format those codecs share: <c>base64url(JSON state) "." base64url(HMAC-SHA256(key, JSON state))</c>.
/// Clients hold tokens across requests, so the format and the signature input are a wire contract and must not
/// change. Each codec keeps its own state validation and its own invalid-token exception.
/// </summary>
internal sealed class HmacTokenCodec<TState> where TState : class
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _key;

    public HmacTokenCodec(string? signingKey, string tokenName)
    {
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException($"{tokenName} signing key must contain at least 32 UTF-8 bytes.");
        _key = Encoding.UTF8.GetBytes(signingKey);
    }

    public string Encode(TState state)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        return $"{Base64Url(payload)}.{Base64Url(HMACSHA256.HashData(_key, payload))}";
    }

    /// <summary>
    /// Returns the signed state, or <c>null</c> when the token is malformed, was not signed with this key, or does
    /// not hold a <typeparamref name="TState"/>.
    /// </summary>
    public TState? Decode(string token)
    {
        try
        {
            if (token.Split('.') is not [var payloadPart, var signaturePart])
                return null;
            var payload = FromBase64Url(payloadPart);
            var signature = FromBase64Url(signaturePart);
            return CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(_key, payload))
                ? JsonSerializer.Deserialize<TState>(payload, JsonOptions)
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}

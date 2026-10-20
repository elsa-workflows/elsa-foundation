using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Etl.Design.Api;

internal sealed class EtlCursorCodec
{
    private const int FormatVersion = 1;
    private const int MaximumTokenLength = 4096;
    private const int MaximumScalarLength = 512;
    private const int MaximumDefinitionIdLength = 450;
    private const int MaximumPageSize = 1000;
    private const int SignatureLength = 32;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] _signingKey;

    public EtlCursorCodec(string signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        try
        {
            _signingKey = StrictUtf8.GetBytes(signingKey);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("The ETL cursor signing key must be valid UTF-8 text.", nameof(signingKey), exception);
        }

        if (_signingKey.Length < 32)
            throw new ArgumentException("The ETL cursor signing key must contain at least 32 UTF-8 bytes.", nameof(signingKey));
    }

    public string Encode(EtlCursorState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsValidState(state))
            throw new ArgumentException("The ETL cursor state is invalid or outside its supported bounds.", nameof(state));

        var payload = JsonSerializer.SerializeToUtf8Bytes(state, EtlCursorJsonContext.Default.EtlCursorState);
        var signature = HMACSHA256.HashData(_signingKey, payload);
        var token = $"{EncodeBase64Url(payload)}.{EncodeBase64Url(signature)}";
        if (token.Length > MaximumTokenLength)
            throw new ArgumentException("The encoded ETL cursor exceeds its supported length.", nameof(state));
        return token;
    }

    public EtlCursorState Decode(string token)
    {
        if (token is null || token.Length is 0 or > MaximumTokenLength)
            throw InvalidToken();

        var separator = token.IndexOf('.');
        if (separator <= 0 || separator != token.LastIndexOf('.') || separator == token.Length - 1)
            throw InvalidToken();

        byte[] payload;
        byte[] signature;
        try
        {
            payload = DecodeBase64Url(token.AsSpan(0, separator));
            signature = DecodeBase64Url(token.AsSpan(separator + 1));
        }
        catch (FormatException)
        {
            throw InvalidToken();
        }

        if (signature.Length != SignatureLength)
            throw InvalidToken();
        var expectedSignature = HMACSHA256.HashData(_signingKey, payload);
        if (!CryptographicOperations.FixedTimeEquals(signature, expectedSignature))
            throw InvalidToken();

        EtlCursorState? state;
        try
        {
            state = JsonSerializer.Deserialize(payload, EtlCursorJsonContext.Default.EtlCursorState);
        }
        catch (JsonException exception)
        {
            throw InvalidToken(exception);
        }

        if (!IsValidState(state))
            throw InvalidToken();

        var canonicalPayload = JsonSerializer.SerializeToUtf8Bytes(state, EtlCursorJsonContext.Default.EtlCursorState);
        if (!payload.AsSpan().SequenceEqual(canonicalPayload))
            throw InvalidToken();

        return state;
    }

    private static bool IsValidState(EtlCursorState? state) =>
        state is not null &&
        state.Format == FormatVersion &&
        (state.Kind is "definitions" or "versions" or "bindings") &&
        IsLowercaseSha256(state.ScopeKey) &&
        IsBoundedOptional(state.ResourceId, MaximumDefinitionIdLength) &&
        state.Limit is >= 1 and <= MaximumPageSize &&
        IsBoundedOptional(state.LastId, MaximumDefinitionIdLength) &&
        state.LastVersion >= 0 &&
        state.ThroughVersion >= 0 &&
        IsBoundedOptional(state.LastAlias, MaximumScalarLength) &&
        IsBoundedOptional(state.LastRole, MaximumScalarLength);

    private static bool IsBoundedOptional(string? value, int maximumLength) => value is null || value.Length <= maximumLength;

    private static bool IsLowercaseSha256(string? value)
    {
        if (value is null || value.Length != 64)
            return false;
        foreach (var character in value)
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        return true;
    }

    private static string EncodeBase64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value.Length % 4 == 1)
            throw InvalidToken();
        foreach (var character in value)
            if (character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-' and not '_')
                throw InvalidToken();

        var standard = value.ToString().Replace('-', '+').Replace('_', '/');
        standard += new string('=', (4 - standard.Length % 4) % 4);
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(standard);
        }
        catch (FormatException)
        {
            throw InvalidToken();
        }

        if (!StringComparer.Ordinal.Equals(EncodeBase64Url(decoded), value.ToString()))
            throw InvalidToken();
        return decoded;
    }

    private static FormatException InvalidToken(Exception? innerException = null) =>
        new("The ETL cursor is invalid or unsupported.", innerException);
}

internal sealed record EtlCursorState(
    int Format,
    string Kind,
    string ScopeKey,
    string? ResourceId,
    int Limit,
    string? LastId,
    int LastVersion,
    int ThroughVersion,
    string? LastAlias,
    string? LastRole);

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = false)]
[JsonSerializable(typeof(EtlCursorState))]
internal partial class EtlCursorJsonContext : JsonSerializerContext
{
}

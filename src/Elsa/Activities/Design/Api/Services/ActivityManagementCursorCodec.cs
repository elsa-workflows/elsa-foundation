using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Api.Services;

public sealed record ActivityManagementCursorState(string Scope, int Offset, DateTimeOffset AsOf);

public interface IActivityManagementCursorCodec
{
    string Encode(ActivityManagementCursorState state);
    ActivityManagementCursorState Decode(string cursor);
}

public sealed class ActivityManagementCursorInvalidException : Exception
{
    public ActivityManagementCursorInvalidException() : base("The activity management cursor is invalid.") { }
}

public sealed class HmacActivityManagementCursorCodec : IActivityManagementCursorCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _key;

    public HmacActivityManagementCursorCodec(IOptions<ActivityDependencyCursorOptions> options)
    {
        var signingKey = options.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("Activity management cursor signing key must contain at least 32 UTF-8 bytes.");
        _key = Encoding.UTF8.GetBytes(signingKey);
    }

    public string Encode(ActivityManagementCursorState state)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    public ActivityManagementCursorState Decode(string cursor)
    {
        try
        {
            var parts = cursor.Split('.');
            if (parts is not [var payloadPart, var signaturePart]) throw new ActivityManagementCursorInvalidException();
            var payload = FromBase64Url(payloadPart);
            var signature = FromBase64Url(signaturePart);
            var expected = HMACSHA256.HashData(_key, payload);
            if (!CryptographicOperations.FixedTimeEquals(signature, expected)) throw new ActivityManagementCursorInvalidException();
            var state = JsonSerializer.Deserialize<ActivityManagementCursorState>(payload, JsonOptions);
            if (state is null || state.Offset < 0 || string.IsNullOrWhiteSpace(state.Scope))
                throw new ActivityManagementCursorInvalidException();
            return state;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ActivityManagementCursorInvalidException)
        {
            throw new ActivityManagementCursorInvalidException();
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

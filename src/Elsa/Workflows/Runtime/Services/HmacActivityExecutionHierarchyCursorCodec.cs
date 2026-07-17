using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class ActivityExecutionHierarchyCursorOptions
{
    public string? SigningKey { get; set; }
}

public sealed class HmacActivityExecutionHierarchyCursorCodec : IActivityExecutionHierarchyCursorCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _key;

    public HmacActivityExecutionHierarchyCursorCodec(IOptions<ActivityExecutionHierarchyCursorOptions> options)
    {
        var configured = options.Value.SigningKey;
        _key = string.IsNullOrWhiteSpace(configured)
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(configured);
        if (_key.Length < 32)
            throw new InvalidOperationException("Activity execution hierarchy cursor signing key must contain at least 32 UTF-8 bytes.");
    }

    public string Encode(ActivityExecutionHierarchyCursorState state)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        return $"{Base64Url(payload)}.{Base64Url(HMACSHA256.HashData(_key, payload))}";
    }

    public ActivityExecutionHierarchyCursorState Decode(string cursor)
    {
        try
        {
            var parts = cursor.Split('.', StringSplitOptions.None);
            if (parts is not [var payloadPart, var signaturePart])
                throw Invalid();
            var payload = FromBase64Url(payloadPart);
            var signature = FromBase64Url(signaturePart);
            if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(_key, payload)))
                throw Invalid();
            var state = JsonSerializer.Deserialize<ActivityExecutionHierarchyCursorState>(payload, JsonOptions);
            if (state is null || state.SchemaVersion != 1 || state.EffectiveLimit <= 0 || state.CommittedThroughSequence < 0)
                throw Invalid();
            return state;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw Invalid(exception);
        }
    }

    private static ActivityExecutionHierarchyCursorException Invalid(Exception? inner = null) =>
        new(ActivityExecutionHierarchyCursorFailure.Invalid, "The activity execution hierarchy cursor is invalid.", inner);

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}

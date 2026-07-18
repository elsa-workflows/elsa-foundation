using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Api.Services;

public sealed record ActivityForkCandidateIdState(
    string ReservationId,
    string RequestFingerprint,
    DateTimeOffset ExpiresAt);

public interface IActivityForkCandidateIdCodec
{
    string Encode(ActivityForkCandidateIdState state);

    ActivityForkCandidateIdState Decode(string candidateId);
}

public sealed class ActivityForkCandidateIdInvalidException : Exception
{
    public ActivityForkCandidateIdInvalidException() : base("The activity fork candidate identity is invalid.") { }
}

public sealed class HmacActivityForkCandidateIdCodec(IOptions<ActivityDependencyCursorOptions> options)
    : IActivityForkCandidateIdCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _key = ResolveKey(options.Value.SigningKey);

    public string Encode(ActivityForkCandidateIdState state)
    {
        if (string.IsNullOrWhiteSpace(state.ReservationId) ||
            string.IsNullOrWhiteSpace(state.RequestFingerprint))
            throw new ArgumentException("Fork candidate identity material is required.", nameof(state));

        var payload = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var signature = HMACSHA256.HashData(_key, payload);
        return $"{Base64Url(payload)}.{Base64Url(signature)}";
    }

    public ActivityForkCandidateIdState Decode(string candidateId)
    {
        try
        {
            if (candidateId.Split('.') is not [var payloadPart, var signaturePart])
                throw new ActivityForkCandidateIdInvalidException();
            var payload = FromBase64Url(payloadPart);
            var suppliedSignature = FromBase64Url(signaturePart);
            var expectedSignature = HMACSHA256.HashData(_key, payload);
            if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
                throw new ActivityForkCandidateIdInvalidException();
            var state = JsonSerializer.Deserialize<ActivityForkCandidateIdState>(payload, JsonOptions);
            if (state is null ||
                string.IsNullOrWhiteSpace(state.ReservationId) ||
                string.IsNullOrWhiteSpace(state.RequestFingerprint) ||
                state.ExpiresAt == default)
                throw new ActivityForkCandidateIdInvalidException();
            return state;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ActivityForkCandidateIdInvalidException)
        {
            throw new ActivityForkCandidateIdInvalidException();
        }
    }

    private static byte[] ResolveKey(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("Activity fork candidate signing key must contain at least 32 UTF-8 bytes.");
        return Encoding.UTF8.GetBytes(signingKey);
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

public sealed class ActivityForkReservationOptions
{
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(1);
}

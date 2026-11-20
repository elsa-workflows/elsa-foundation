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

public sealed class HmacActivityForkCandidateIdCodec(IOptions<ActivityTokenSigningOptions> options)
    : IActivityForkCandidateIdCodec
{
    private readonly HmacTokenCodec<ActivityForkCandidateIdState> _codec = new(options.Value.SigningKey, "Activity fork candidate");

    public string Encode(ActivityForkCandidateIdState state)
    {
        if (string.IsNullOrWhiteSpace(state.ReservationId) ||
            string.IsNullOrWhiteSpace(state.RequestFingerprint))
            throw new ArgumentException("Fork candidate identity material is required.", nameof(state));

        return _codec.Encode(state);
    }

    public ActivityForkCandidateIdState Decode(string candidateId) =>
        _codec.Decode(candidateId) is { } state &&
        !string.IsNullOrWhiteSpace(state.ReservationId) &&
        !string.IsNullOrWhiteSpace(state.RequestFingerprint) &&
        state.ExpiresAt != default
            ? state
            : throw new ActivityForkCandidateIdInvalidException();
}

public sealed class ActivityForkReservationOptions
{
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(1);
}

using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Api.Services;

public sealed record ActivityManagementCursorState(string Scope, int Offset, long SnapshotSequence);

public interface IActivityManagementCursorCodec
{
    string Encode(ActivityManagementCursorState state);
    ActivityManagementCursorState Decode(string cursor);
}

public sealed class ActivityManagementCursorInvalidException : Exception
{
    public ActivityManagementCursorInvalidException() : base("The activity management cursor is invalid.") { }
}

public sealed class HmacActivityManagementCursorCodec(IOptions<ActivityTokenSigningOptions> options) : IActivityManagementCursorCodec
{
    private readonly HmacTokenCodec<ActivityManagementCursorState> _codec = new(options.Value.SigningKey, "Activity management cursor");

    public string Encode(ActivityManagementCursorState state) => _codec.Encode(state);

    public ActivityManagementCursorState Decode(string cursor) =>
        _codec.Decode(cursor) is { Offset: >= 0, SnapshotSequence: >= 0 } state && !string.IsNullOrWhiteSpace(state.Scope)
            ? state
            : throw new ActivityManagementCursorInvalidException();
}

using Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

/// <summary>The durable publication that an installed host can transfer to standalone Event ingress.</summary>
/// <remarks><c>EventName</c> is the exact actor correlation key within the bound tenant.</remarks>
public sealed record AuthoredStandaloneEvent
{
    internal AuthoredStandaloneEvent(string occurrence, ActorRef publisher, string eventName, string? payload,
        long committedAt, string transferFingerprint) =>
        (Occurrence, Publisher, EventName, Payload, CommittedAt, TransferFingerprint) =
        (occurrence, publisher, eventName, payload, committedAt, transferFingerprint);

    public string Occurrence { get; }
    public ActorRef Publisher { get; }
    public string EventName { get; }
    public string? Payload { get; }
    public long CommittedAt { get; }
    public string TransferFingerprint { get; }
    public AuthoredStandaloneEventCursor Cursor => new(CommittedAt, Occurrence);
}

/// <summary>An opaque position for one ordered pass over pending standalone Event transfers.</summary>
public sealed class AuthoredStandaloneEventCursor
{
    internal AuthoredStandaloneEventCursor(long committedAt, string occurrence) =>
        (CommittedAt, Occurrence) = (committedAt, occurrence);

    internal long CommittedAt { get; }
    internal string Occurrence { get; }
}

/// <summary>A host-bound, at-least-once transfer edge. Acknowledge only after durable destination acceptance.</summary>
public interface IAuthoredStandaloneEventOutbox
{
    /// <summary>Returns one bounded page. A new pass starts at a null cursor and includes any unacknowledged rows.</summary>
    ValueTask<IReadOnlyList<AuthoredStandaloneEvent>> ReadAsync(AuthoredStandaloneEventCursor? after = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an exact pending row. False also covers a duplicate acknowledgement.</summary>
    ValueTask<bool> AcknowledgeAsync(string occurrence, string transferFingerprint, CancellationToken cancellationToken = default);
}

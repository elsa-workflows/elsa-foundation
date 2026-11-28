namespace Kernel.Foundation.Contracts;

/// <summary>The durable publication that an installed host can transfer to standalone Event ingress.</summary>
/// <remarks><c>EventName</c> is the exact actor correlation key within the bound tenant.</remarks>
public sealed record AuthoredStandaloneEvent(string Occurrence, ActorRef Publisher, int PublisherPartition, string EventName,
    string? Payload, long CommittedAt, string TransferFingerprint)
{
    public AuthoredStandaloneEventCursor Cursor => new(CommittedAt, Occurrence);
}

/// <summary>A keyset position for one ordered pass over pending standalone Event transfers.</summary>
public sealed record AuthoredStandaloneEventCursor(long CommittedAt, string Occurrence);

/// <summary>A host-bound, at-least-once transfer edge. Acknowledge only after durable destination acceptance.</summary>
public interface IAuthoredStandaloneEventOutbox
{
    /// <summary>Returns one bounded page. A new pass starts at a null cursor and includes any unacknowledged rows.</summary>
    ValueTask<IReadOnlyList<AuthoredStandaloneEvent>> ReadAsync(AuthoredStandaloneEventCursor? after = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an exact pending row. False also covers a duplicate acknowledgement.</summary>
    ValueTask<bool> AcknowledgeAsync(string occurrence, string transferFingerprint, CancellationToken cancellationToken = default);
}

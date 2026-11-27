using System.Text.Json.Serialization;

namespace Kernel.Foundation.Contracts;

/// <summary>An opaque continuation for a read.</summary>
public readonly record struct PageToken(string Value);

public sealed record Page<T>(IReadOnlyList<T> Items, PageToken? Next, long AsOf)
{
    /// <summary>The actor's committed revision in this journal snapshot, including an empty page for a live actor.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? JournalHeadRevision { get; init; }

    /// <summary>An opaque bookmark beyond the journal commits visible in this snapshot. Use it to read later commits.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PageToken? JournalCatchUpToken { get; init; }

    /// <summary>The actor's committed cancellation in the same journal snapshot, when retained.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JournalCancellation? JournalCancellation { get; init; }
}

public sealed record ActorView(ActorHeader Header, string? Selection, ParkView? Park, long AsOf);

public sealed record ActorQuery(
    string? Type = null,
    ActorStatus? Status = null,
    IReadOnlyDictionary<string, string>? Labels = null,
    long? EndedBefore = null,
    bool? Parked = null);

public sealed record ParkView(long At, string? OpenResolution);

public sealed record JournalEntry(long Revision, string Entry)
{
    /// <summary>The zero-based entry order within the committed revision, when exposed by the reader.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CommitOrdinal { get; init; }

    // Readers may omit the ordinal. Preserve equality for callers comparing journal contents.
    public bool Equals(JournalEntry? other) => other is not null && Revision == other.Revision && Entry == other.Entry;

    public override int GetHashCode() => HashCode.Combine(Revision, Entry);
}

public sealed record JournalCancellation(long Revision, long At);

public enum DeliveryState { Pending, Held, Consumed, Arrived }

public sealed record DeliveryView(
    string Handle,
    string Id,
    string Kind,
    ActorRef? Sender,
    long? Due,
    DeliveryState State,
    int Attempts,
    long Size);

public interface IActorReads
{
    ValueTask<ActorView?> GetAsync(BoundScope scope, ActorRef actor, CancellationToken cancellationToken = default);

    ValueTask<Page<ActorView>> ListAsync(BoundScope scope, ActorQuery query, PageToken? from = null,
        CancellationToken cancellationToken = default);

    /// <summary>A nonempty page returns a bookmark after its last entry, including when the reader catches up.</summary>
    ValueTask<Page<JournalEntry>> TailAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default);

    ValueTask<Page<DeliveryView>> InboxAsync(BoundScope scope, ActorRef actor, PageToken? from = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Optional exact read of a completed actor's retained named outputs, under the host's bound scope.</summary>
public interface IActorTerminalOutputReads
{
    /// <summary>Reads the output from the supplied terminal time. A metadata-only read omits the value body.</summary>
    ValueTask<ActorTerminalOutput?> ReadTerminalOutputAsync(BoundScope scope, ActorRef actor, long endedAt, bool includeValues,
        CancellationToken cancellationToken = default);
}

/// <summary>The authored names as JSON, the complete UTF-8 JSON output object when requested and retained, and its commit time.</summary>
public sealed record ActorTerminalOutput(long CapturedAt, string Names, bool Omitted, byte[]? Values);

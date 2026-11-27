using System.Text.Json.Serialization;

namespace Kernel.Foundation.Contracts;

/// <summary>An opaque continuation for a read.</summary>
public readonly record struct PageToken(string Value);

public sealed record Page<T>(IReadOnlyList<T> Items, PageToken? Next, long AsOf)
{
    /// <summary>The actor's committed revision in the snapshot that produced a nonempty journal page.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? JournalHeadRevision { get; init; }

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

public sealed record JournalEntry(long Revision, string Entry);

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

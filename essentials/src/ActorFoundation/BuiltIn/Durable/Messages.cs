using System.Text.Json;
using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>
/// Whether an actor still runs, and how it ended. Layer 3's meaning (a workflow's status), kept here because the row
/// carries it and the live output ends with it: a seam leak (FINDINGS section 24).
/// </summary>
public enum InstanceStatus { Running, Finished, Faulted, Cancelled }

/// <summary>What each commit of an instance leaves in the journal: the model's entries, only that its state changed, or nothing.</summary>
public enum History { Full, State, Minimal }

/// <summary>What a message needs to create its target when the target does not exist.</summary>
public sealed record CreationStart(long Generation, long Previous, string Intent);

public sealed record StartSpec(string Model, string Definition, string? Input = null, string? Parent = null, bool Ephemeral = false, History History = History.Full,
    CreationStart? Creation = null, long ParentCreation = 0, TimeSpan? ShortWait = null)
{
    internal string? FoundationInlineTenant { get; init; }
}

public sealed record SendOptions(TimeSpan? Delay = null, StartSpec? Start = null, long? TargetCreation = null, long? DueAt = null);

/// <summary>The storage key scopes an opaque wire ID without modifying it. Positive scopes identify
/// tickets by the exact split of the logical actor!key string, including when actor or key contains '!'.</summary>
public readonly record struct InboxKey(string Id, long Scope = 0, string? Match = null)
{
    internal const long ChildCreatorScope = long.MaxValue;
    internal const long EventDeliveryScope = -2;
    internal const long EventCreatorScope = -3;
    internal const long FoundationOccurrenceScope = -4;
    public bool HasIssuedOrigin => Scope == -1 || Scope > 0;
    public static InboxKey Issued(string id) => new(id, -1);
    public static InboxKey Custom(string actor, string id) => new(id, (long)int.MaxValue + 2 + actor.Length);
    public static InboxKey Ticket(string actor, string key, long creation = 0) => Ticket(actor, key, creation, 0);

    internal static InboxKey Ticket(string actor, string key, long creation, long generation)
    {
        if (generation < 0 || generation > 0 && creation <= 0) throw new ArgumentOutOfRangeException(nameof(generation));
        var id = Worker.TicketId(actor, key, creation);
        if (generation > 0) id += FormattableString.Invariant($"#g{generation}");
        return new(id, (long)CreationProtocol.Origin(actor, creation).Length + 1);
    }

    internal static InboxKey JobResult(string actor, string key, long creation, long generation = 0)
    {
        var ticket = Ticket(actor, key, creation, generation);
        return ticket with { Id = ticket.Id + "/result" };
    }
    public static implicit operator InboxKey(string ordinaryId) => new(ordinaryId);
    public override string ToString() => Id;
}

internal sealed record FoundationOperatorCommand(string? Selection)
{
    internal string? DeleteTarget { get; init; }
    internal bool RetentionDelete { get; init; }
    internal FoundationTerminalStamp? QualifiedTerminal { get; init; }
    internal bool RootThenDelete { get; init; }
    internal bool RootHeldReply { get; init; }
    internal ParkRecoveryMode? RecoverMode { get; init; }
    internal IReadOnlyList<string>? RecoverHandles { get; init; }
    // A qualified root recover: the Park mark its selection sealed, the admission's ordinary window in milliseconds
    // for the rows it restores or clears, and the store's Park facts read when it was polled.
    internal FoundationParkMark? RootParkMark { get; init; }
    internal long? RootParkWindow { get; init; }
    internal FoundationRootParkFacts? RootParkFacts { get; init; }
    // A drain control of an open recover (protocol 23): the release generation of the resolution it pages.
    internal long? RootParkDrain { get; init; }
}

internal sealed record FoundationTerminalStamp(long Revision, long At);

internal sealed record FoundationParkMark(long Revision, long At);

// Arrived counts the rows this commit restores: the whole set, or under protocol 23 the next page.
internal sealed record FoundationRootParkFacts(FoundationParkMark? Mark, int Arrived, int Consumed, bool FitsPage, string KeptDigest)
{
    internal FoundationParkDrainState? Drain { get; init; }

    // A recover that fits one page journals its outcome; a larger set journals that its drain opened.
    internal string OpenJournal(string operation) => FitsPage
        ? Json.Write(new { Outcome = "recovered", Operation = operation, Restored = Arrived, Cleared = Consumed, AlreadyRearmed = 0 })
        : Json.Write(new { Outcome = "recovering", Operation = operation, Restored = Arrived, Cleared = Consumed });

    // The drain's Complete journals the totals, as the in-memory Complete does.
    internal string CompleteJournal(string operation) =>
        Json.Write(new { Outcome = "recovered", Operation = operation, Restored = Drain!.Restored, Cleared = Drain.Cleared, AlreadyRearmed = 0 });
}

// An open drain: its release generation, the cursor after the last restored row and the running totals.
internal sealed record FoundationParkDrainState(long Generation, long Seq, string Id, long Restored, long Cleared);

/// <summary>A message. Times are database time in milliseconds.</summary>
public sealed record Message(
    string Id, InstanceRef Target, string Kind, string? Payload = null, string? Sender = null,
    StartSpec? Start = null, long? Due = null, long? KeepUntil = null, string? ReplyTo = null, long? TargetCreation = null, long SenderCreation = 0)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long InboxScope { get; private init; }
    // Qualified identity returned with a generated creator by its PostgreSQL poll. Never accepted from external ingress.
    internal (string Actor, string Tenant, string Reference)? FoundationRootBinding { get; init; }
    internal bool BindFoundationChild { get; init; }
    internal bool StoreStartInputOnce { get; init; }
    internal Message? StoredEnvelope { get; init; }
    internal EventMatch? ProjectedEvent { get; init; }
    internal string? EventStartAmbientKey { get; init; }
    internal FoundationOperatorCommand? Operator { get; init; }
    internal ParkCatchUpTicket? ParkCatchUp { get; init; }
    internal FoundationPublishWork? FoundationPublish { get; init; }
    internal bool HasFencedEventStep => ParkCatchUp is not null || FoundationPublish is not null;
    internal bool IsControl => Operator is not null || Kinds.IsControl(Kind) || Kind is Kinds.Drain or Kinds.ParkExpiry or Kinds.Cascade;
    internal bool ActorAuthored { get; init; }
    internal bool CascadeOrigin { get; private init; }
    internal bool FoundationJobResult { get; init; }
    internal long JobGeneration { get; init; }
    internal bool FoundationHeldJobResult { get; init; }
    private WeakReference<Message>? _issuedTicket;
    internal bool HasIssuedTicket => _issuedTicket?.TryGetTarget(out var issued) == true && ReferenceEquals(issued, this);
    internal Message WithoutTicketAuthority() => this with { _issuedTicket = null };

    internal Message AsKernelTicket()
    {
        // Authority belongs to the final emitted envelope. A record copy cannot carry it to another message.
        _issuedTicket = new(this);
        return this;
    }
    internal Message AsCascadeTerminate() => (this with { CascadeOrigin = true }).AsKernelTicket();
    internal Message WithoutCascadeOrigin() => CascadeOrigin ? this with { CascadeOrigin = false } : this;
    internal long DeliveryOrder(long seq) => FoundationHeldJobResult ? -seq : seq;
    internal bool ActorControl => ActorAuthored && Kind.StartsWith("kernel.", StringComparison.Ordinal);
    [System.Text.Json.Serialization.JsonIgnore]
    public string? EventMatch { get; private init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public InboxKey InboxKey => new(StorageId ?? Id, InboxScope, EventMatch);
    internal string? StorageId { get; init; }
    public Message WithInboxScope(long scope, string? match = null) => this with { InboxScope = scope, EventMatch = match };
    public Message AsIssued() => WithInboxScope(InboxKey.Issued(Id).Scope);
    public Message AsOrdinary() => InboxScope == 0 && EventMatch is null && !CascadeOrigin ? this :
        this with { InboxScope = 0, EventMatch = null, CascadeOrigin = false };
    // Creation provenance is assigned by the accepted event insert or StartChild. Outside entry points
    // discard their supplied scope. Ordinary application sends can address an already running child.
    internal bool SuppliedCreator => Start is not null &&
        (Target.ChildAddress && InboxScope != InboxKey.ChildCreatorScope ||
         Target.EventStartAddress && (EventMatch is null || InboxScope > -2));
    public T Read<T>() => Json.Read<T>(Payload!);
}

public sealed record JobFinished([property: System.Text.Json.Serialization.JsonConverter(typeof(DurableKeyJson))] string Key, string? Result, string? Error);

public static class Kinds
{
    internal const string ChildFinished = "child-finished";
    public const string Start = "start";
    public const string JobFinished = "job-finished";
    public const string Event = "event";
    internal const string JobDeadline = "kernel.job-deadline";
    internal const string Drain = "kernel.drain";
    internal const string Cascade = "kernel.cascade";
    internal const string ParkExpiry = "kernel.park-expiry";
    public const string Job = "kernel.job";    // a job's ticket: the intent while the work is in flight, the result afterwards
    public const string Meet = "kernel.meet";  // "read the events for the keys this commit subscribed"

    // What anyone may tell the kernel to do with an instance. The actor type handles them alone, in order; no model sees
    // them. Delivery treats them apart (they pass a withheld actor, go alone, are never suspects), so they are named here.
    public const string Suspend = "kernel.suspend";     // deliver nothing but kernel messages until a resume
    public const string Resume = "kernel.resume";
    public const string Terminate = "kernel.terminate";   // end it, its jobs and its waits, and every instance it started
    public const string Delete = "kernel.delete";         // remove an ended instance: its row, its waits, its journal rows
    internal const string Recover = "kernel.recover";     // resolve a Park; not a control, so a parked actor's turn order applies

    public static void RequireApplicationSend(string kind)
    {
        if (IsPrivateKernelKind(kind))
            throw new ArgumentException("The foundation reserves this message kind.", nameof(kind));
    }

    internal static bool IsPrivateKernelKind(string kind) => kind.StartsWith("kernel.", StringComparison.Ordinal) &&
        !IsControl(kind) && kind is not ("kernel.recover" or "kernel.discard" or "kernel.migrate");

    internal const string UnissuedPrivateKind = "The outcome includes a private kernel kind with no issuer.";
    internal static bool IsUnissuedPrivate(Message message) => IsPrivateKernelKind(message.Kind) && !message.HasIssuedTicket;

    public static bool IsControl(string kind) => kind is Suspend or Resume or Terminate or Delete;
    public const string Publish = "kernel.publish";    // NATIVE N1: "publish this event now that the commit has landed"
}

/// <summary>A background job: it may execute more than once; its result changes state once.</summary>
public interface IJob
{
    InstanceRef Instance { get; }
    long Creation => 0;
    string? AttemptId => null;
    int AttemptNumber => 1;
    long? Deadline => null;
    string Key { get; }
    string? Arguments { get; }
}

public static class Json
{
    public static string Write(object? value) => JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object));
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json)!;
    public static object Read(string json, Type type) => JsonSerializer.Deserialize(json, type)!;
}

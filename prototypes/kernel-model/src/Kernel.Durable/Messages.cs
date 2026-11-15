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
    CreationStart? Creation = null, long ParentCreation = 0, TimeSpan? ShortWait = null);

public sealed record SendOptions(TimeSpan? Delay = null, StartSpec? Start = null, long? TargetCreation = null);

/// <summary>The storage key scopes an opaque wire ID without modifying it. Positive scopes identify
/// tickets by the exact split of the logical actor!key string, including when actor or key contains '!'.</summary>
public readonly record struct InboxKey(string Id, long Scope = 0, string? Match = null)
{
    internal const long ChildCreatorScope = long.MaxValue;
    public bool HasIssuedOrigin => Scope == -1 || Scope > 0;
    public static InboxKey Issued(string id) => new(id, -1);
    public static InboxKey Custom(string actor, string id) => new(id, (long)int.MaxValue + 2 + actor.Length);
    public static InboxKey Ticket(string actor, string key, long creation = 0) => new(Worker.TicketId(actor, key, creation), (long)CreationProtocol.Origin(actor, creation).Length + 1);
    public static implicit operator InboxKey(string ordinaryId) => new(ordinaryId);
    public override string ToString() => Id;
}

/// <summary>A message. Times are database time in milliseconds.</summary>
public sealed record Message(
    string Id, InstanceRef Target, string Kind, string? Payload = null, string? Sender = null,
    StartSpec? Start = null, long? Due = null, long? KeepUntil = null, string? ReplyTo = null, long? TargetCreation = null, long SenderCreation = 0)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long InboxScope { get; private init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string? EventMatch { get; private init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public InboxKey InboxKey => new(Id, InboxScope, EventMatch);
    public Message WithInboxScope(long scope, string? match = null) => this with { InboxScope = scope, EventMatch = match };
    public Message AsIssued() => WithInboxScope(InboxKey.Issued(Id).Scope);
    public Message AsOrdinary() => InboxScope == 0 && EventMatch is null ? this : WithInboxScope(0);
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
    public const string Start = "start";
    public const string JobFinished = "job-finished";
    public const string Event = "event";
    public const string Job = "kernel.job";    // a job's ticket: the intent while the work is in flight, the result afterwards
    public const string Meet = "kernel.meet";  // "read the events for the keys this commit subscribed"

    // What anyone may tell the kernel to do with an instance. The actor type handles them alone, in order; no model sees
    // them. Delivery treats them apart (they pass a withheld actor, go alone, are never suspects), so they are named here.
    public const string Suspend = "kernel.suspend";     // deliver nothing but kernel messages until a resume
    public const string Resume = "kernel.resume";
    public const string Terminate = "kernel.terminate";   // end it, its jobs and its waits, and every instance it started
    public const string Delete = "kernel.delete";         // remove an ended instance: its row, its waits, its journal rows

    public static void RequireApplicationSend(string kind)
    {
        if (kind == Job) throw new ArgumentException("A kernel job ticket must be created by DurableTurn.StartJob.", nameof(kind));
        if (kind is Meet or Publish) throw new ArgumentException("Event tickets must be created by DurableTurn.Subscribe or Publish.", nameof(kind));
    }

    public static bool IsControl(string kind) => kind is Suspend or Resume or Terminate or Delete;
    public const string Publish = "kernel.publish";    // NATIVE N1: "publish this event now that the commit has landed"
}

/// <summary>A background job: it may execute more than once; its result changes state once.</summary>
public interface IJob
{
    InstanceRef Instance { get; }
    long Creation => 0;
    string Key { get; }
    string? Arguments { get; }
}

public static class Json
{
    public static string Write(object? value) => JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object));
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json)!;
    public static object Read(string json, Type type) => JsonSerializer.Deserialize(json, type)!;
}

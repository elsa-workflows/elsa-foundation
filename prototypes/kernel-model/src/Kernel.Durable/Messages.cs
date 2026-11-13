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
public sealed record StartSpec(string Model, string Definition, string? Input = null, string? Parent = null, bool Ephemeral = false, History History = History.Full);

public sealed record SendOptions(TimeSpan? Delay = null, StartSpec? Start = null);

/// <summary>A message. Times are database time in milliseconds.</summary>
public sealed record Message(
    string Id, InstanceRef Target, string Kind, string? Payload = null, string? Sender = null,
    StartSpec? Start = null, long? Due = null, long? KeepUntil = null, string? ReplyTo = null)
{
    public T Read<T>() => Json.Read<T>(Payload!);
}

public sealed record JobFinished(string Key, string? Result, string? Error);

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

    public static bool IsControl(string kind) => kind is Suspend or Resume or Terminate or Delete;
    public const string Publish = "kernel.publish";    // NATIVE N1: "publish this event now that the commit has landed"
}

/// <summary>A background job: it may execute more than once; its result changes state once.</summary>
public interface IJob
{
    InstanceRef Instance { get; }
    string Key { get; }
    string? Arguments { get; }
}

public static class Json
{
    public static string Write(object? value) => JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object));
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json)!;
    public static object Read(string json, Type type) => JsonSerializer.Deserialize(json, type)!;
}

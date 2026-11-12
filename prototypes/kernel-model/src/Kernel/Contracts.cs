using System.Globalization;
using System.Text.Json;

namespace Kernel;

/// <summary>An instance's address. The id embeds the partition: "3/order-17".</summary>
public readonly record struct InstanceRef(string Id)
{
    public int Partition => int.Parse(Id.AsSpan(0, Id.IndexOf('/')), CultureInfo.InvariantCulture);
    public static InstanceRef In(int partition, string name) => new($"{partition}/{name}");
    public override string ToString() => Id;
}

public enum InstanceStatus { Running, Finished, Faulted, Cancelled }

public enum Placement { WithParent }

/// <summary>What a message needs to create its target when the target does not exist.</summary>
public sealed record StartSpec(string Model, string Definition, string? Input = null, string? Parent = null, bool Ephemeral = false);

public sealed record SendOptions(TimeSpan? Delay = null, StartSpec? Start = null);

/// <summary>A message. Times are database time in milliseconds.</summary>
public sealed record Message(
    string Id, InstanceRef Target, string Kind, string? Payload = null, string? Sender = null,
    StartSpec? Start = null, long? Due = null, long? KeepUntil = null)
{
    public T Read<T>() => Json.Read<T>(Payload!);
}

public sealed record InstanceInfo(InstanceRef Ref, string Model, string Definition, string? Parent);

public sealed record ChildFinished(string Child, InstanceStatus Status, string? Output);

public sealed record RunFinished(string Key, string? Result, string? Error);

public static class Kinds
{
    public const string Start = "start";
    public const string ChildFinished = "child-finished";
    public const string RunFinished = "run-finished";
    public const string Event = "event";
    public const string Run = "kernel.run";    // a run's ticket: the intent while the work is in flight, the result afterwards
    public const string Meet = "kernel.meet";  // "read the events for the keys this commit subscribed"
}

public interface IExecutionModel
{
    string Key { get; }
    Type StateType { get; }
    object CreateState(StartSpec start);
    ValueTask StepAsync(IStep step, CancellationToken cancellationToken);
    ValueTask<object?> RunAsync(IRun run, CancellationToken cancellationToken);
}

/// <summary>Off-step work: it may execute more than once; its result changes state once.</summary>
public interface IRun
{
    InstanceRef Instance { get; }
    string Key { get; }
    string? Arguments { get; }
}

public interface IStep
{
    InstanceInfo Instance { get; }
    IReadOnlyList<Message> Inbox { get; }
    object State { get; set; }
    void Send(InstanceRef target, string kind, object? payload = null, SendOptions? options = null);
    InstanceRef Start(StartSpec spec, Placement placement = Placement.WithParent);
    void Run(string key, object? arguments = null);
    void CancelRun(string key);
    void Subscribe(string key, string wait);
    void Unsubscribe(string key, string wait);
    void Record(object entry);
    void SetStatus(InstanceStatus status, string? subStatus = null, object? output = null);
}

public static class Json
{
    public static string Write(object? value) => JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object));
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json)!;
    public static object Read(string json, Type type) => JsonSerializer.Deserialize(json, type)!;
}

/// <summary>
/// Deliberate defects. Each switches off one rule of the protocol so that the simulation can show it notices.
/// The first group are mutations of the current design; the second are behaviours of an earlier draft or of a
/// literal reading of the sketch, kept so that the seed that exposed each one stays replayable (see FINDINGS.md).
/// </summary>
[Flags]
public enum Flaw
{
    None = 0,
    FenceWithoutEpoch = 1 << 0,
    FenceWithoutPosition = 1 << 1,
    CommitDespiteFence = 1 << 2,
    NoResetAfterFailedCommit = 1 << 3,
    DeliverBeforeCommit = 1 << 4,
    PollWithoutSkip = 1 << 5,
    KeepConsumedMessages = 1 << 6,
    DeleteWithoutCount = 1 << 7,

    WallClockTimers = 1 << 10,
    DeleteOutsideCommands = 1 << 11,
    RunBeforeMark = 1 << 12,
    VolatileRunTicket = 1 << 13,
    MeetWithoutTicket = 1 << 14,
    PollRace = 1 << 15,
    HandOverWhileBehind = 1 << 16,
    BumpAllInherited = 1 << 17,
    EveryTakeCounts = 1 << 18,
    CorrelationForgets = 1 << 20,   // read by the simulation: the correlation model does not keep events for later waits
    StableNodeName = 1 << 19,      // read by the simulation: a restarted process keeps its predecessor's name
}

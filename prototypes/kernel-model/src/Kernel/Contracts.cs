using System.Globalization;
using System.Text.Json;

namespace Kernel;

/// <summary>An instance's address. The id embeds the partition: "3/order-17".</summary>
public readonly record struct InstanceRef(string Id)
{
    public int Partition => int.Parse(Id.AsSpan(0, Id.IndexOf('/')), CultureInfo.InvariantCulture);
    public static InstanceRef In(int partition, string name) => new($"{partition}/{name}");

    /// <summary>A partition for a name, the same on every node.</summary>
    public static int Spread(string name, int partitions)
    {
        var hash = 2166136261;
        foreach (var c in name) hash = (hash ^ c) * 16777619;
        return (int)(hash % (uint)partitions);
    }
    public override string ToString() => Id;
}

public enum InstanceStatus { Running, Finished, Faulted, Cancelled }

public enum Placement { WithParent, Spread }

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

public sealed record InstanceInfo(InstanceRef Ref, string Model, string Definition, string? Parent);

public sealed record ChildFinished(string Child, InstanceStatus Status, string? Output);

public sealed record JobFinished(string Key, string? Result, string? Error);

public static class Kinds
{
    public const string Start = "start";
    public const string ChildFinished = "child-finished";
    public const string JobFinished = "job-finished";
    public const string Event = "event";
    public const string Job = "kernel.job";    // a job's ticket: the intent while the work is in flight, the result afterwards
    public const string Meet = "kernel.meet";  // "read the events for the keys this commit subscribed"

    // What anyone may tell the kernel to do with an instance. The kernel handles them alone, in order; no model sees them.
    public const string Suspend = "kernel.suspend";     // deliver nothing but kernel messages until a resume
    public const string Resume = "kernel.resume";
    public const string Terminate = "kernel.terminate";   // end it, its jobs and its waits, and every instance it started
    public const string Delete = "kernel.delete";         // remove an ended instance: its row, its waits, its journal rows

    public static bool IsControl(string kind) => kind is Suspend or Resume or Terminate or Delete;
    public const string Publish = "kernel.publish";    // NATIVE N1: "publish this event now that the commit has landed"
}

public interface IExecutionModel
{
    string Key { get; }
    Type StateType { get; }
    int StateVersion { get; }
    object CreateState(StartSpec start);

    /// <summary>
    /// A row written at an older version, in its stored form: the kernel cannot read it as a type it no longer knows.
    /// Called on load; the next commit writes it at <see cref="StateVersion"/>.
    /// </summary>
    object UpgradeState(string state, int fromVersion);
    ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken);
    ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken);
}

/// <summary>A background job: it may execute more than once; its result changes state once.</summary>
public interface IJob
{
    InstanceRef Instance { get; }
    string Key { get; }
    string? Arguments { get; }
}

public interface ITurn
{
    InstanceInfo Instance { get; }
    IReadOnlyList<Message> Inbox { get; }
    object State { get; set; }
    object? Ambient { get; }                    // what the starter on this node gave: this activation only, never stored

    /// <summary>
    /// Live output to whoever waits. Released when the turn's commit lands, or at once when it has nothing to write
    /// (the owner's default of 12 November). A caller whose route opts out (<see cref="ReplyTo.Of"/>) gets it when the
    /// turn ends instead, so it may see output of a turn whose commit is then lost.
    /// </summary>
    void Emit(string channel, object? item);

    /// <summary>Returns the message's id, derived from this instance and its revision: what <see cref="Recall"/> takes.</summary>
    string Send(InstanceRef target, string kind, object? payload = null, SendOptions? options = null);

    /// <summary>
    /// Takes back a message this instance sent to itself and has not been given yet: a timer, a retry (merge, friction
    /// F-N4). Its row is deleted with this turn's commit, as a cancelled job's ticket is. The model vouches that it has
    /// not received it; one that is in this turn's inbox is consumed anyway, and the call does nothing.
    /// </summary>
    void Recall(string message);

    /// <summary>
    /// A child by default: it reports back, and a terminate goes on to it. <paramref name="detached"/> starts an instance
    /// that has no parent (merge, friction F-N5): it reports to nobody, and since its id is minted here it needs no read.
    /// </summary>
    InstanceRef Start(StartSpec spec, Placement placement = Placement.WithParent, bool detached = false);
    void StartJob(string key, object? arguments = null);
    void CancelJob(string key);
    void Hold(Message message);                 // not consumed: handed to the next turn again, in its place in the order

    /// <summary>
    /// A wait for events of <paramref name="key"/> published from its commit on. <paramref name="keep"/> also gives it
    /// the events still kept from before (the owner's default of 12 November: off; BPMN-style correlation turns it on):
    /// only then does the commit carry the "read the events" ticket, which costs a read and a commit (friction F-N8).
    /// </summary>
    void Subscribe(string key, string wait, bool keep = false);
    void Unsubscribe(string key, string wait);

    /// <summary>
    /// True when this message was held by an owner before this one, so a turn that took it may have run already and
    /// its result been lost (merge, friction F-N6). A first delivery is false. It may say true for a message whose
    /// earlier owner never ran it: "may have run", never "did run".
    /// </summary>
    bool Redelivered(Message message);

    /// <summary>
    /// NATIVE N1: publish an event from a turn. The commit carries a ticket to self; after it lands the owner inserts the
    /// event and delivers it to the subscriptions it finds, as <see cref="Events.PublishAsync"/> does for an outsider.
    /// Returns the event's id, derived from the instance and its revision. <paramref name="keep"/> is how long the event
    /// stays for waits that ask for kept events; the waits that exist when it is published get it either way.
    /// </summary>
    string Publish(string key, string? payload, TimeSpan keep);

    void Record(object entry);
    void Rebind(string definition);             // from the next turn on, the instance belongs to another definition (version)
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
public enum Flaw : long
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
    VolatileJobTicket = 1 << 13,
    MeetWithoutTicket = 1 << 14,
    PollRace = 1 << 15,
    HandOverWhileBehind = 1 << 16,
    BumpAllInherited = 1 << 17,
    EveryTakeCounts = 1 << 18,
    CorrelationForgets = 1 << 20,   // read by the simulation: the correlation model does not keep events for later waits
    StableNodeName = 1 << 19,      // read by the simulation: a restarted process keeps its predecessor's name

    // Round 3: each piece's rule, switched off.
    GreedyBalancer = 1 << 21,      // a node takes every free partition and never releases a surplus
    TrialPerPartition = 1 << 22,   // a trial is alone in its partition only, not in its process
    ProjectorBlindWrite = 1 << 23, // the projector's write does not check that its position is still where it read it
    EmitBeforeCommit = 1 << 24,    // what a turn emits is released when the turn ends, before its commit lands
    ReplyTwice = 1 << 25,          // a request takes a second answer that arrives after its first
    NoDeadline = 1 << 26,          // a request waits until something answers it
    DeliverWhileSuspended = 1 << 27, // a suspended instance is given its messages
    NoShelf = 1 << 28,             // a suspect that cannot be tried keeps its row unseen
    SuspendDuringTrial = 1 << 29,  // an instance may be suspended while it has a message on trial
    TerminateRootOnly = 1 << 30,   // a terminate ends its instance and not the instances it started
    HoldLoses = 1L << 31,          // a held message is consumed like the rest
    HoldKeepsTrial = 1L << 32,     // a held message that was on trial stays on trial in its row
    SkipUpgrade = 1L << 33,        // a row at an older state version is read as the current one
    TurnNewerState = 1L << 34,     // a model turns a row that a newer version of it wrote
    RebindForgotten = 1L << 35,    // a rebind is not written to the row
    StartWithoutRead = 1L << 36,   // read by the simulation: a registrar of a start subscription does not read the events kept
    CancelKnownChildren = 1L << 37, // read by the simulation: a parent cancels only the children that have reported back
    EvictBusy = 1L << 38,          // an instance whose commit is in flight may be evicted
    DeleteLeavesJournal = 1L << 39, // a delete leaves the instance's journal rows
    ProjectorStopsAtGap = 1L << 40, // the projector stops at the first missing position, as in section 10
    TrialBeforeLoad = 1L << 41,    // for the measurement of section 21: a trial may start before its instance is loaded and idle
}

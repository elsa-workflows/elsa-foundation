using System.Globalization;

namespace Kernel.Runtime;

/// <summary>
/// An actor's address. The id embeds the partition, its scope under the built-in runtime: "3/order-17". The name keeps
/// the kernel's word for the actors it has so far, workflow instances.
/// </summary>
public readonly record struct InstanceRef(string Id)
{
    public int Partition => int.Parse(Id.AsSpan(0, Id.IndexOf('/')), CultureInfo.InvariantCulture);
    public static InstanceRef In(int partition, string name) => new($"{partition}/{name}");

    /// <summary>A partition for a name, the same on every node: placement, the runtime's business.</summary>
    public static int Spread(string name, int partitions)
    {
        var hash = 2166136261;
        foreach (var c in name) hash = (hash ^ c) * 16777619;
        return (int)(hash % (uint)partitions);
    }
    public override string ToString() => Id;
}

/// <summary>
/// Deliberate defects. Each switches off one rule of the protocol so that the simulation can show it notices.
/// The first group are mutations of the current design; the second are behaviours of an earlier draft or of a
/// literal reading of the sketch, kept so that the seed that exposed each one stays replayable (see FINDINGS.md).
/// One enum for the three layers, at the lowest of them, so that every flaw keeps its bit and every seed its meaning.
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
    HideDeliveryHistory = 1L << 43,  // mutation: suppress the delivery-history hint
    IgnoreLeaseDeadline = 1L << 42, // mutation: start new work after the local safe deadline
    TrialBeforeLoad = 1L << 41,    // for the measurement of section 21: a trial may start before its instance is loaded and idle
}

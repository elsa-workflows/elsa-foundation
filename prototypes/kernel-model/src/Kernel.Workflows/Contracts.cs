using Kernel.Durable;
using Kernel.Runtime;

namespace Kernel.Workflows;

public enum Placement { WithParent, Spread }

public sealed record InstanceInfo(InstanceRef Ref, string Model, string Definition, string? Parent);

/// <summary>What a parent is told when a child ends.</summary>
public sealed record ChildFinished(string Child, InstanceStatus Status, string? Output);

/// <summary>The message kinds of the workflow instance; the durable layer's own are in <see cref="Kinds"/>.</summary>
public static class WorkflowKinds
{
    public const string ChildFinished = "child-finished";
}

/// <summary>
/// An execution model: native, BPMN, ETL, agents, the simulation's toy models. A workflow instance
/// (<see cref="WorkflowInstance"/>) runs one for everything the instance does not own itself.
/// </summary>
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

public sealed record ModelMetadataUnavailable(string Actor, string Model, int StoredVersion, int AvailableVersion);

/// <summary>Optional model-owned metadata; the workflow actor keeps command and commit authority.</summary>
public interface IModelMetadata
{
    bool HandlesMetadata(Message message);
    ValueTask MetadataAsync(ITurn turn, InstanceStatus status, CancellationToken cancellationToken);
    void TerminateMetadata(object state);
    bool ProtectsDeletion(object state, long now, ICommitClock clock);
}

public interface ITurn
{
    InstanceInfo Instance { get; }
    long Now { get; } // the existing database-anchored owner clock
    ICommitClock Clock { get; }
    IReadOnlyList<Message> Inbox { get; }
    object State { get; set; }
    object? Ambient { get; }                    // what the starter on this node gave: this activation only, never stored

    /// <summary>
    /// Live output to whoever waits. Released when the turn's commit lands, or at once when it has nothing to write
    /// (the owner's default of 12 November). A caller whose route opts out (<see cref="ReplyTo.Of"/>) gets it when the
    /// turn ends instead, so it may see output of a turn whose commit is then lost.
    /// </summary>
    void Emit(string channel, object? item);
    /// <summary>Reply only to this turn's consumed request, after commit, without observing future workflow output.</summary>
    void Reply(Message request, string channel, object? item);

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
    /// True when a polled row was already marked seen or on trial when observed. This is a delivery-history hint,
    /// not proof that a turn executed. False does not prove no other owner ran it, including during takeover races.
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

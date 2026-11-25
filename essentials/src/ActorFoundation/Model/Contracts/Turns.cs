using Kernel.Foundation.Contracts;

namespace Kernel.Model.Contracts;

/// <summary>A workflow model's view of a foundation turn, with model-owned in-memory state.</summary>
public interface ITurn
{
    InstanceInfo Instance { get; }

    /// <summary>The foundation's clock in Unix milliseconds, fixed for the turn.</summary>
    long Now { get; }
    IReadOnlyList<Message> Inbox { get; }
    object State { get; set; }
    object? Ambient { get; }

    void Emit(string channel, object? item);
    void Reply(Message message, string channel, object? item);
    string Send(ActorRef target, string kind, object? payload = null, long? dueAt = null);
    void Recall(string id);
    ActorRef Start(WorkflowStart spec, Placement placement = Placement.WithParent, bool detached = false);
    void StartJob(string key, object? arguments = null, TimeSpan? timeout = null);
    void CancelJob(string key);
    void Hold(Message message);
    void Subscribe(string key, string wait, bool keep = false);
    void Unsubscribe(string key, string wait);
    string Publish(string key, string? payload, TimeSpan keepFor);
    void Record(object entry);
    void Rebind(string definition);

    /// <summary>
    /// Sets one of the instance's own labels, or removes it with null: metadata reads filter on labels (ActorQuery.Labels),
    /// such as a correlation id or a display name. A model may hold two; the actor hosting it keeps the header's other labels
    /// for its own names. A name and a value are at most 1,024 UTF-8 bytes. A host that projects no model labels refuses.
    /// </summary>
    void SetLabel(string name, string? value) => throw new NotSupportedException("This host sets no model labels.");
    void SetStatus(ActorStatus status, string? subStatus = null, object? output = null);
}

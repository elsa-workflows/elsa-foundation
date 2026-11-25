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
    /// such as a correlation id or a display name. The host holds them within its budget and refuses one past it, one of
    /// its own names, or a name or value longer than the foundation allows (ArgumentException).
    /// </summary>
    void SetLabel(string name, string? value);
    void SetStatus(ActorStatus status, string? subStatus = null, object? output = null);
}

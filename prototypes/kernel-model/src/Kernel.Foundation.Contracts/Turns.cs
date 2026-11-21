namespace Kernel.Foundation.Contracts;

/// <summary>A background attempt. The deadline uses the foundation clock.</summary>
public interface IJob
{
    ActorRef Actor { get; }
    string Key { get; }
    string? Arguments { get; }
    string AttemptId { get; }
    int AttemptNumber { get; }
    long? Deadline { get; }
}

/// <summary>The durable actor's code. The foundation owns admission, delivery, persistence and recovery.</summary>
public interface IActorType
{
    string Type { get; }
    bool Withholds(ActorHeader header);
    ValueTask TurnAsync(IActorTurn turn, CancellationToken cancellationToken);
    ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken);
    RestorePlan RestoreWaits(ActorHeader header, string state);

    /// <summary>An upper bound on final-state growth, in bytes of the serialized state.</summary>
    long TerminalGrowth(ActorHeader header, string state);

    bool ProtectsDeletion(ActorHeader header, string state, long now);
}

/// <summary>The state, inbox, and effects available to one invocation of the actor type.</summary>
public interface IActorTurn
{
    ActorHeader Header { get; }

    /// <summary>The foundation's reading in Unix milliseconds, fixed for the turn.</summary>
    long Now { get; }
    IReadOnlyList<Message> Inbox { get; }
    string? State { get; set; }
    object? Ambient { get; }

    void Emit(string channel, object? item);
    void Reply(Message message, string channel, object? item);
    string Send(ActorRef target, string kind, string? payload = null, long? dueAt = null);
    void Recall(string id);
    ActorRef Start(StartSpec spec, Placement placement = Placement.WithParent, bool detached = false);
    void StartJob(string key, string? arguments = null, TimeSpan? timeout = null);
    void CancelJob(string key);
    void Hold(Message message);
    void Subscribe(string key, string wait, bool keep = false);
    void Unsubscribe(string key, string wait);
    string Publish(string key, string? payload, TimeSpan keepFor);
    void Record(object entry);
    void End(ActorStatus status, object? output = null);
    void SetHeader(string typeData, IReadOnlyDictionary<string, string> labels);
}

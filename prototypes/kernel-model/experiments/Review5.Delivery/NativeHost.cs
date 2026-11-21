namespace Kernel.Review5;

/// <summary>
/// One model turn of the real <see cref="NativeModel"/>, hosted outside the Worker (copied from Review4.Blockers, with
/// job starts recorded). The host gives the model its inbox and state and records what the model asks for: sends,
/// recalls, holds, job starts, records and its end status. The harness then applies those effects to its own durable
/// rows as the commit would. A change to <see cref="ITurn"/> breaks the build.
/// </summary>
sealed class NativeHost(InstanceInfo instance, long now, long revision) : ITurn
{
    public InstanceInfo Instance { get; } = instance;
    public long Now { get; } = now;
    public ICommitClock Clock => null!; // the workflows in this harness never read the commit clock
    public IReadOnlyList<Message> Inbox { get; init; } = [];
    public object State { get; set; } = new NativeState();
    public object? Ambient => null;

    public readonly List<Message> Sent = [];
    public readonly List<string> Recalled = [];
    public readonly List<Message> HeldMessages = [];
    public readonly List<string> Records = [];
    public readonly List<(string Key, object? Arguments)> Jobs = [];
    public InstanceStatus? EndStatus;

    public void Emit(string channel, object? item) => Records.Add($"emit:{channel}");
    public void Reply(Message request, string channel, object? item) => Records.Add($"reply:{request.Id}");

    public string Send(InstanceRef target, string kind, object? payload = null, SendOptions? options = null)
    {
        // An issued id derived from this instance, its creation and the turn's revision, as DurableTurn.Send derives it.
        var id = FormattableString.Invariant($"{CreationProtocol.Origin(Instance.Ref.Id, Instance.Creation)}#{revision}.{Sent.Count:D4}");
        var self = target.Id == Instance.Ref.Id;
        var due = options?.Delay is { } delay ? Now + (long)delay.TotalMilliseconds : (long?)null;
        Sent.Add(new Message(id, target, kind, payload is null ? null : Json.Write(payload), Instance.Ref.Id, Due: due,
            TargetCreation: options?.TargetCreation ?? (self ? Instance.Creation : null), SenderCreation: Instance.Creation).AsIssued());
        return id;
    }

    public void Recall(string message) => Recalled.Add(message);
    public void Hold(Message message) => HeldMessages.Add(message);
    public void Subscribe(string key, string wait, bool keep = false) => Records.Add($"subscribe:{key}:{wait}:{keep}");
    public void Unsubscribe(string key, string wait) => Records.Add($"unsubscribe:{key}:{wait}");
    public bool Redelivered(Message message) => false;
    public void Record(object entry) => Records.Add(entry.ToString()!);
    public void SetStatus(InstanceStatus status, string? subStatus = null, object? output = null) => EndStatus = status;
    /// <summary>DurableTurn.StartJob writes the ticket (Turns.cs 394–403); here the harness writes it from what is recorded.</summary>
    public void StartJob(string key, object? arguments = null) => Jobs.Add((key, arguments));
    public void CancelJob(string key) => Records.Add($"cancel-job:{key}");

    public InstanceRef Start(StartSpec spec, Placement placement = Placement.WithParent, bool detached = false) => throw Unused(nameof(Start));
    public CreationAddress ChildAddress(InstanceRef child) => throw Unused(nameof(ChildAddress));
    public string Publish(string key, string? payload, TimeSpan keep) => throw Unused(nameof(Publish));
    public void Rebind(string definition) => throw Unused(nameof(Rebind));

    private static NotSupportedException Unused(string call) => new($"the harness workflows never call ITurn.{call}");
}

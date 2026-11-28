namespace Kernel.Sim;

// Correlation as an ordinary instance per key: a model on the kernel, with no table, no kernel message and no rule of
// its own. Subscribing and publishing are messages to it. It holds the waits and the events it keeps, and forwards.

public sealed record Wait(string Instance, string Name, StartSpec? Start = null);

public sealed record Kept(string Id, string? Payload);

public sealed record Publish(string Id, string? Payload, int KeepMs);

public sealed class CorrelationState
{
    public List<Wait> Waits { get; set; } = [];
    public List<Kept> Events { get; set; } = [];
}

public sealed class Correlation(bool forgets = false) : Model<CorrelationState>
{
    public static readonly StartSpec Spec = new("correlation", "key");
    public override string Key => "correlation";

    /// <summary>The instance of a key: the key hashes to a partition, so every node finds it without asking.</summary>
    public static InstanceRef Of(string key, int partitions)
    {
        var hash = 2166136261;
        foreach (var c in key) hash = (hash ^ c) * 16777619;
        return InstanceRef.In((int)(hash % (uint)partitions), "key:" + key);
    }

    public static Message Published(string messageId, string key, int partitions, Publish publish, long keepUntil) =>
        new(messageId, Of(key, partitions), "publish", Json.Write(publish), Start: Spec, KeepUntil: keepUntil);

    protected override void Turn(ITurn turn, CorrelationState state)
    {
        foreach (var message in turn.Inbox)
            switch (message.Kind)
            {
                case "subscribe" when message.Read<Wait>() is var wait && !state.Waits.Contains(wait):
                    state.Waits.Add(wait);
                    if (!forgets)
                        foreach (var kept in state.Events) Deliver(turn, wait, kept);    // an event that came first still meets the wait
                    break;
                case "unsubscribe":
                    state.Waits.Remove(message.Read<Wait>());
                    break;
                case "publish" when message.Read<Publish>() is var publish && !state.Events.Exists(e => e.Id == publish.Id):
                    var e = new Kept(publish.Id, publish.Payload);
                    state.Events.Add(e);
                    foreach (var wait in state.Waits) Deliver(turn, wait, e);
                    turn.Send(turn.Instance.Ref, "expire", publish.Id, new SendOptions(Delay: TimeSpan.FromMilliseconds(publish.KeepMs)));
                    break;
                case "expire":
                    state.Events.RemoveAll(e => e.Id == message.Read<string>());
                    break;
            }
    }

    // A wait may carry a start spec: then the event starts the instance it names. That is all "start on an event" is.
    private static void Deliver(ITurn turn, Wait wait, Kept e) =>
        turn.Send(new InstanceRef(wait.Instance), Kinds.Event, e.Id, wait.Start is null ? null : new SendOptions(Start: wait.Start));
}

/// <summary>The waiter again, waiting through correlation instances instead of the subscription table.</summary>
public sealed class CWaiter(int partitions) : Model<WaiterState>
{
    public override string Key => "cwaiter";
    public override object CreateState(StartSpec start) => new WaiterState { Keys = Json.Read<List<string>>(start.Input!) };

    protected override void Turn(ITurn turn, WaiterState state)
    {
        var me = new Wait(turn.Instance.Ref.Id, "w");
        foreach (var message in turn.Inbox)
        {
            if (message.Kind == Kinds.Start && !state.Started)
            {
                state.Started = true;
                foreach (var key in state.Keys) turn.Send(Correlation.Of(key, partitions), "subscribe", me, new SendOptions(Start: Correlation.Spec));
                state.Waits.AddRange(state.Keys);
            }
            else if (message.Kind == Kinds.Event && state.Waits.Count > 0)
            {
                state.Got.Add(message.Id);
                foreach (var key in state.Waits) turn.Send(Correlation.Of(key, partitions), "unsubscribe", me, new SendOptions(Start: Correlation.Spec));
                state.Waits.Clear();
                turn.SetStatus(InstanceStatus.Finished);
            }
        }
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Contract = Kernel.Foundation.Contracts;
using Models = Kernel.Model.Contracts;

namespace Kernel.Native;

internal sealed class ContractTurn(Models.ITurn turn, Action<NativeOutbound>? outbound, long? endedAt = null) : INativeTurn
{
    private HashSet<string>? _timerHandles;

    public Contract.ActorRef Actor => turn.Instance.Ref;
    public string Definition => turn.Instance.Definition;
    public bool HasExactIdentity => true;
    public bool SavesWaitRecipes => true;
    public string OperationOrigin => Actor.Id;
    public long Now => turn.Now;
    public NativeState State => (NativeState)turn.State;
    public object? Ambient => turn.Ambient;
    public IReadOnlyList<Contract.Message> Items => turn.Inbox;
    public IReceiptClock Clock { get; } = new ModelReceiptClock(turn.Now, endedAt);

    public bool TargetsCurrentLife(Contract.Message message) => true; // the foundation addressed this turn's exact ref
    public string? TimerWait(Contract.Message message)
    {
        // The foundation ties the sender to this life. Only an outstanding timer's exact handle can carry its payload.
        if (message.Sender != Actor) return null;
        _timerHandles ??= State.Waits.Values.Where(wait => wait.Kind == "timer" && wait.Key.Length > 0)
            .Select(wait => wait.Key).ToHashSet(StringComparer.Ordinal);
        if (!_timerHandles.Contains(message.Id)) return null;
        var receipt = JsonSerializer.Deserialize<TimerFired>(message.Payload!);
        return receipt is not null && State.Waits.TryGetValue(receipt.Wait, out var expected) &&
            expected.Kind == "timer" && expected.Key == message.Id ? receipt.Wait : null;
    }
    public string? EventWait(Contract.Message message) => message.Event?.Wait;
    public Contract.ChildFinished ChildFinished(Contract.Message message) => JsonSerializer.Deserialize<Contract.ChildFinished>(message.Payload!)!;
    public bool MayHaveRun() => false;
    public void Hold(Contract.Message message) => turn.Hold(message);
    public void Reply(Contract.Message message, object? reply) => turn.Reply(message, "response", reply);

    public string SendToSelf(string kind, object? payload = null, long? dueAt = null) =>
        turn.Send(Actor, kind, payload, dueAt);

    public WaitEntry StartChild(string frame, string workflow, string mode, JsonObject? input) =>
        new(frame, "child", turn.Start(NativeExecutionModel.Start(workflow, mode, input)).Id);

    public Contract.ActorRef StartDetached(string workflow, string mode, JsonObject? input) =>
        turn.Start(NativeExecutionModel.Start(workflow, mode, input), detached: true);

    public void CancelChild(WaitEntry child) => turn.Send(new Contract.ActorRef(child.Key), NativeKinds.Cancel);
    public void StartJob(string key, BackgroundArgs arguments) => turn.StartJob(key, arguments);
    public void CancelJob(string key) => turn.CancelJob(key);
    public void Subscribe(string key, string wait) => turn.Subscribe(key, wait);
    public void Unsubscribe(string key, string wait) => turn.Unsubscribe(key, wait);
    public void Recall(string id) => turn.Recall(id);
    public void Publish(string key, string? payload, TimeSpan keep) => turn.Publish(key, payload, keep);
    public void Emit(string channel, object? item) => turn.Emit(channel, item);
    public void Record(string entry) => turn.Record(entry);
    public void SetStatus(Contract.ActorStatus status, string? subStatus, JsonNode? output) => turn.SetStatus(status, subStatus, output);
    public string EffectKey(string node, string iteration, int attempt) => Models.EffectKey.For(Actor.Id, node, iteration, attempt);
    public void ExternalAttempt(string frame, string node, int attempt, bool mayHaveRun) { }

    public void SendOutbound(string frame, string node, string iteration, int attempt, string key, OutboundEffect effect) =>
        outbound?.Invoke(new(Actor, frame, node, iteration, attempt, key, effect));
}

/// <summary>Receipt times owned by the model, with the foundation's end time supplied for retention checks.</summary>
internal sealed class ModelReceiptClock(long now, long? endedAt = null) : IReceiptClock
{
    private const string Prefix = "now:";
    public string Terminal => "workflow:end";
    public void Require() { }
    public string Capture() => Prefix + now.ToString(CultureInfo.InvariantCulture);
    public void Release(string reference) { }
    public bool IsModelReference(string reference) => reference.StartsWith(Prefix, StringComparison.Ordinal);

    public long? Resolve(string reference) => reference == Terminal ? endedAt :
        IsModelReference(reference) && long.TryParse(reference.AsSpan(Prefix.Length), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) ? value : null;
}

using System.Text.Json.Nodes;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native;

internal interface INativeTurn
{
    Contract.ActorRef Actor { get; }
    string Definition { get; }
    bool HasExactIdentity { get; }
    bool SavesWaitRecipes { get; }
    string OperationOrigin { get; }
    long Now { get; }
    NativeState State { get; }
    object? Ambient { get; }
    IReadOnlyList<Contract.Message> Items { get; }
    IReceiptClock Clock { get; }
    bool IsTimer(Contract.Message message, string handle);
    bool TargetsCurrentLife(Contract.Message message);
    string? EventWait(Contract.Message message);
    Contract.ChildFinished ChildFinished(Contract.Message message);
    bool MayHaveRun();
    void Hold(Contract.Message message);
    void Reply(Contract.Message message, object? reply);
    string SendToSelf(string kind, object? payload = null, long? dueAt = null);
    WaitEntry StartChild(string frame, string workflow, string mode, JsonObject? input);
    Contract.ActorRef StartDetached(string workflow, string mode, JsonObject? input);
    void CancelChild(WaitEntry child);
    void StartJob(string key, BackgroundArgs arguments);
    void CancelJob(string key);
    void Subscribe(string key, string wait);
    void Unsubscribe(string key, string wait);
    void Recall(string id);
    void Publish(string key, string? payload, TimeSpan keep);
    void Emit(string channel, object? item);
    void Record(string entry);
    void SetStatus(Contract.ActorStatus status, string? subStatus, JsonNode? output);
    string EffectKey(string node, string iteration, int attempt);
    void ExternalAttempt(string frame, string node, int attempt, bool mayHaveRun);
    void SendOutbound(string frame, string node, string iteration, int attempt, string key, OutboundEffect effect);
}

internal interface IReceiptClock
{
    string Terminal { get; }
    void Require();
    string Capture();
    long? Resolve(string reference);
    void Release(string reference);
    bool IsModelReference(string reference);
}

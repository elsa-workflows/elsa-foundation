using Models = Kernel.Model.Contracts;
using System.Text.Json.Nodes;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native;

internal readonly record struct ChildWorkflowTarget(string Id, Workflow? Selected = null)
{
    internal Models.WorkflowStart Start(string mode, JsonObject? input) => Selected is { } workflow
        ? NativeExecutionModel.Start(workflow, mode, input)
        : NativeExecutionModel.Start(Id, mode, input);
}

internal interface INativeTurn
{
    Contract.ActorRef Actor { get; }
    string Workflow { get; }
    bool HasExactIdentity { get; }
    bool SavesWaitRecipes { get; }
    string OperationOrigin { get; }
    string? TestScopeId => null;
    long Now { get; }
    NativeState State { get; }
    object? Ambient { get; }
    IReadOnlyList<Contract.Message> Items { get; }
    IReceiptClock Clock { get; }
    string? TimerWait(Contract.Message message);
    bool TargetsCurrentLife(Contract.Message message);
    string? EventWait(Contract.Message message);
    Contract.ChildFinished ChildFinished(Contract.Message message);
    bool MayHaveRun();
    void Hold(Contract.Message message);
    void Reply(Contract.Message message, object? reply);
    string SendToSelf(string kind, object? payload = null, long? dueAt = null);
    WaitEntry StartChild(string frame, ChildWorkflowTarget workflow, string mode, JsonObject? input);
    Contract.ActorRef StartDetached(ChildWorkflowTarget workflow, string mode, JsonObject? input);
    void CancelChild(WaitEntry child);
    void StartJob(string key, BackgroundArgs arguments, TimeSpan? timeout);
    void CancelJob(string key);
    void Subscribe(string key, string wait);
    void Unsubscribe(string key, string wait);
    void Recall(string id);
    void Publish(string key, string? payload, TimeSpan keep);
    void Emit(string channel, object? item);
    void Record(string entry);
    void RecordFrameScheduled(string frame, int attempt) => Record($"a:{frame}:{attempt}");
    void RecordFrameStart(Frame frame) { }
    void RecordFrameOutcome(NativeJournalKind kind, Frame frame) => Record($"{OutcomeCode(kind)}:{frame.Id}:{frame.Node}");
    internal static char OutcomeCode(NativeJournalKind kind) => kind switch
    {
        NativeJournalKind.Completed => 'c',
        NativeJournalKind.Faulted => 'f',
        NativeJournalKind.Cancelled => 'x',
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    void SetStatus(Contract.ActorStatus status, string? subStatus, JsonNode? output);
    /// <summary>One of the instance's own labels (Set Correlation Id, Set Instance Name); null removes it.</summary>
    void SetLabel(string name, string? value);
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

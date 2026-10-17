namespace Elsa.UnifiedExecution.WorkProof;

/// <summary>Two ordered activities. The policy releases the second only after accepting the first result.</summary>
public sealed class SequencePolicy : ICompositionPolicy
{
    public string Id => "native-sequence-witness-v1";

    public CompositionPlan Start(string inputJson)
    {
        var state = new SequenceState(0, false);
        return new CompositionPlan(JsonSupport.Serialize(state),
            [new WorkDefinition("sequence-0", "snapshot-echo", "1", inputJson)], [], false);
    }

    public CompositionPlan ApplyResult(string stateJson, WorkReceipt receipt)
    {
        SequenceState state = JsonSupport.Deserialize<SequenceState>(stateJson);
        if (receipt.Status != "Succeeded") throw new InvalidOperationException("Sequence cannot advance on a rejected or non-success result.");
        if (state.Next == 0 && receipt.InvocationId == "sequence-0")
        {
            return new CompositionPlan(JsonSupport.Serialize(new SequenceState(1, false)),
                [new WorkDefinition("sequence-1", "snapshot-echo", "1", receipt.ResultJson)], [], false);
        }
        if (state.Next == 1 && receipt.InvocationId == "sequence-1")
            return new CompositionPlan(JsonSupport.Serialize(new SequenceState(2, true)), [], [], true);
        throw new InvalidOperationException($"Unexpected sequence result '{receipt.InvocationId}' at step {state.Next}.");
    }

    public CompositionPlan Continue(string stateJson, string continuationId)
        => throw new InvalidOperationException($"Sequence has no semantic continuation '{continuationId}'.");

    private sealed record SequenceState(int Next, bool Complete);
}

/// <summary>Two branches that join through a durable component continuation after both results arrive.</summary>
public sealed class FlowchartPolicy : ICompositionPolicy
{
    public string Id => "native-flowchart-witness-v1";

    public CompositionPlan Start(string inputJson)
    {
        var state = new FlowchartState([], false, false);
        return new CompositionPlan(JsonSupport.Serialize(state),
        [
            new WorkDefinition("branch-a", "snapshot-echo", "1", "{\"value\":\"A\"}"),
            new WorkDefinition("branch-b", "snapshot-echo", "1", "{\"value\":\"B\"}")
        ], [], false);
    }

    public CompositionPlan ApplyResult(string stateJson, WorkReceipt receipt)
    {
        FlowchartState state = JsonSupport.Deserialize<FlowchartState>(stateJson);
        if (receipt.Status != "Succeeded") throw new InvalidOperationException("Flowchart cannot join a rejected or non-success result.");
        if (receipt.InvocationId is not ("branch-a" or "branch-b"))
            throw new InvalidOperationException($"Unexpected Flowchart branch receipt '{receipt.InvocationId}'.");

        var completed = state.Completed.ToHashSet(StringComparer.Ordinal);
        completed.Add(receipt.InvocationId);
        bool queueJoin = completed.Count == 2 && !state.JoinQueued;
        var next = state with { Completed = completed.Order(StringComparer.Ordinal).ToArray(), JoinQueued = state.JoinQueued || queueJoin };
        return new CompositionPlan(JsonSupport.Serialize(next), [], queueJoin ? ["flowchart-join"] : [], false);
    }

    public CompositionPlan Continue(string stateJson, string continuationId)
    {
        FlowchartState state = JsonSupport.Deserialize<FlowchartState>(stateJson);
        if (continuationId != "flowchart-join" || !state.JoinQueued || state.Completed.Length != 2 || state.Joined)
            throw new InvalidOperationException($"Flowchart continuation '{continuationId}' is not ready.");
        return new CompositionPlan(JsonSupport.Serialize(state with { Joined = true }), [], [], true);
    }

    private sealed record FlowchartState(string[] Completed, bool JoinQueued, bool Joined);
}

/// <summary>Test policy that aligns two result applications after both read the same state revision.</summary>
internal sealed class ConcurrentApplyPolicy : ICompositionPolicy, IDisposable
{
    private readonly Barrier _sameRevision = new(2);
    private int _calls;

    public string Id => "concurrent-apply-cas-witness-v1";

    public CompositionPlan Start(string inputJson)
        => new(JsonSupport.Serialize(new ConcurrentState([])),
        [
            new WorkDefinition("race-a", "snapshot-echo", "1", "{\"value\":\"a\"}"),
            new WorkDefinition("race-b", "snapshot-echo", "1", "{\"value\":\"b\"}")
        ], [], false);

    public CompositionPlan ApplyResult(string stateJson, WorkReceipt receipt)
    {
        if (Interlocked.Increment(ref _calls) <= 2 && !_sameRevision.SignalAndWait(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("Concurrent result applications did not reach the same-revision barrier.");
        ConcurrentState state = JsonSupport.Deserialize<ConcurrentState>(stateJson);
        string[] accepted = state.Accepted.Append(receipt.InvocationId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new CompositionPlan(JsonSupport.Serialize(new ConcurrentState(accepted)), [], [], accepted.Length == 2);
    }

    public CompositionPlan Continue(string stateJson, string continuationId)
        => throw new InvalidOperationException($"No concurrent-apply continuation '{continuationId}' exists.");

    public void Dispose() => _sameRevision.Dispose();

    private sealed record ConcurrentState(string[] Accepted);
}

/// <summary>Coordinates durable receipts with an independently authored composition policy.</summary>
public sealed class CompositionCoordinator
{
    private readonly DurableWorkStore _store;
    private readonly WorkScope _scope;
    private readonly string _executionId;
    private readonly string _owner;
    private readonly ICompositionPolicy _policy;
    private long? _fence;

    public CompositionCoordinator(DurableWorkStore store, WorkScope scope, string executionId, string owner, ICompositionPolicy policy)
    {
        _store = store;
        _scope = scope;
        _executionId = executionId;
        _owner = owner;
        _policy = policy;
    }

    public CommitReceipt Start(string inputJson)
    {
        CompositionPlan plan = _policy.Start(inputJson);
        CommitReceipt receipt = _store.CreateAndCommitInitialPlan(_scope, _executionId, _policy.Id, _owner, $"start:{_executionId}", plan);
        Activate();
        return receipt;
    }

    /// <summary>Pins this coordinator activation to the current owner epoch; a stale instance cannot adopt a new one.</summary>
    public void Activate()
    {
        CoordinatorSnapshot current = _store.ReadCoordinator(_scope, _executionId);
        if (current.Owner != _owner)
            throw new InvalidOperationException($"Coordinator '{_owner}' cannot activate execution owned by '{current.Owner}'.");
        _fence = current.Fence;
    }

    public int ApplyPendingResults()
    {
        int count = 0;
        foreach (PendingResult pending in _store.ReadPendingResults(_scope, _executionId))
        {
            ExecutionSnapshot execution = _store.ReadExecution(_scope, _executionId);
            var receipt = new WorkReceipt(pending.InvocationId, "Succeeded", pending.ResultJson, pending.Fingerprint);
            CompositionPlan plan = _policy.ApplyResult(execution.StateJson, receipt);
            _store.CommitPlan(_scope, _executionId, _owner, ActiveFence(), execution.Revision,
                $"result:{pending.InvocationId}", plan, sourceResultInvocation: pending.InvocationId);
            count++;
        }
        return count;
    }

    public int RunPendingContinuations()
    {
        int count = 0;
        foreach (string continuationId in _store.ReadPendingContinuations(_scope, _executionId))
        {
            ExecutionSnapshot execution = _store.ReadExecution(_scope, _executionId);
            CompositionPlan plan = _policy.Continue(execution.StateJson, continuationId);
            _store.CommitPlan(_scope, _executionId, _owner, ActiveFence(), execution.Revision,
                $"continuation:{continuationId}", plan, sourceContinuationId: continuationId);
            count++;
        }
        return count;
    }

    private long ActiveFence() => _fence ?? throw new InvalidOperationException("Coordinator must be activated before processing durable work.");
}

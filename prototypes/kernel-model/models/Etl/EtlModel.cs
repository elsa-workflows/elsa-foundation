using Kernel.Durable;
using Kernel.Workflows;

namespace Kernel.Etl;

// Parameters of this experiment, not kernel defaults. Input describes external data by reference, not bytes.
public sealed record RangeInput(int Start, int Count, string PayloadRef, int Fanout, int Credit, bool Direct = false);
public sealed record Total(long Count, long Sum);
public sealed class EtlState
{
    public RangeInput Input { get; set; } = new(0, 0, "", 64, 8);
    public int Cursor { get; set; }
    public Dictionary<string, RangeInput> Pending { get; set; } = [];
    public long Count { get; set; }
    public long Sum { get; set; }
    public int MaxPending { get; set; }
    public int Selected { get; set; }
    public bool Started { get; set; }
}

public sealed class EtlModel(bool duplicate = false, bool excessCredit = false) : IExecutionModel
{
    public const string ModelKey = "etl-spike";
    public string Key => ModelKey;
    public Type StateType => typeof(EtlState);
    public int StateVersion => 1;
    public object CreateState(StartSpec start) => new EtlState { Input = Json.Read<RangeInput>(start.Input!) };
    public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();
    public ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask TurnAsync(ITurn turn, CancellationToken cancellationToken)
    {
        var s = (EtlState)turn.State;
        foreach (var message in turn.Inbox)
        {
            if (message.Kind == Kinds.Start) s.Started = true;
            if (message.Kind != WorkflowKinds.ChildFinished) continue;
            var finished = message.Read<ChildFinished>();
            if (!s.Pending.Remove(finished.Child, out var range)) throw new InvalidOperationException("Unexpected or duplicate child result");
            if (finished.Status != InstanceStatus.Finished || finished.Output is null) throw new InvalidOperationException("Child failed");
            var total = Json.Read<Total>(finished.Output);
            if (total.Count != range.Count) throw new InvalidOperationException("Wrong child range count");
            s.Count += total.Count * (duplicate && turn.Instance.Ref.Id == "0/root" ? 2 : 1);
            s.Sum += total.Sum * (duplicate && turn.Instance.Ref.Id == "0/root" ? 2 : 1);
            s.Selected++;
        }
        if (!s.Started) return ValueTask.CompletedTask;
        var input = s.Input;
        if (input.Count == 1)
        {
            s.Count = 1;
            s.Sum = input.Start + 1L;
            s.Cursor = 1;
            turn.SetStatus(InstanceStatus.Finished, output: new Total(s.Count, s.Sum));
            return ValueTask.CompletedTask;
        }
        int span = 1;
        if (!input.Direct)
            while ((input.Count + (long)span - 1) / span > input.Fanout) span *= input.Fanout;
        while (s.Cursor < input.Count && s.Pending.Count < input.Credit + (excessCredit ? 1 : 0))
        {
            var count = Math.Min(span, input.Count - s.Cursor);
            var range = input with { Start = input.Start + s.Cursor, Count = count, Direct = false };
            var child = turn.Start(new StartSpec(ModelKey, "range", Json.Write(range), History: History.Minimal), Placement.Spread);
            s.Pending.Add(child.Id, range);
            s.Cursor += count;
            s.MaxPending = Math.Max(s.MaxPending, s.Pending.Count);
        }
        if (s.Cursor == input.Count && s.Pending.Count == 0)
            turn.SetStatus(InstanceStatus.Finished, output: new Total(s.Count, s.Sum));
        return ValueTask.CompletedTask;
    }
}

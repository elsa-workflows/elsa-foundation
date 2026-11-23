using Kernel.Durable;
using Kernel.Workflows;

namespace Kernel.Values;

public sealed record Source(int Length, int Seed)
{
    public byte[] Bytes()
    {
        var value = new byte[Length];
        new Random(Seed).NextBytes(value);
        return value;
    }
}
public sealed class ValueState
{
    public string? Inline { get; set; }
    public ValueRef? Reference { get; set; }
    public int Checkpoints { get; set; }
}

public sealed class ValueModel(IValueStore store, bool byReference) : IExecutionModel
{
    public string Key => byReference ? "value-reference" : "value-inline";
    public int StateVersion => 1;
    public Type StateType => typeof(ValueState);
    public object CreateState(StartSpec start) => new ValueState();
    public object UpgradeState(string state, int fromVersion) => throw new NotSupportedException();

    public async ValueTask<object?> ExecuteJobAsync(IJob job, CancellationToken ct)
    {
        if (job.Key == "put") return await store.PutAsync(Json.Read<Source>(job.Arguments!).Bytes(), ct);
        if (job.Key == "read") return ValueRef.For(await store.ReadAsync(Json.Read<ValueRef>(job.Arguments!), ct));
        throw new InvalidOperationException("Unknown value job.");
    }

    public ValueTask TurnAsync(ITurn turn, CancellationToken ct)
    {
        var state = (ValueState)turn.State;
        foreach (var message in turn.Inbox)
        {
            switch (message.Kind)
            {
                case Kinds.Start:
                    var source = message.Read<Source>();
                    if (byReference) turn.StartJob("put", source);
                    else state.Inline = Convert.ToBase64String(source.Bytes());
                    break;
                case Kinds.JobFinished:
                    var result = message.Read<JobFinished>();
                    if (result.Error is not null) throw new InvalidDataException("Value job failed: " + result.Error);
                    var verified = Json.Read<ValueRef>(result.Result!);
                    if (result.Key == "put") state.Reference = verified;
                    else if (result.Key == "read" && verified == state.Reference)
                        turn.SetStatus(InstanceStatus.Finished);
                    else throw new InvalidDataException("Read job returned a different reference.");
                    break;
                case "read":
                    if (byReference) turn.StartJob("read", state.Reference ?? throw new InvalidOperationException("No value."));
                    else { _ = ValueRef.For(Convert.FromBase64String(state.Inline!)); turn.SetStatus(InstanceStatus.Finished); }
                    break;
            }
        }
        if (state.Reference is not null || state.Inline is not null)
        {
            state.Checkpoints++;
            if (byReference) turn.Record(state.Reference!);
            else turn.Record(state.Inline!);
        }
        return ValueTask.CompletedTask;
    }
}

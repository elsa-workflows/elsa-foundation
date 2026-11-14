using System.Text.Json.Nodes;
using Kernel.Native;

internal static class ClrWork
{
    internal static string Kind = "leaf";
    internal static int Failures, Activations;
    internal static readonly List<(string Frame, string Operation, int Local, string Phase)> Calls = [];
    internal static void Reset(string kind) { Kind = kind; Failures = Activations = 0; Calls.Clear(); }
    internal static void Fail(string phase)
    {
        if (Kind == phase && Failures++ < 2) throw new InvalidOperationException("CLR dependency unavailable");
    }
    internal static void Record(ActivityContext c, int local, string phase) => Calls.Add((c.FrameId, c.OperationKey, local, phase));
}
internal sealed class ClrDependency : Kernel.Native.Activity<int>
{
    public ClrDependency() { }
    private int _local;
    [Input] public int Local { get => _local; set { ClrWork.Activations++; ClrWork.Fail("activation"); _local = value; } }
    protected override ValueTask<Transition> ExecuteAsync(ActivityContext c)
    { ClrWork.Record(c, Local, "leaf"); ClrWork.Fail("leaf"); return new(Complete(Local)); }
}
internal sealed class ClrWait : StatefulActivity<int, int, JsonNode?>
{
    public ClrWait() { }
    [Input] public int Local { get; set; }
    protected override ValueTask<Transition> ExecuteAsync(StartContext<JsonNode?> c)
    {
        ClrWork.Record(c, Local, "start"); ClrWork.Fail("start");
        return new(ClrWork.Kind == "resume" ? Suspend(Local, new EventTrigger("release")) : Complete(Local));
    }
    protected override ValueTask<Transition> ResumeAsync(ResumeContext<int, JsonNode?> c)
    {
        ClrWork.Record(c, c.State, "resume"); ClrWork.Fail("resume"); return new(Complete(c.State));
    }
}

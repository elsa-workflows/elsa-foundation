using Kernel.Durable;

namespace Kernel.Workflows;

/// <summary>The workflow owner captures terminality independently of an execution model's private state.</summary>
public static class WorkflowCommitClock
{
    public const string Terminal = "workflow:end";
    internal static InstanceRow End(InstanceRow row)
    {
        if (CommitClockFacts.Read(row) is not { } times) return row;
        times.TryAdd(Terminal, null);
        return row with { CommitTimes = Json.Write(times) };
    }
}

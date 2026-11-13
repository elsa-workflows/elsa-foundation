namespace Kernel.Workflows;

/// <summary>The workflow owner captures terminality independently of an execution model's private state.</summary>
public static class WorkflowCommitClock
{
    public const string Terminal = "workflow:end";
    internal static InstanceRow End(InstanceRow row)
    {
        if (row.CommitTimes is null) return row;
        var times = new Dictionary<string, long?>(row.CommitTimes, StringComparer.Ordinal);
        times.TryAdd(Terminal, null);
        return row with { CommitTimes = times };
    }
}

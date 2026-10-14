using System.Reflection;
using Elsa.Tasks.Core;
using Elsa.Tasks.Schedules;

namespace Elsa.Tasks.Tests;

/// <summary>Reads what a scheduled execution would wait before its next run, without waiting for it.</summary>
internal static class ScheduledExecutions
{
    private static readonly FieldInfo IntervalField =
        typeof(ScheduledTaskExecution).GetField("_interval", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>The interval <paramref name="execution"/> re-arms its timer with after every run.</summary>
    public static Func<TimeSpan> NextWait(IScheduledTaskExecution execution) =>
        (Func<TimeSpan>)IntervalField.GetValue(Assert.IsType<ScheduledTaskExecution>(execution))!;
}

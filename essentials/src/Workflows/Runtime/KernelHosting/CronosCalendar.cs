using Cronos;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>
/// Cron occurrences on the kernel host, read as the old core reads them (Cronos, UTC): five fields are standard cron, six or
/// more include seconds, and an occurrence is the first strictly after the given time.
/// </summary>
public sealed class CronosCalendar : ICronCalendar
{
    public long? Next(string expression, long after) =>
        Parse(expression).GetNextOccurrence(DateTimeOffset.FromUnixTimeMilliseconds(after), TimeZoneInfo.Utc, inclusive: false)?.ToUnixTimeMilliseconds();

    /// <summary>Why an expression does not read, or null when it does: the check a schedule table runs at registration.</summary>
    public static string? Error(string expression)
    {
        try { _ = Parse(expression); return null; }
        catch (FormatException refused) { return refused.Message; }
    }

    private static CronExpression Parse(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        try { return CronExpression.Parse(expression.Trim(), fields >= 6 ? CronFormat.IncludeSeconds : CronFormat.Standard); }
        catch (CronFormatException refused) { throw new FormatException($"Cron expression '{expression}' is invalid: {refused.Message}", refused); }
    }
}

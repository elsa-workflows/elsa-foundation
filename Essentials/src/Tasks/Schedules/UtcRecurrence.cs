using System.Globalization;
using System.Xml;
using Cronos;

namespace Elsa.Tasks.Schedules;

/// <summary>Pure UTC recurrence arithmetic; callers own anchors, cursors and missed-occurrence policy.</summary>
public static class UtcRecurrence
{
    /// <summary>Parses a positive ISO-8601 duration or invariant <see cref="TimeSpan"/> expression.</summary>
    /// <exception cref="FormatException">The expression is invalid or is not a positive duration.</exception>
    public static TimeSpan ParseInterval(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        TimeSpan interval;
        if (expression.StartsWith('P') || expression.StartsWith("-P", StringComparison.Ordinal))
            interval = XmlConvert.ToTimeSpan(expression);
        else if (!TimeSpan.TryParse(expression, CultureInfo.InvariantCulture, out interval))
            throw new FormatException($"Interval expression '{expression}' is neither an ISO-8601 duration nor a TimeSpan.");
        if (interval <= TimeSpan.Zero)
            throw new FormatException($"Interval expression '{expression}' must be a positive duration.");
        return interval;
    }

    /// <summary>Returns the UTC cron occurrence strictly after the cursor, or null when no occurrence remains.</summary>
    /// <param name="expression">Five-field cron, or six-field cron when seconds are allowed.</param>
    /// <param name="after">The caller-owned cursor, interpreted as an instant regardless of its offset.</param>
    /// <param name="allowSeconds">Allows six-field cron; false preserves the host Tasks five-field contract.</param>
    /// <exception cref="FormatException">The expression is invalid; the original parser cause is preserved.</exception>
    public static DateTimeOffset? NextCron(string expression, DateTimeOffset after, bool allowSeconds = true) =>
        ParseCron(expression, allowSeconds).GetNextOccurrence(after, TimeZoneInfo.Utc, inclusive: false);

    internal static CronExpression ParseCron(string expression, bool allowSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        var format = allowSeconds && fields >= 6 ? CronFormat.IncludeSeconds : CronFormat.Standard;
        try { return CronExpression.Parse(expression, format); }
        catch (Exception error) when (error is CronFormatException or MissingSeedException)
        { throw new FormatException($"Cron expression '{expression}' is invalid: {error.Message}", error); }
    }
}

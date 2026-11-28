using System.Globalization;
using System.Xml;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Parses an authored recurring interval in ISO-8601 or invariant TimeSpan format.</summary>
public static class RecurringScheduleInterval
{
    /// <summary>Returns a positive interval. The caller chooses any additional bounds and normalization.</summary>
    public static TimeSpan ParsePositive(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        // ISO-8601 durations start with 'P'; anything else is treated as a plain TimeSpan for authoring convenience.
        TimeSpan interval;
        if (expression.StartsWith('P') || expression.StartsWith("-P", StringComparison.Ordinal))
            interval = XmlConvert.ToTimeSpan(expression);
        else if (!TimeSpan.TryParse(expression, CultureInfo.InvariantCulture, out interval))
            throw new FormatException($"Interval expression '{expression}' is neither an ISO-8601 duration nor a TimeSpan.");

        if (interval <= TimeSpan.Zero)
            throw new FormatException($"Interval expression '{expression}' must be a positive duration.");

        return interval;
    }
}

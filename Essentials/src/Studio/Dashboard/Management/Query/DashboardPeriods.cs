using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Core.Models;

namespace Elsa.Studio.Dashboard.Management.Query;

internal static class DashboardPeriods
{
    public static IReadOnlyList<DashboardEffectivePeriod> Resolve(DashboardQueryDefinition query, DateTimeOffset evaluatedAt)
    {
        if (query.Time is not { } time) return [];
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(time.TimeZone);
            if (time.TimeZone != "UTC" && !zone.HasIanaId) throw Invalid("Choose an IANA timezone.");
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DashboardQueryException("INVALID_TIME", "The requested IANA timezone is unavailable.", exception);
        }
        DateTimeOffset from;
        DateTimeOffset to;
        var localNow = TimeZoneInfo.ConvertTime(evaluatedAt, zone);
        var weekDate = localNow.Date.AddDays(-((int)localNow.DayOfWeek + 6) % 7);
        switch (time.Window)
        {
            case null or "absolute":
                if (time.From is not { } absoluteFrom || time.To is not { } absoluteTo)
                    throw Invalid("An absolute window requires inclusive from and exclusive to instants.");
                from = absoluteFrom;
                to = absoluteTo;
                break;
            case "calendarWeekToDate": from = LocalInstant(weekDate, zone); to = evaluatedAt; break;
            case "last7Days": from = evaluatedAt.AddDays(-7); to = evaluatedAt; break;
            case "last30Days": from = evaluatedAt.AddDays(-30); to = evaluatedAt; break;
            default: throw Invalid("This relative time window is unsupported.");
        }
        if (time.Window is not (null or "absolute") && (time.From is not null || time.To is not null))
            throw Invalid("A relative window cannot also contain absolute instants.");
        CheckRange(from, to);
        var periods = new List<DashboardEffectivePeriod> { new("current", from.ToUniversalTime(), to.ToUniversalTime(), time.TimeZone) };
        if (query.Comparison is { } comparison)
        {
            DateTimeOffset baselineFrom;
            DateTimeOffset baselineTo;
            if (comparison.Mode == "absolute")
            {
                if (comparison.From is not { } requestedFrom || comparison.To is not { } requestedTo)
                    throw Invalid("An absolute comparison requires both baseline instants.");
                baselineFrom = requestedFrom;
                baselineTo = requestedTo;
            }
            else
            {
                if (time.Window != "calendarWeekToDate" || comparison.From is not null || comparison.To is not null)
                    throw Invalid("An equal-elapsed week comparison requires the calendar-week-to-date window.");
                baselineFrom = LocalInstant(weekDate.AddDays(-7), zone);
                baselineTo = baselineFrom.Add(to - from);
                if (baselineTo > from)
                    throw Invalid("The equal-elapsed baseline extends beyond the previous calendar week. Choose explicit comparison periods.");
            }
            CheckRange(baselineFrom, baselineTo);
            periods.Add(new("baseline", baselineFrom.ToUniversalTime(), baselineTo.ToUniversalTime(), time.TimeZone));
        }
        if (time.Bucket is { } bucket && periods.Sum(period => Math.Ceiling((period.To - period.From).TotalHours / (bucket == "hour" ? 1 : 24))) > 744)
            throw new DashboardQueryException("QUERY_LIMIT", "The requested periods exceed the total time-bucket bound.");
        return periods;
    }

    private static DateTimeOffset LocalInstant(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw Invalid("A calendar boundary is ambiguous or unavailable in this timezone. Choose explicit instants.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static void CheckRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (from >= to || to - from > TimeSpan.FromDays(93))
            throw Invalid("Each time period must be nonempty and no longer than 93 elapsed days.");
    }

    private static DashboardQueryException Invalid(string message) => new("INVALID_TIME", message);
}

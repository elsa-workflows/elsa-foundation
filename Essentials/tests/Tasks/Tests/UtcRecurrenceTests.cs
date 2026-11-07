using System.Globalization;
using Elsa.Tasks.Schedules;
using Xunit;

namespace Elsa.Tasks.Tests;

public sealed class UtcRecurrenceTests
{
    [Theory]
    [InlineData(14)]
    [InlineData(-12)]
    public void Cron_uses_the_cursor_instant_and_utc_boundaries(int offsetHours)
    {
        var instant = new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
        var cursor = instant.ToOffset(TimeSpan.FromHours(offsetHours));
        var next = UtcRecurrence.NextCron("*/15 * * * *", cursor);
        Assert.Equal(instant.AddMinutes(15), next);
        Assert.Equal(TimeSpan.Zero, next!.Value.Offset);
    }

    [Fact]
    public void Seconds_are_explicitly_allowed_and_exact_cursor_occurrences_are_excluded()
    {
        var cursor = new DateTimeOffset(2026, 11, 6, 0, 0, 10, TimeSpan.Zero);
        Assert.Equal(cursor.AddSeconds(10), UtcRecurrence.NextCron("*/10 * * * * *", cursor));
        var error = Assert.Throws<FormatException>(() => UtcRecurrence.NextCron("*/10 * * * * *", cursor, allowSeconds: false));
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void Exhausted_cron_returns_no_future_responsibility()
    {
        Assert.Null(UtcRecurrence.NextCron("0 0 30 2 *", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Hashed_cron_without_a_seed_is_a_wrapped_format_error()
    {
        var error = Assert.Throws<FormatException>(() => UtcRecurrence.NextCron("H * * * *", DateTimeOffset.UnixEpoch));
        Assert.IsType<Cronos.MissingSeedException>(error.InnerException);
    }

    [Fact]
    public void Standard_format_preserves_existing_second_macro_support()
    {
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1),
            UtcRecurrence.NextCron("@every_second", DateTimeOffset.UnixEpoch, allowSeconds: false));
    }

    [Fact]
    public void Interval_parsing_is_invariant_when_the_host_uses_comma_decimals()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(TimeSpan.FromMilliseconds(125), UtcRecurrence.ParseInterval("00:00:00.125"));
            Assert.Equal(TimeSpan.FromMilliseconds(125), UtcRecurrence.ParseInterval("PT0.125S"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using Jint.Runtime;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// The time system of a <c>binding-pure-v2</c> engine: it has no clock, its time zone is UTC on every host, and it
/// parses only the ECMAScript date-time string format (ADR 0088 D4). The <c>Date</c> prelude reports the clock-reading
/// forms with guidance first; throwing here is the safety net beneath it.
/// </summary>
internal sealed partial class ClocklessTimeSystem : ITimeSystem
{
    public static readonly ClocklessTimeSystem Instance = new();

    // Fixed names, so toString() does not depend on how the host localizes the UTC zone's display name.
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.CreateCustomTimeZone(
        "UTC", TimeSpan.Zero, "Coordinated Universal Time", "Coordinated Universal Time");

    private static readonly DefaultTimeSystem IsoParser = new(Utc, CultureInfo.InvariantCulture);

    private ClocklessTimeSystem()
    {
    }

    public TimeZoneInfo DefaultTimeZone => Utc;

    public DateTimeOffset GetUtcNow() =>
        throw new InvalidOperationException("The binding-pure-v2 JavaScript sandbox has no clock. Declare a Clock parameter instead.");

    public bool TryParse(string date, out long epochMilliseconds)
    {
        epochMilliseconds = 0;
        return IsoDateTimeString().IsMatch(date) && IsoParser.TryParse(date, out epochMilliseconds);
    }

    public TimeSpan GetUtcOffset(long epochMilliseconds) => TimeSpan.Zero;

    // ECMAScript date-time string format: YYYY[-MM[-DD]] or ±YYYYYY..., optionally THH:mm[:ss[.sss]] and Z or ±HH:mm.
    [GeneratedRegex(@"^(?:[+-]\d{6}|\d{4})(?:-\d{2}(?:-\d{2})?)?(?:T\d{2}:\d{2}(?::\d{2}(?:\.\d{3})?)?(?:Z|[+-]\d{2}:\d{2})?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDateTimeString();
}

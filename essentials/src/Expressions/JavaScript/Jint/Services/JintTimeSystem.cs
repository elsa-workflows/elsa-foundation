using System.Globalization;
using Jint.Runtime;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// The engine clock (spec 1223 FR-002, ADR 0046 Dates): <c>new Date()</c>, <c>Date.now()</c> and <c>Temporal.Now</c>
/// read the host's <see cref="TimeProvider"/>, and every expression evaluates in UTC with the invariant culture. A
/// converted Elsa 3 expression that reproduces local time names its zone in its own code; there is no ambient zone.
/// </summary>
internal sealed class JintTimeSystem(TimeProvider timeProvider) : ITimeSystem
{
    private readonly DefaultTimeSystem _utc = new(TimeZoneInfo.Utc, CultureInfo.InvariantCulture);

    public TimeZoneInfo DefaultTimeZone => TimeZoneInfo.Utc;

    public DateTimeOffset GetUtcNow() => timeProvider.GetUtcNow();

    public TimeSpan GetUtcOffset(long epochMilliseconds) => TimeSpan.Zero;

    public bool TryParse(string date, out long epochMilliseconds) => _utc.TryParse(date, out epochMilliseconds);
}

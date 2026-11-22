using System.Numerics;
using Jint.Native.Temporal;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>
/// Jint's IANA zone data with UTC as the default zone (spec 1223 FR-002, FR-023, FR-025). Without it the default zone of
/// <c>Intl.DateTimeFormat</c> and <c>Temporal.Now</c> is the host's local zone, an ambient zone an expression must not
/// carry: a script that wants a zone names it (<c>timeZone: 'Europe/Amsterdam'</c>).
/// </summary>
internal sealed class UtcDefaultTimeZoneProvider : ITimeZoneProvider
{
    public static UtcDefaultTimeZoneProvider Instance { get; } = new();

    private static readonly ITimeZoneProvider Zones = DefaultTimeZoneProvider.Instance;

    public string GetDefaultTimeZone() => "UTC";

    public long GetOffsetNanosecondsFor(string timeZoneId, BigInteger epochNanoseconds) =>
        Zones.GetOffsetNanosecondsFor(timeZoneId, epochNanoseconds);

    public BigInteger[] GetPossibleInstantsFor(string timeZoneId, int year, int month, int day, int hour, int minute, int second, int millisecond, int microsecond, int nanosecond) =>
        Zones.GetPossibleInstantsFor(timeZoneId, year, month, day, hour, minute, second, millisecond, microsecond, nanosecond);

    public BigInteger? GetNextTransition(string timeZoneId, BigInteger epochNanoseconds) =>
        Zones.GetNextTransition(timeZoneId, epochNanoseconds);

    public BigInteger? GetPreviousTransition(string timeZoneId, BigInteger epochNanoseconds) =>
        Zones.GetPreviousTransition(timeZoneId, epochNanoseconds);

    public bool IsValidTimeZone(string timeZoneId) => Zones.IsValidTimeZone(timeZoneId);

    public string CanonicalizeTimeZone(string timeZoneId) => Zones.CanonicalizeTimeZone(timeZoneId);

    public IReadOnlyCollection<string> GetAvailableTimeZones() => Zones.GetAvailableTimeZones();

    public string GetPrimaryTimeZoneIdentifier(string timeZoneId) => Zones.GetPrimaryTimeZoneIdentifier(timeZoneId);
}

using System.Text.Json;

namespace Elsa3.Mapping.Internal;

/// <summary>
/// The helper a converted Elsa 3 script uses under the date interpretation <c>SourceTimeZone</c> (spec 1223 FR-023):
/// written once into the converted expression, as the converted null/whitespace helpers are (spec 247), with the Elsa 3
/// server's IANA zone written in it. The engine stays on UTC; the helper computes Elsa 3's local time in that zone with
/// <c>Temporal</c>, daylight saving included.
/// <list type="bullet">
/// <item><c>call(d, name, args)</c>: a local getter reads the field of <c>d</c>'s instant in the zone; a local setter
/// changes that field in the zone and keeps <c>d</c>'s instant in step. A receiver that is not a <c>Date</c> is called
/// as written.</item>
/// <item><c>local(y, m, ...)</c>: the local multi-argument constructor, as epoch milliseconds.</item>
/// <item><c>parse(text)</c>: <c>Date.parse</c>. Text with an offset or a date-only text parses natively, as in Elsa 3;
/// other text is a wall-clock time in the zone.</item>
/// <item><c>from(value)</c>: the one-argument constructor: a <c>Date</c> or a number as is, a string through
/// <c>parse</c>.</item>
/// </list>
/// Wall-clock times follow <c>Date</c>: out-of-range fields roll over (through <c>Date.UTC</c> and the UTC setters), a
/// skipped hour moves forward and a repeated hour takes the earlier instant (<c>Temporal</c>'s <c>compatible</c>
/// disambiguation, which is ECMAScript's rule for local time).
/// </summary>
internal static class Elsa3ZonedDateHelper
{
    /// <summary>The name the helper is declared under in a converted expression.</summary>
    public const string Name = "__elsa3Tz";

    /// <summary>The helper's declaration for <paramref name="zone"/>, one statement.</summary>
    public static string Declaration(string zone) =>
        $"var {Name} = (function (zone) {{ {Body} }})({JsonSerializer.Serialize(zone)});";

    private const string Body =
        "function at(t) { return Temporal.Instant.fromEpochMilliseconds(t).toZonedDateTimeISO(zone); } " +
        "function wall(f, constructed) { " +
        "var t = constructed ? Date.UTC(f[0], f[1], f[2], f[3], f[4], f[5], f[6]) : (function () { var w = new Date(0); w.setUTCFullYear(f[0], f[1], f[2]); return w.setUTCHours(f[3], f[4], f[5], f[6]); })(); " +
        "if (t !== t) return NaN; " +
        "var u = Temporal.Instant.fromEpochMilliseconds(t).toZonedDateTimeISO('UTC'); " +
        "return Temporal.PlainDateTime.from({ year: u.year, month: u.month, day: u.day, hour: u.hour, minute: u.minute, second: u.second, millisecond: u.millisecond }).toZonedDateTime(zone, { disambiguation: 'compatible' }).epochMilliseconds; } " +
        "var first = { setFullYear: 0, setYear: 0, setMonth: 1, setDate: 2, setHours: 3, setMinutes: 4, setSeconds: 5, setMilliseconds: 6 }; " +
        "var count = { setFullYear: 3, setYear: 1, setMonth: 2, setDate: 1, setHours: 4, setMinutes: 3, setSeconds: 2, setMilliseconds: 1 }; " +
        "function call(d, name, args) { " +
        "if (!(d instanceof Date)) return d[name].apply(d, args); " +
        "var t = d.getTime(); var setter = Object.prototype.hasOwnProperty.call(first, name); " +
        "if (t !== t) { if (name !== 'setFullYear' && name !== 'setYear') { if (setter) args.map(Number); return NaN; } t = 0; } " +
        "var z = at(t); " +
        "switch (name) { " +
        "case 'getFullYear': return z.year; case 'getYear': return z.year - 1900; case 'getMonth': return z.month - 1; " +
        "case 'getDate': return z.day; case 'getDay': return z.dayOfWeek % 7; case 'getHours': return z.hour; " +
        "case 'getMinutes': return z.minute; case 'getSeconds': return z.second; case 'getMilliseconds': return z.millisecond; " +
        "case 'getTimezoneOffset': return -z.offsetNanoseconds / 60000000000; } " +
        "var f = [z.year, z.month - 1, z.day, z.hour, z.minute, z.second, z.millisecond]; var a = args.map(Number); " +
        "if (a.length === 0) return d.setTime(NaN); " +
        "for (var i = 0; i < Math.min(a.length, count[name]); i++) f[first[name] + i] = a[i]; " +
        "if (name === 'setYear' && f[0] === f[0]) { var y = Math.trunc(f[0]); if (y >= 0 && y <= 99) f[0] = 1900 + y; } " +
        "return d.setTime(wall(f, false)); } " +
        "function local() { var a = Array.prototype.slice.call(arguments).map(Number); while (a.length < 7) a.push(a.length === 2 ? 1 : 0); return wall(a, true); } " +
        "var offset = /(?:[zZ]|[+-]\\d{2}(?::?\\d{2})?|\\b(?:UTC|GMT|UT)\\b[^()]*)\\s*(?:\\([^)]*\\))?\\s*$/; " +
        "var dateOnly = /^\\s*[+-]?\\d{4,6}(?:-\\d{2}(?:-\\d{2})?)?\\s*$/; " +
        "function parse(value) { var text = String(value); " +
        "if (dateOnly.test(text) || offset.test(text)) return Date.parse(text); " +
        "var t = Date.parse(text); if (t !== t) return NaN; var u = new Date(t); " +
        "return wall([u.getUTCFullYear(), u.getUTCMonth(), u.getUTCDate(), u.getUTCHours(), u.getUTCMinutes(), u.getUTCSeconds(), u.getUTCMilliseconds()], false); } " +
        "function from(value) { if (value instanceof Date) return value.getTime(); " +
        "var p = typeof value === 'object' && value !== null ? value.valueOf() : value; " +
        "return typeof p === 'string' ? parse(p) : Number(p); } " +
        "return { call: call, local: local, parse: parse, from: from };";
}

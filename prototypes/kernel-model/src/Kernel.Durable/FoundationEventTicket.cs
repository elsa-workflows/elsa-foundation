using System.Globalization;

namespace Kernel.Durable;

internal static class FoundationEventTicket
{
    private const string Version = "1|";

    internal static string Encode(EventRow publication)
    {
        var until = publication.KeepUntil.ToString(CultureInfo.InvariantCulture);
        var header = FormattableString.Invariant(
            $"{Version}{publication.Key.Length}:{publication.Key}{publication.Id.Length}:{publication.Id}{until.Length}:{until}");
        return publication.Payload is { } payload ? string.Concat(header, "1", payload) : header + "0";
    }

    internal static EventRow Decode(Message ticket)
    {
        var encoded = ticket.Payload ?? throw new FormatException("A publish ticket requires an event envelope.");
        // An already committed ticket can still carry the JSON format used before bounded publication.
        if (!encoded.StartsWith(Version, StringComparison.Ordinal)) return ticket.Read<EventRow>();

        var rest = encoded.AsSpan(Version.Length);
        var key = TakeField(ref rest);
        var id = TakeField(ref rest);
        var time = TakeField(ref rest);
        var separator = key.IndexOf('_');
        if (separator <= 0 || !long.TryParse(time, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var until) ||
            rest.IsEmpty || rest[0] is not ('0' or '1') || rest[0] == '0' && rest.Length != 1)
            throw new FormatException("The publish ticket has an invalid event header.");

        return new EventRow(key, id, rest[0] == '0' ? null : rest[1..].ToString(), until)
        {
            FoundationTenant = key[..separator]
        };
    }

    private static string TakeField(ref ReadOnlySpan<char> rest)
    {
        var separator = rest.IndexOf(':');
        if (separator <= 0 ||
            !int.TryParse(rest[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var length) ||
            length > rest.Length - separator - 1)
            throw new FormatException("The publish ticket has an invalid field length.");

        var value = rest.Slice(separator + 1, length).ToString();
        rest = rest[(separator + 1 + length)..];
        return value;
    }
}

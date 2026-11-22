using System.Globalization;
using System.Text.Json.Serialization;

namespace Kernel.Durable;

internal static class FoundationEventTicket
{
    private const string Version = "1|";

    internal static string Encode(EventRow publication)
        => FoundationEventFrame.Encode(Version, publication.Payload, publication.Key, publication.Id,
            publication.KeepUntil.ToString(CultureInfo.InvariantCulture));

    internal static EventRow Decode(Message ticket)
    {
        var encoded = ticket.Payload ?? throw new FormatException("A publish ticket requires an event envelope.");
        // An already committed ticket can still carry the JSON format used before bounded publication.
        if (!encoded.StartsWith(Version, StringComparison.Ordinal)) return ticket.Read<EventRow>();

        var rest = encoded.AsSpan(Version.Length);
        var (key, id) = TakeIdentity(ref rest);
        var time = FoundationEventFrame.TakeField(ref rest);
        var separator = key.IndexOf('_');
        if (separator <= 0 || !long.TryParse(time, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var until))
            throw new FormatException("The publish ticket has an invalid event header.");

        return new EventRow(key, id, FoundationEventFrame.ReadPayload(rest), until)
        {
            FoundationTenant = key[..separator]
        };
    }

    internal static (string Key, string Id)? ReadScopedIdentity(Message ticket)
    {
        var encoded = ticket.Payload ?? throw new FormatException("A publish ticket requires an event envelope.");
        if (!encoded.StartsWith(Version, StringComparison.Ordinal))
        {
            var legacy = Json.Read<LegacyIdentity>(encoded);
            return legacy.FoundationTenant is not null ? (legacy.Key, legacy.Id) : null;
        }

        var rest = encoded.AsSpan(Version.Length);
        return TakeIdentity(ref rest);
    }

    private static (string Key, string Id) TakeIdentity(ref ReadOnlySpan<char> rest) =>
        (FoundationEventFrame.TakeField(ref rest), FoundationEventFrame.TakeField(ref rest));

    private sealed record LegacyIdentity(
        [property: JsonConverter(typeof(DurableKeyJson))] string Key,
        [property: JsonConverter(typeof(DurableKeyJson))] string Id,
        string? FoundationTenant);
}

internal static class FoundationEventFrame
{
    internal static string Encode(string version, string? payload, params string[] fields)
    {
        var header = new System.Text.StringBuilder(version);
        foreach (var field in fields)
            header.Append(field.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(field);
        return payload is null ? header.Append('0').ToString() : string.Concat(header.ToString(), "1", payload);
    }

    internal static string? ReadPayload(ReadOnlySpan<char> rest) => rest switch
    {
        "0" => null,
        ['1', ..] => rest[1..].ToString(),
        _ => throw new FormatException("The event envelope has an invalid payload marker.")
    };

    internal static string TakeField(ref ReadOnlySpan<char> rest)
    {
        var separator = rest.IndexOf(':');
        if (separator <= 0 ||
            !int.TryParse(rest[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var length) ||
            length > rest.Length - separator - 1)
            throw new FormatException("The event envelope has an invalid field length.");

        var value = rest.Slice(separator + 1, length).ToString();
        rest = rest[(separator + 1 + length)..];
        return value;
    }
}

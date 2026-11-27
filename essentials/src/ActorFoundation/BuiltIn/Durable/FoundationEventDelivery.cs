namespace Kernel.Durable;

internal static class FoundationEventDelivery
{
    private const string Version = "2|";

    internal static string Encode(EventRow publication, SubscriptionRow wait, Guid incarnation) =>
        FoundationEventFrame.Encode(Version, publication.Payload, wait.Key, publication.Id, incarnation.ToString("N"), wait.Wait);

    // Legacy event matches are JSON objects. A full, lowercase digest is the new format discriminator.
    internal static bool HasDigest(Message message) => message.EventMatch is { Length: 64 } match &&
        match.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0;

    internal static EventMatch ReadHeader(Message message, out ReadOnlySpan<char> rest)
    {
        if (message.Payload is not { } encoded || !encoded.StartsWith(Version, StringComparison.Ordinal))
            throw new FormatException("The event delivery has an invalid version.");

        rest = encoded.AsSpan(Version.Length);
        var key = FoundationEventFrame.TakeField(ref rest);
        var id = FoundationEventFrame.TakeField(ref rest);
        var occurrence = FoundationEventFrame.TakeField(ref rest);
        var wait = FoundationEventFrame.TakeField(ref rest);
        var separator = key.IndexOf('_');
        if (separator <= 0 || !Guid.TryParseExact(occurrence, "N", out var incarnation))
            throw new FormatException("The event delivery has an invalid identity.");

        return new EventMatch(key, id, message.Target.Id, wait, message.TargetCreation ?? 0)
        {
            FoundationTenant = key[..separator],
            FoundationIncarnation = incarnation
        };
    }
}

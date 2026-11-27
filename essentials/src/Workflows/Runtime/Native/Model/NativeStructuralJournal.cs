using System.Text.Json;

namespace Kernel.Native;

internal enum NativeJournalKind
{
    Turn = 0, Scheduled = 1, Started = 2, Completed = 3, Faulted = 4, Cancelled = 5
}

// Model-owned object records have a distinct wire shape. CompositeContext.Record can contribute only strings.
internal sealed record NativeStructuralJournal(int NativeStructure, NativeJournalKind Kind, long At, string? Frame, string? Node, string? Parent)
{
    private const int Version = 1;

    internal static NativeStructuralJournal Create(NativeJournalKind kind, long at, string? frame = null, string? node = null,
        string? parent = null) => new(Version, kind, at, frame, node, parent);

    // A true result identifies our wire shape. A null decoded record means the marked entry is invalid or unsupported.
    internal static bool Read(JsonElement entry, out NativeStructuralJournal? record)
    {
        record = null;
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(nameof(NativeStructure), out var version))
            return false;

        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var value) || value != Version ||
            !entry.TryGetProperty(nameof(Kind), out var kind) || kind.ValueKind != JsonValueKind.Number ||
            !entry.TryGetProperty(nameof(At), out var at) || at.ValueKind != JsonValueKind.Number ||
            !at.TryGetInt64(out var milliseconds) || milliseconds < DateTimeOffset.MinValue.ToUnixTimeMilliseconds() ||
            milliseconds > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            return true;

        try
        {
            var decoded = entry.Deserialize<NativeStructuralJournal>();
            record = decoded?.Kind switch
            {
                NativeJournalKind.Turn when decoded.Frame is null && decoded.Node is null && decoded.Parent is null => decoded,
                NativeJournalKind.Scheduled when !string.IsNullOrEmpty(decoded.Frame) && decoded.Node is null && decoded.Parent is null => decoded,
                NativeJournalKind.Started or NativeJournalKind.Completed or NativeJournalKind.Faulted or NativeJournalKind.Cancelled
                    when !string.IsNullOrEmpty(decoded.Frame) && !string.IsNullOrEmpty(decoded.Node) => decoded,
                _ => null
            };
        }
        catch (JsonException)
        {
            // The marker is present even when its contents cannot be decoded.
        }
        return true;
    }
}

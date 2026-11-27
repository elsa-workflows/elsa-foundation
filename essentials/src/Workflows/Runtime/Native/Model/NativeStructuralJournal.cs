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

    internal static NativeStructuralJournal? Read(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(nameof(NativeStructure), out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var value) || value != Version ||
            !entry.TryGetProperty(nameof(Kind), out var kind) || kind.ValueKind != JsonValueKind.Number ||
            !entry.TryGetProperty(nameof(At), out var at) || at.ValueKind != JsonValueKind.Number)
            return null;

        var record = entry.Deserialize<NativeStructuralJournal>();
        return record?.Kind switch
        {
            NativeJournalKind.Turn when record.Frame is null && record.Node is null && record.Parent is null => record,
            NativeJournalKind.Scheduled when !string.IsNullOrEmpty(record.Frame) && record.Node is null && record.Parent is null => record,
            NativeJournalKind.Started or NativeJournalKind.Completed or NativeJournalKind.Faulted or NativeJournalKind.Cancelled
                when !string.IsNullOrEmpty(record.Frame) && !string.IsNullOrEmpty(record.Node) => record,
            _ => null
        };
    }
}

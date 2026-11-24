using System.Buffers.Binary;
using System.Text.Json;

namespace Kernel.Durable;

internal static class FoundationJournalPaging
{
    internal readonly record struct Bookmark(long Position, int Ordinal)
    {
        internal const int Size = sizeof(long) + sizeof(int);

        internal static Bookmark Read(ReadOnlySpan<byte> data) =>
            new(BinaryPrimitives.ReadInt64LittleEndian(data), BinaryPrimitives.ReadInt32LittleEndian(data[sizeof(long)..]));

        internal void Write(Span<byte> data)
        {
            BinaryPrimitives.WriteInt64LittleEndian(data, Position);
            BinaryPrimitives.WriteInt32LittleEndian(data[sizeof(long)..], Ordinal);
        }
    }

    internal static byte[] Context(string tenant, string reference) =>
        JsonSerializer.SerializeToUtf8Bytes(new { Read = "actor-tail", Tenant = tenant, Actor = reference });

    internal static (IReadOnlyList<(long Revision, string Entry)> Entries, Bookmark Last) Read(
        IEnumerable<(long Position, long Revision, string Entries)> rows, Bookmark after, int limit)
    {
        var added = new List<(long Revision, string Entry)>();
        var last = after;
        foreach (var row in rows)
        {
            using var document = JsonDocument.Parse(row.Entries);
            var entries = document.RootElement;
            var split = entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() > 0;
            var count = split ? entries.GetArrayLength() : 1;
            var ordinal = row.Position == after.Position ? after.Ordinal + 1 : 0;
            for (; ordinal < count; ordinal++)
            {
                added.Add((row.Revision, split ? entries[ordinal].GetRawText() : row.Entries));
                last = new(row.Position, ordinal);
                if (added.Count == limit) return (added, last);
            }
        }
        return (added, last);
    }
}

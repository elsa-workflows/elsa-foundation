using System.Buffers.Binary;
using System.Text.Json;

namespace Kernel.EntityFramework;

/// <summary>A selection from the terminal read model of one generated actor creation.</summary>
internal static class PostgresRootTerminalSelection
{
    private const int Size = sizeof(long) * 2;

    internal static string Seal(FoundationReadCursor cursor, string tenant, string reference, long revision, long at)
    {
        Span<byte> mark = stackalloc byte[Size];
        BinaryPrimitives.WriteInt64LittleEndian(mark, revision);
        BinaryPrimitives.WriteInt64LittleEndian(mark[sizeof(long)..], at);
        return cursor.Seal(Context(tenant, reference), mark);
    }

    internal static bool Matches(FoundationReadCursor cursor, string? selection, string tenant, string reference, long revision, long at)
    {
        Span<byte> mark = stackalloc byte[Size];
        return cursor.TryOpen(Context(tenant, reference), selection, mark)
            && BinaryPrimitives.ReadInt64LittleEndian(mark) == revision
            && BinaryPrimitives.ReadInt64LittleEndian(mark[sizeof(long)..]) == at;
    }

    private static byte[] Context(string tenant, string reference) =>
        JsonSerializer.SerializeToUtf8Bytes(new { Read = "postgres-root-terminal-selection/1", Tenant = tenant, Actor = reference });
}

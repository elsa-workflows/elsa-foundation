using System.Buffers.Binary;
using System.Text.Json;

namespace Kernel.EntityFramework;

/// <summary>A selection of one generated actor creation from the read model.</summary>
internal static class PostgresRootSelection
{
    private const int Size = sizeof(long) * 2;

    // A generated reference is bound to creation one. The running mark does not name a revision.
    internal static string SealRunning(FoundationReadCursor cursor, string tenant, string reference) =>
        cursor.Seal(Context("postgres-root-running-selection/1", tenant, reference), []);

    internal static bool MatchesRunning(FoundationReadCursor cursor, string? selection, string tenant, string reference) =>
        cursor.TryOpen(Context("postgres-root-running-selection/1", tenant, reference), selection, []);

    internal static string SealTerminal(FoundationReadCursor cursor, string tenant, string reference, long revision, long at)
    {
        Span<byte> mark = stackalloc byte[Size];
        BinaryPrimitives.WriteInt64LittleEndian(mark, revision);
        BinaryPrimitives.WriteInt64LittleEndian(mark[sizeof(long)..], at);
        return cursor.Seal(Context("postgres-root-terminal-selection/1", tenant, reference), mark);
    }

    internal static bool MatchesTerminal(FoundationReadCursor cursor, string? selection, string tenant, string reference, long revision, long at)
    {
        Span<byte> mark = stackalloc byte[Size];
        return cursor.TryOpen(Context("postgres-root-terminal-selection/1", tenant, reference), selection, mark)
            && BinaryPrimitives.ReadInt64LittleEndian(mark) == revision
            && BinaryPrimitives.ReadInt64LittleEndian(mark[sizeof(long)..]) == at;
    }

    private static byte[] Context(string read, string tenant, string reference) =>
        JsonSerializer.SerializeToUtf8Bytes(new { Read = read, Tenant = tenant, Actor = reference });
}

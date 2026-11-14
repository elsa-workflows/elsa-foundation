namespace Elsa.Workflows.Publishing.Releases.Persistence.EntityFrameworkCore;

/// <summary>SQL-only built-ins; SQLite column affinity does not establish its actual storage class.</summary>
internal static class ArchiveSqliteFunctions
{
    internal static string BinaryStorageType(byte[] value) => throw new NotSupportedException("SQLite translation is required.");
    internal static string TextStorageType(string value) => throw new NotSupportedException("SQLite translation is required.");
    internal static string Character(int value) => throw new NotSupportedException("SQLite translation is required.");
}

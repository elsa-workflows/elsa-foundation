using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

/// <summary>The exact root prepared for a held start before its executable can be pinned.</summary>
internal sealed record PreparedRootStart(string Tenant, string Reference, string Actor, int Partition,
    string Operation, string Fingerprint, long Deadline);

internal static class RootPreparedStarts
{
    internal const string Table = "FoundationPreparedRootStarts";
    internal const string DeadlineIndex = "IX_FoundationPreparedRootStarts_Deadline";
    internal const string DeadlineColumns = """("Deadline", "Reference")""";

    internal static string Columns(bool sqlite)
    {
        var text = sqlite ? "TEXT" : """text COLLATE "C" """;
        var deadline = sqlite ? "INTEGER" : "bigint";
        return $$"""
            "Reference" {{text}} NOT NULL PRIMARY KEY CHECK (length("Reference") = 64),
            "Tenant" {{text}} NOT NULL,
            "Actor" {{text}} NOT NULL,
            "Partition" integer NOT NULL REFERENCES "Partitions" ("Id"),
            "Operation" {{text}} NOT NULL CHECK (length("Operation") = 64),
            "Fingerprint" {{text}} NOT NULL CHECK (length("Fingerprint") = 67),
            "Deadline" {{deadline}} NOT NULL CHECK ("Deadline" > 0)
            """;
    }

    internal static async Task InstallPostgresAsync(KernelDb db, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE TABLE IF NOT EXISTS "{Table}" ({Columns(sqlite: false)}) USING heap;
            CREATE INDEX IF NOT EXISTS "{DeadlineIndex}" ON "{Table}" {DeadlineColumns};
            """, token);
    }
}

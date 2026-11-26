namespace Kernel.EntityFramework;

/// <summary>
/// SQLite (Microsoft.Data.Sqlite): no wire, one writer. One command of many statements is one call into the engine per
/// statement, but one command, where EF sent one command per row. RETURNING as on PostgreSQL. The store assigns seq
/// inside the insert, max + 1 per row, so a multi-row insert of messages gets increasing seqs in send order.
/// </summary>
internal sealed class SqliteCommitDialect() : ReturningDialect(SqliteCommitDialect.MillisecondClock, "1", "0")
{
    internal const string MillisecondClock = "CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)";

    /// <summary>Row i of a message insert takes max(Seq) + i: SQLite evaluates the uncorrelated subquery once per statement.</summary>
    public override string? Seq(int ordinal) => FormattableString.Invariant($"""(SELECT COALESCE(MAX("Seq"), 0) FROM "Messages") + {ordinal}""");

    public override void Fence(OwnedCommand c, IReadOnlyList<FenceRow> rows, CommitWindow? window, bool unfenced, bool ignoreCount = false)
    {
        if (window is not null) throw new NotSupportedException("Commit windows are PostgreSQL's.");
        if (rows.Count == 1) { base.Fence(c, rows, window, unfenced, ignoreCount); return; }
        // A VALUES CTE can carry the whole owned set without SQLite's compound SELECT term limit.
        var values = string.Join(", ", rows.Select(r => FormattableString.Invariant($"({r.Id}, {r.Epoch}, {r.Position}, {r.NewPosition})")));
        var held = unfenced ? "1 = 1" : """ "Partitions"."Epoch" = f.epoch AND "Partitions"."Position" = f.pos """;
        c.Statement($"""
            WITH f(id, epoch, pos, np) AS (VALUES {values})
            UPDATE "Partitions" SET "Position" = f.np FROM f WHERE "Partitions"."Id" = f.id AND ({held})
            RETURNING {Now} AS "Value"
            """, Expect.Fence, rows.Count);
    }
}

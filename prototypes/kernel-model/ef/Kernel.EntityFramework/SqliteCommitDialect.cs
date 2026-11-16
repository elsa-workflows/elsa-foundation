namespace Kernel.EntityFramework;

/// <summary>
/// SQLite (Microsoft.Data.Sqlite): no wire, one writer. One command of many statements is one call into the engine per
/// statement, but one command, where EF sent one command per row. RETURNING as on PostgreSQL. The store assigns seq
/// inside the insert, max + 1 per row, so a multi-row insert of messages gets increasing seqs in send order.
/// </summary>
internal sealed class SqliteCommitDialect() : ReturningDialect("CAST((julianday('now') - 2440587.5) * 86400000 AS INTEGER)", "1", "0")
{
    /// <summary>Row i of a message insert takes max(Seq) + i: SQLite evaluates the uncorrelated subquery once per statement.</summary>
    public override string? Seq(int ordinal) => FormattableString.Invariant($"""(SELECT COALESCE(MAX("Seq"), 0) FROM "Messages") + {ordinal}""");

    public override void Fence(OwnedCommand c, IReadOnlyList<FenceRow> rows, long leaseMs, CommitWindow? window, bool unfenced, bool ignoreCount = false)
    {
        if (window is not null) throw new NotSupportedException("Commit windows are PostgreSQL's.");
        if (rows.Count == 1) { base.Fence(c, rows, leaseMs, window, unfenced, ignoreCount); return; }
        // k rows: one writer, so no lock order to keep; UPDATE ... FROM a derived table (SQLite 3.33).
        var f = string.Join(" UNION ALL ", rows.Select(r => FormattableString.Invariant($"SELECT {r.Id} AS id, {r.Epoch} AS epoch, {r.Position} AS pos, {r.NewPosition} AS np")));
        c.Statement(FormattableString.Invariant($"""UPDATE "Partitions" SET "Position" = f.np, "LeaseUntil" = {Now} + {leaseMs} FROM ({f}) AS f WHERE "Partitions"."Id" = f.id AND ({(unfenced ? "1 = 1" : "\"Partitions\".\"Epoch\" = f.epoch AND \"Partitions\".\"Position\" = f.pos")}) RETURNING {Now} AS "Value" """), Expect.Fence, rows.Count);
    }
}

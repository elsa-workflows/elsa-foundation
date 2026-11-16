namespace Kernel.EntityFramework;

/// <summary>
/// MySQL 8 (Connector/NET): one command of many statements is one round trip; every statement is followed by a SELECT
/// that reports its matched rows (ROW_COUNT with CLIENT_FOUND_ROWS, Connector/NET's default), so the client reads one
/// result set per statement (Connector/NET's reader steps over the statements that return none). No RETURNING: a sent message is one insert and a SELECT LAST_INSERT_ID(). Backticks.
/// </summary>
internal sealed class MySqlCommitDialect : CommitDialect
{
    public override string Now => "CAST(UNIX_TIMESTAMP(SYSDATE(3)) * 1000 AS SIGNED)";
    public override string Quote(string sql) => sql.Replace('"', '`');
    public override string True => "1";
    public override string False => "0";
    /// <summary>LAST_INSERT_ID() names one insert's first id, so a message is an insert of its own.</summary>
    public override int MessageRowsPerStatement => 1;

    public override void Fence(OwnedCommand c, IReadOnlyList<FenceRow> rows, long leaseMs, CommitWindow? window, bool unfenced, bool ignoreCount = false)
    {
        if (window is not null) throw new NotSupportedException("Commit windows are PostgreSQL's.");
        var lease = FormattableString.Invariant($"\"LeaseUntil\" = {Now} + {leaseMs}");
        if (rows.Count == 1)
        {
            var r = rows[0];
            c.Statement(FormattableString.Invariant($"""UPDATE "Partitions" SET "Position" = {r.NewPosition}, {lease} WHERE "Id" = {r.Id} AND ({(unfenced ? "1 = 1" : FormattableString.Invariant($"\"Epoch\" = {r.Epoch} AND \"Position\" = {r.Position}"))})"""));
        }
        else
        {
            // k rows: locked in ascending Id order first (the primary key, as the take's range would take them), then one UPDATE over a derived table.
            c.Statement($"""SELECT COUNT(*) FROM "Partitions" WHERE "Id" IN ({string.Join(", ", rows.Select(r => r.Id))}) FOR UPDATE""", Expect.Count, rows.Count);
            var f = string.Join(" UNION ALL ", rows.Select(r => FormattableString.Invariant($"SELECT {r.Id} AS id, {r.Epoch} AS epoch, {r.Position} AS pos, {r.NewPosition} AS np")));
            c.Statement($"""UPDATE "Partitions" p JOIN ({f}) AS f ON p."Id" = f.id SET p."Position" = f.np, p.{lease} WHERE {(unfenced ? "1 = 1" : "p.\"Epoch\" = f.epoch AND p.\"Position\" = f.pos")}""");
        }
        c.Statement($"""SELECT ROW_COUNT() AS "Matched", {Now} AS "Now" """, Expect.Fence, rows.Count);
    }

    public override void Checked(OwnedCommand c, string dml, int count) { c.Statement(dml); c.Statement("SELECT ROW_COUNT()", Expect.Count, count); }
    public override void Free(OwnedCommand c, string dml) { c.Statement(dml); c.Statement("SELECT 0", Expect.Skip); }
    public override void InsertMessages(OwnedCommand c, string insert, int count) { c.Statement(insert); c.Statement("SELECT LAST_INSERT_ID(), ROW_COUNT()", Expect.Seq, count); }
    public override void KeyedMessages(OwnedCommand c, string action, IReadOnlyList<string[]> keys, bool exact) =>
        Checked(c, $"""{action} WHERE "Done" = 0 AND "EventMatch" IS NULL AND {KeyColumns(exact)} IN ({KeyList(keys)})""", keys.Count);
    /// <summary>The row the database holds under the alias must be equal to both spellings: a count of 1, else not landed.</summary>
    public override void AliasCheck(OwnedCommand c, string rowId, string actor) =>
        c.Statement($"""SELECT COUNT(*) FROM "Instances" WHERE "Id" = {rowId} AND "Id" = {actor}""", Expect.Count, 1);
    // Not INSERT IGNORE: IGNORE also turns a value too long into a warning and cuts it (Engines.cs).
    public override string SubscribeIfAbsent(string key, string instance, string wait, int partition) =>
        FormattableString.Invariant($"""INSERT INTO "Subscriptions" ("Key", "Instance", "Wait", "Partition") VALUES ({key}, {instance}, {wait}, {partition}) ON DUPLICATE KEY UPDATE "Key" = "Key" """);

    public override async Task<(long Count, List<long> Values)> ReadAsync(System.Data.Common.DbDataReader reader, Expect kind, int count, CancellationToken cancellationToken)
    {
        var values = new List<long>();
        if (!await reader.ReadAsync(cancellationToken)) return (0, values);
        var first = Convert.ToInt64(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture);
        switch (kind)
        {
            case Expect.Fence: values.Add(Convert.ToInt64(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture)); return (first, values);
            case Expect.Seq:
                var rows = Convert.ToInt64(reader.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
                for (var i = 0; i < rows; i++) values.Add(first + i);
                return (rows, values);
            default: return (first, values);
        }
    }
}

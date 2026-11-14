using System.Diagnostics;
using System.Globalization;
using Npgsql;

/// <summary>
/// For the write-volume runs (WRITES.md, design-now question 7): an actor type that writes every row at one history level
/// and, with a pad, with a larger state. A start spec at that level would make <see cref="WorkflowInstance"/> write the
/// same rows: the row's level, and the journal row the history envelope gives at that level (Full: the entries; State: an
/// empty row; Minimal: none). A delete keeps its journal row at every level, as there. The pad is a JSON property the
/// models do not read, so they drop it and it is added again at each commit: the whole state is rewritten every time, as
/// a model's large variable would be. It is random base64, so PostgreSQL's TOAST compression does not hide it.
/// </summary>
sealed class Leveled(History? level, int pad)
{
    private const string Marker = "\"pad\":\"";

    public History? Level => level;

    public IActorType Over(IActorType inner) => new Shaped(inner, this);

    private sealed class Shaped(IActorType inner, Leveled shape) : IActorType
    {
        public bool Withholds(InstanceRow row, ITurnServices services) => inner.Withholds(row, services);
        public TurnOutcome? Command(TurnInput input) => shape.Shape(inner.Command(input));
        public async ValueTask<TurnOutcome> TurnAsync(TurnInput input) => shape.Shape(await inner.TurnAsync(input))!;
        public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error) => shape.Shape(inner.Park(row, actor, taken, spec, error))!;
        public ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken cancellationToken) => inner.ExecuteJobAsync(model, job, cancellationToken);
    }

    /// <summary>Rows written with the pad: a state that is not a JSON object is left as it is.</summary>
    public long Padded => _padded;
    private long _padded;

    private TurnOutcome? Shape(TurnOutcome? outcome)
    {
        if (outcome?.Row is not { } row) return outcome;
        if (level is { } history && row.History != history)
        {
            // The row was shaped at Full (every model's start spec says so) and is written at another level: what the envelope gives there.
            outcome.Journal = history switch { History.Full => outcome.Journal ?? "[]", History.State => outcome.Journal is null ? null : "[]", _ => null };
            row = row with { History = history };
        }
        if (pad > 0 && row.State.StartsWith('{') && !row.State.Contains(Marker, StringComparison.Ordinal))
        {
            row = row with { State = (row.State == "{}" ? "{" : row.State[..^1] + ",") + Marker + Pad(row.Id) + "\"}" };
            Interlocked.Increment(ref _padded);
        }
        outcome.Row = row;
        return outcome;
    }

    private string Pad(string id)
    {
        var bytes = new byte[pad * 3 / 4 + 3];
        new Random(StringComparer.Ordinal.GetHashCode(id)).NextBytes(bytes);
        return Convert.ToBase64String(bytes)[..pad];
    }
}

/// <summary>
/// PostgreSQL write volume and bloat: what a run wrote to the WAL, split by table and by record type (pg_walinspect),
/// tuple counts, dead tuples and vacuums (pg_stat_user_tables), sizes, and exact dead and free space (pgstattuple).
/// The container must keep the run's WAL (<c>-c wal_keep_size=4GB</c>), or the split cannot be read back.
/// </summary>
sealed class PgWrites(string connection)
{
    public static readonly string[] Tables = ["Instances", "Journal", "Messages", "Partitions", "Subscriptions", "Events", "Nodes"];

    public sealed record Table(string Name, long Ins, long Upd, long Hot, long Del, long Live, long Dead, long Vacuums, long AutoVacuums, long Heap, long Index, long Toast);

    public sealed record Snapshot(TimeSpan At, string Lsn, long WalBytes, long WalRecords, long WalFpi, IReadOnlyDictionary<string, Table> Tables);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<Snapshot> _timeline = [];

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var c = new NpgsqlConnection(connection + ";Application Name=kernel-writes");
        await c.OpenAsync();
        return c;
    }

    private static async Task<List<object[]>> RowsAsync(NpgsqlConnection c, string sql, params object[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, c) { CommandTimeout = 600 };
        foreach (var p in parameters) command.Parameters.AddWithValue(p);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object[]>();
        while (await reader.ReadAsync())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }
        return rows;
    }

    private static long L(object o) => o is DBNull ? 0 : Convert.ToInt64(o, CultureInfo.InvariantCulture);

    /// <summary>The extensions, once per database; and the storage parameters of the mitigations, on the empty tables.</summary>
    public async Task PrepareAsync(int fillFactor, bool fastVacuum)
    {
        await using var c = await OpenAsync();
        await RowsAsync(c, "CREATE EXTENSION IF NOT EXISTS pg_walinspect");
        await RowsAsync(c, "CREATE EXTENSION IF NOT EXISTS pgstattuple");
        foreach (var table in new[] { "Instances", "Messages" })
        {
            if (fillFactor is > 0 and < 100) await RowsAsync(c, $"""ALTER TABLE "{table}" SET (fillfactor = {fillFactor})""");
            if (fastVacuum) await RowsAsync(c, $"""ALTER TABLE "{table}" SET (autovacuum_vacuum_scale_factor = 0, autovacuum_vacuum_threshold = 500, autovacuum_vacuum_insert_scale_factor = 0, autovacuum_vacuum_insert_threshold = 2000, autovacuum_vacuum_cost_delay = 0)""");
        }
    }

    public async Task<string> DescribeAsync()
    {
        await using var c = await OpenAsync();
        var settings = await RowsAsync(c, """SELECT c.relname, array_to_string(c.reloptions, ', ') FROM pg_class c WHERE c.relname = ANY($1) AND c.relkind = 'r' AND c.reloptions IS NOT NULL ORDER BY 1""", [Tables]);
        var server = await RowsAsync(c, "SELECT current_setting('autovacuum_naptime'), current_setting('wal_keep_size'), current_setting('max_wal_size'), current_setting('checkpoint_timeout'), current_setting('full_page_writes')");
        var s = server[0];
        return $"autovacuum_naptime {s[0]}, wal_keep_size {s[1]}, max_wal_size {s[2]}, checkpoint_timeout {s[3]}, full_page_writes {s[4]}" +
               (settings.Count == 0 ? "; table settings: defaults" : "; " + string.Join("; ", settings.Select(r => $"{r[0]} ({r[1]})")));
    }

    public async Task<Snapshot> TakeAsync()
    {
        await using var c = await OpenAsync();
        // The insert position, not the write position: a record not yet written (a prune during a read, say) belongs to this
        // window. pg_walinspect reads only flushed WAL, so wait for the WAL writer to flush up to it (200 ms by default).
        var wal = (await RowsAsync(c, "SELECT pg_current_wal_insert_lsn()::text, wal_bytes, wal_records, wal_fpi FROM pg_stat_wal"))[0];
        for (var tries = 0; tries < 100 && !(bool)(await RowsAsync(c, "SELECT pg_current_wal_flush_lsn() >= $1::pg_lsn", (string)wal[0]))[0][0]; tries++) await Task.Delay(20);
        var rows = await RowsAsync(c, """
            SELECT s.relname, s.n_tup_ins, s.n_tup_upd, s.n_tup_hot_upd, s.n_tup_del, s.n_live_tup, s.n_dead_tup, s.vacuum_count, s.autovacuum_count,
                   pg_relation_size(s.relid), pg_indexes_size(s.relid), CASE WHEN c.reltoastrelid = 0 THEN 0 ELSE pg_total_relation_size(c.reltoastrelid) END
            FROM pg_stat_user_tables s JOIN pg_class c ON c.oid = s.relid
            """);
        var tables = rows.Select(r => new Table((string)r[0], L(r[1]), L(r[2]), L(r[3]), L(r[4]), L(r[5]), L(r[6]), L(r[7]), L(r[8]), L(r[9]), L(r[10]), L(r[11]))).ToDictionary(t => t.Name);
        return new Snapshot(_clock.Elapsed, (string)wal[0], L(wal[1]), L(wal[2]), L(wal[3]), tables);
    }

    /// <summary>Samples every <paramref name="every"/> until <paramref name="stop"/>, for the dead-tuple timeline.</summary>
    public async Task SampleAsync(TimeSpan every, Func<bool> stop)
    {
        while (!stop())
        {
            try { _timeline.Add(await TakeAsync()); }
            catch (Exception) { }
            await Task.Delay(every);
        }
    }

    /// <summary>
    /// What the WAL between two snapshots holds, by table and part (heap, index, toast) and by record type. <c>Bytes</c>
    /// is the work of transactions that did not abort; <c>Aborted</c> that of transactions with an abort record in the
    /// range (a fenced commit rolled back, a session killed mid-commit). The record types are of the first kind only.
    /// </summary>
    public async Task<(List<(string Table, string Part, long Records, long Bytes, long Fpi, long Aborted)> Parts, List<(string Table, string Kind, long Records, long Bytes)> Kinds)> SplitAsync(Snapshot from, Snapshot to)
    {
        if (Bytes(to.Lsn) <= Bytes(from.Lsn)) return ([], []);       // nothing written: pg_walinspect would find no record
        await using var c = await OpenAsync();
        // A record is counted once, on the relation of its first block, and so are its full-page images; a record that names
        // no block (a commit, say) is in no table. A sequence counts with the table that owns it (Messages' seq).
        const string blocks = """
            WITH aborted AS (
              SELECT DISTINCT xid FROM pg_get_wal_records_info($1::pg_lsn, $2::pg_lsn) WHERE resource_manager = 'Transaction' AND record_type LIKE 'ABORT%'
            ), b AS (
              SELECT start_lsn, block_id, relfilenode, record_length, block_fpi_length, resource_manager || '/' || record_type AS kind,
                     block_id = min(block_id) OVER (PARTITION BY start_lsn) AS first,
                     xid <> '0'::xid AND xid IN (SELECT xid FROM aborted) AS ab
              FROM pg_get_wal_block_info($1::pg_lsn, $2::pg_lsn, false)
              WHERE reldatabase = (SELECT oid FROM pg_database WHERE datname = current_database())
            ), names AS (
              SELECT c.relfilenode AS f,
                     CASE c.relkind WHEN 'r' THEN c.relname
                                    WHEN 't' THEN (SELECT o.relname FROM pg_class o WHERE o.reltoastrelid = c.oid)
                                    WHEN 'i' THEN COALESCE((SELECT o.relname FROM pg_index x JOIN pg_class o ON o.reltoastrelid = x.indrelid WHERE x.indexrelid = c.oid),
                                                           (SELECT t.relname FROM pg_index x JOIN pg_class t ON t.oid = x.indrelid WHERE x.indexrelid = c.oid))
                                    WHEN 'S' THEN COALESCE((SELECT t.relname FROM pg_depend d JOIN pg_class t ON t.oid = d.refobjid
                                                            WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.refclassid = 'pg_class'::regclass LIMIT 1), c.relname) END AS tbl,
                     CASE c.relkind WHEN 'r' THEN 'heap' WHEN 't' THEN 'toast' WHEN 'S' THEN 'seq'
                                    WHEN 'i' THEN CASE WHEN EXISTS (SELECT 1 FROM pg_index x JOIN pg_class o ON o.reltoastrelid = x.indrelid WHERE x.indexrelid = c.oid) THEN 'toast' ELSE 'index' END END AS part
              FROM pg_class c WHERE c.relfilenode <> 0
            )
            """;
        var parts = await RowsAsync(c, blocks + """
            SELECT COALESCE(n.tbl, '(catalog)'), COALESCE(n.part, ''), count(*) FILTER (WHERE b.first AND NOT b.ab), COALESCE(sum(b.record_length) FILTER (WHERE b.first AND NOT b.ab), 0),
                   COALESCE(sum(b.block_fpi_length) FILTER (WHERE b.first AND NOT b.ab), 0), COALESCE(sum(b.record_length) FILTER (WHERE b.first AND b.ab), 0)
            FROM b LEFT JOIN names n ON n.f = b.relfilenode GROUP BY 1, 2 ORDER BY 1, 2
            """, from.Lsn, to.Lsn);
        var kinds = await RowsAsync(c, blocks + """
            SELECT COALESCE(n.tbl, '(catalog)'), b.kind, count(*), sum(b.record_length)
            FROM b LEFT JOIN names n ON n.f = b.relfilenode WHERE b.first AND NOT b.ab GROUP BY 1, 2 ORDER BY 1, 4 DESC
            """, from.Lsn, to.Lsn);
        return (parts.Select(r => ((string)r[0], (string)r[1], L(r[2]), L(r[3]), L(r[4]), L(r[5]))).ToList(), kinds.Select(r => ((string)r[0], (string)r[1], L(r[2]), L(r[3]))).ToList());
    }

    /// <summary>The bytes of full-page images on every block of the run's relations, by transactions that did not abort.</summary>
    public async Task<long> FpiAsync(Snapshot from, Snapshot to)
    {
        if (Bytes(to.Lsn) <= Bytes(from.Lsn)) return 0;
        await using var c = await OpenAsync();
        return L((await RowsAsync(c, """
            WITH aborted AS (SELECT DISTINCT xid FROM pg_get_wal_records_info($1::pg_lsn, $2::pg_lsn) WHERE resource_manager = 'Transaction' AND record_type LIKE 'ABORT%')
            SELECT COALESCE(sum(block_fpi_length), 0) FROM pg_get_wal_block_info($1::pg_lsn, $2::pg_lsn, false)
            WHERE reldatabase = (SELECT oid FROM pg_database WHERE datname = current_database()) AND NOT (xid <> '0'::xid AND xid IN (SELECT xid FROM aborted))
            """, from.Lsn, to.Lsn))[0][0]);
    }

    /// <summary>Exact live, dead and free bytes of each table, from a full scan (pgstattuple).</summary>
    public async Task<List<string>> TupleStatsAsync()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;     // this flow only: 1,234 whatever the machine's locale
        await using var c = await OpenAsync();
        var lines = new List<string>();
        // The instance's TOAST table holds every state over about 2 kB: rewritten whole at each commit, so it is where large states bloat.
        foreach (var (table, relation) in new[] { ("Instances", "'\"Instances\"'::regclass"), ("  its TOAST", "(SELECT reltoastrelid FROM pg_class WHERE relname = 'Instances')"), ("Journal", "'\"Journal\"'::regclass"), ("Messages", "'\"Messages\"'::regclass"), ("Partitions", "'\"Partitions\"'::regclass") })
        {
            var r = (await RowsAsync(c, $"SELECT table_len, tuple_count, tuple_len, dead_tuple_count, dead_tuple_len, free_space, free_percent FROM pgstattuple({relation})"))[0];
            lines.Add($"  {table,-11} {L(r[0]),12:N0} {L(r[1]),9:N0} {L(r[2]),12:N0} {L(r[3]),9:N0} {L(r[4]),12:N0} {L(r[5]),12:N0} {Convert.ToDouble(r[6], CultureInfo.InvariantCulture),7:F1}");
        }
        return lines;
    }

    /// <summary>Average and largest sizes of what is left in the tables, as stored (after compression).</summary>
    public async Task<string> RowSizesAsync()
    {
        await using var c = await OpenAsync();
        var r = (await RowsAsync(c, """
            SELECT (SELECT avg(pg_column_size(i.*))::bigint FROM "Instances" i), (SELECT avg(pg_column_size(i."State"))::bigint FROM "Instances" i), (SELECT max(pg_column_size(i."State")) FROM "Instances" i),
                   (SELECT avg(octet_length(i."State"))::bigint FROM "Instances" i),
                   (SELECT avg(pg_column_size(j.*))::bigint FROM "Journal" j), (SELECT avg(pg_column_size(m.*))::bigint FROM "Messages" m)
            """))[0];
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        return $"  rows at the end, as stored: instance {L(r[0]):N0} B on average (its state {L(r[1]):N0} B stored, {L(r[3]):N0} B of text, largest {L(r[2]):N0} B stored), journal row {L(r[4]):N0} B, message {L(r[5]):N0} B";
    }

    public static long Bytes(string lsn) => lsn.Split('/') is [var hi, var lo] ? (long.Parse(hi, NumberStyles.HexNumber) << 32) + long.Parse(lo, NumberStyles.HexNumber) : 0;

    /// <summary>The report of a stress run: WAL per commit, split; tuple counts and sizes before, at the end and after a wait; the dead-tuple timeline.</summary>
    public async Task PrintAsync(Snapshot before, Snapshot end, Snapshot? after, long batches, long units, long turns, List<string>? tuplesEnd, List<string>? tuplesAfter, string? sizes = null)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;     // this flow only: 1,234 whatever the machine's locale
        var wal = Bytes(end.Lsn) - Bytes(before.Lsn);
        Console.WriteLine($"\nWRITES | WAL {before.Lsn} to {end.Lsn}: {wal:N0} bytes ({end.WalRecords - before.WalRecords:N0} records, {end.WalFpi - before.WalFpi:N0} full-page images) | {batches:N0} commits landed, {units:N0} instance commits, {turns:N0} turns | {(batches == 0 ? 0 : wal / batches):N0} bytes per commit, {(units == 0 ? 0 : wal / units):N0} per instance commit");
        var (parts, kinds) = await SplitAsync(before, end);
        var (named, aborted) = (parts.Sum(p => p.Bytes), parts.Sum(p => p.Aborted));
        var fpiAll = await FpiAsync(before, end);
        Console.WriteLine($"  in this database's relations: {named:N0} bytes by transactions that did not abort, {(units == 0 ? 0 : named / units):N0} per landed instance commit; {aborted:N0} bytes ({(named + aborted == 0 ? 0 : 100.0 * aborted / (named + aborted)):F1} % of the relation WAL, {(named == 0 ? 0 : 100.0 * aborted / named):F1} % on top of the rest) by transactions that aborted (fenced, killed); the rest is commit and abort records and any other database's work (autovacuum of an earlier run's database, say)");
        Console.WriteLine($"  full-page images: {fpiAll:N0} bytes in the run's relations, by transactions that did not abort ({(units == 0 ? 0 : fpiAll / units):N0} per landed instance commit); the column below counts those of a record's first block only");
        Console.WriteLine($"  {"table",-14} {"part",-6} {"records",9} {"WAL bytes",14} {"first-blk FPI",13} {"per commit",11} {"per inst.",10} {"aborted",12}");
        foreach (var p in parts.Where(p => p.Bytes + p.Aborted > 0))
            Console.WriteLine($"  {p.Table,-14} {p.Part,-6} {p.Records,9:N0} {p.Bytes,14:N0} {p.Fpi,13:N0} {(batches == 0 ? 0 : p.Bytes / (double)batches),11:N0} {(units == 0 ? 0 : p.Bytes / (double)units),10:N0} {p.Aborted,12:N0}");
        Console.WriteLine($"  {"(other)",-14} {"",-6} {"",9} {wal - named - aborted,14:N0} {"",13} {(batches == 0 ? 0 : (wal - named - aborted) / (double)batches),11:N0} {(units == 0 ? 0 : (wal - named - aborted) / (double)units),10:N0}   no relation of this database");
        if (sizes is not null) Console.WriteLine(sizes);
        Console.WriteLine("  by record type, transactions that did not abort (largest first, per table):");
        foreach (var group in kinds.GroupBy(k => k.Table).Where(g => g.Sum(k => k.Bytes) > 0))
            Console.WriteLine($"    {group.Key,-14} " + string.Join(", ", group.Take(6).Select(k => $"{k.Kind} {k.Records:N0} x / {k.Bytes:N0} B")));

        Console.WriteLine($"  {"table",-11} {"when",-6} {"ins",9} {"upd",9} {"hot",9} {"hot %",6} {"del",9} {"live",8} {"dead",8} {"autovac",7} {"heap",12} {"index",12} {"toast",12}");
        foreach (var name in Tables.Where(end.Tables.ContainsKey))
            foreach (var (when, s) in new[] { ("before", before), ("end", end), ("after", after) })
            {
                if (s is null || !s.Tables.TryGetValue(name, out var t)) continue;
                var b = before.Tables.GetValueOrDefault(name) ?? t;
                var (upd, hot) = (t.Upd - b.Upd, t.Hot - b.Hot);
                Console.WriteLine($"  {name,-11} {when,-6} {t.Ins - b.Ins,9:N0} {upd,9:N0} {hot,9:N0} {(upd == 0 ? "" : (100.0 * hot / upd).ToString("F0")),6} {t.Del - b.Del,9:N0} {t.Live,8:N0} {t.Dead,8:N0} {t.AutoVacuums - b.AutoVacuums,7} {t.Heap,12:N0} {t.Index,12:N0} {t.Toast,12:N0}");
            }
        if (tuplesEnd is not null)
        {
            Console.WriteLine($"  pgstattuple {"",-1} {"bytes",12} {"tuples",9} {"live bytes",12} {"dead",9} {"dead bytes",12} {"free bytes",12} {"free %",7}");
            Console.WriteLine("  at the end:");
            tuplesEnd.ForEach(Console.WriteLine);
            if (tuplesAfter is not null) { Console.WriteLine("  after the wait:"); tuplesAfter.ForEach(Console.WriteLine); }
        }
        if (_timeline.Count > 0)
        {
            var peaks = Tables.Where(end.Tables.ContainsKey).Select(n => (n, peak: _timeline.Max(s => s.Tables.GetValueOrDefault(n)?.Dead ?? 0))).Where(p => p.peak > 0);
            Console.WriteLine("  dead tuples, peak during the run: " + string.Join(", ", peaks.Select(p => $"{p.n} {p.peak:N0}")));
            Console.WriteLine($"  timeline (s from the first snapshot: dead Instances/Messages/Journal/Partitions, autovacuums of Instances/Messages, heap+index+toast of Instances/Messages in kB):");
            foreach (var s in _timeline.Where((_, i) => i % 5 == 0).Append(_timeline[^1]))
            {
                Table T(string n) => s.Tables.GetValueOrDefault(n) ?? new Table(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                long Kb(Table t) => (t.Heap + t.Index + t.Toast) / 1024;
                var (i, m) = (T("Instances"), T("Messages"));
                Console.WriteLine($"    {(s.At - before.At).TotalSeconds,5:F0}: {i.Dead,7:N0} {m.Dead,7:N0} {T("Journal").Dead,6:N0} {T("Partitions").Dead,6:N0} | {i.AutoVacuums - (before.Tables.GetValueOrDefault("Instances")?.AutoVacuums ?? 0),3} {m.AutoVacuums - (before.Tables.GetValueOrDefault("Messages")?.AutoVacuums ?? 0),3} | {Kb(i),8:N0} {Kb(m),8:N0}");
            }
        }
    }

    /// <summary>The cost run's line: the WAL of one row of the table, split by table.</summary>
    public async Task<string> CostLineAsync(Snapshot from, Snapshot to, long units)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;     // this flow only: 1,234 whatever the machine's locale
        var wal = Bytes(to.Lsn) - Bytes(from.Lsn);
        var (parts, _) = await SplitAsync(from, to);
        var by = parts.GroupBy(p => p.Table).Select(g => (g.Key, Bytes: g.Sum(p => p.Bytes))).Where(g => g.Bytes > 0).OrderByDescending(g => g.Bytes);
        var aborted = parts.Sum(p => p.Aborted);
        return $"    WAL {wal:N0} B{(units == 0 ? "" : $", {wal / units:N0} per instance commit ({units})")}: " + string.Join(", ", by.Select(g => $"{g.Key} {g.Bytes:N0}")) + $", other {wal - parts.Sum(p => p.Bytes) - aborted:N0}" + (aborted == 0 ? "" : $", aborted {aborted:N0}");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kernel.EntityFramework;

/// <summary>
/// The owned commit (round 5 B, COMMIT-SQL.md): the whole commit edge as the store's own statements, in one command per
/// round trip, through EF's connection and transaction. EF keeps the schema, the migrations and the cold paths. The
/// semantics are <see cref="EfStore.CommitOnceAsync"/>'s: the fence first (checked as its own round trip, or the first
/// statement of the batch), then every row of the batch with its affected-row count checked, then the set-based
/// deletes; subscriptions are idempotent statements, so FINDINGS 25.2's careful retry has no work here. Rows of one kind
/// are written as one multi-row statement (inserts of instances, journal rows and messages; deletes and marks of
/// messages by key), so a flush of n instances is a handful of statements, not 3n.
/// </summary>
public sealed partial class EfStore
{
    /// <summary>Which code writes the commit; null takes the engine's default: owned where the engine has a dialect.</summary>
    public CommitSql? CommitSql { get; init; }

    /// <summary>MUTATION, for the fence pins: the owned fence does not compare its matched rows with the rows it fenced.</summary>
    public bool FenceIgnoresCount { get; init; }

    /// <summary>MUTATION, for the lock-order case (K1): the k fence rows are locked in the batch's own order, not in ascending partition id.</summary>
    public bool UnorderedFenceLocks { get; init; }

    /// <summary>The store's own refusal of a mark, consume or done outside an owned batch's fenced set, after the transaction rolled back.</summary>
    private sealed class OutsideFencedSet(string message) : Exception(message);

    private CommitDialect? Dialect => field ??= PgCommitDialect.For(Engine, postgres);

    /// <summary>True when this store writes its commits with its own SQL rather than EF's SaveChanges.</summary>
    public bool OwnedCommit => (CommitSql ?? (Dialect is null ? Kernel.EntityFramework.CommitSql.EntityFramework : Kernel.EntityFramework.CommitSql.Owned)) == Kernel.EntityFramework.CommitSql.Owned;

    private CommitDialect RequireDialect() => Dialect ?? throw new NotSupportedException("This engine has no owned commit dialect yet; use CommitSql.EntityFramework.");

    private async Task<Committed> CommitOwnedAsync(CommitBatch batch, Dictionary<(string Key, string Instance, string Wait), bool> waits, long sentAt, QualifiedRootCheckpoint? root, CancellationToken cancellationToken)
    {
        var dialect = RequireDialect();
        await using var db = create();
        db.Database.AutoSavepointsEnabled = false;
        await using var transaction = await (Engine?.BeginAsync(db, cancellationToken) ?? db.Database.BeginTransactionAsync(cancellationToken));
        var commands = new List<OwnedCommand>();
        var c = new OwnedCommand(dialect) { HasFence = true };
        var window = batch.Window;
        // Statement 1: the fence, before any other row is touched (E9: the partition rows are locked first, k of them in
        // ascending partition id). Checked, it is a round trip of its own and nothing else is sent until it held; in the
        // batch, it is the first statement of the one command.
        var fences = Fenced(batch);
        dialect.Fence(c, fences, window, Unfenced, FenceIgnoresCount);
        // An owned batch, one fence or several: each turn writes its own partition's rows and journal, each mark takes the
        // epoch of its message's partition, and a mark, consume or done may touch only the fenced partitions' messages
        // (InboxKey does not name its partition, so the statements carry the bound and their counts check it).
        var owned = batch.Fences is not null;
        var (epochs, next) = (fences.ToDictionary(f => f.Id, f => f.Epoch), fences.ToDictionary(f => f.Id, f => f.Position));
        var bound = owned ? $""" AND "Partition" IN ({string.Join(", ", epochs.Keys.Order())})""" : "";
        var markEpoch = epochs.Count == 1 ? Lit(batch.Epoch) : $"""CASE "Partition" {string.Join(" ", epochs.Select(e => $"WHEN {e.Key} THEN {Lit(e.Value)}"))} END""";
        int Home(string instance) => owned ? StoreBatchRules.AddressPartition(instance) ?? batch.Partition : batch.Partition;
        if (!FenceInBatch) { commands.Add(c); c = new OwnedCommand(dialect); }

        var exact = Engine is SqlServerEngine or MySqlEngine;      // keys carry IssuedWire
        var instances = db.Model.FindEntityType(typeof(InstanceEntity))!;
        var messages = db.Model.FindEntityType(typeof(MessageEntity))!;
        var journal = db.Model.FindEntityType(typeof(JournalEntity))!;
        var subscriptions = db.Model.FindEntityType(typeof(SubscriptionEntity))!;
        var rows = new RowGroups(dialect, exact);
        string Lit(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string Bool(bool value) => value ? dialect.True : dialect.False;

        // Every row goes to its group (RowGroups: the order and the multi-row statements). A command is flushed when the
        // batch is gathered, or when the engine's parameter limit is reached first: the rows' parameters are the command's.
        void Room(int parameters)
        {
            if (c.Parameters.Count + parameters <= dialect.ParameterLimit || c.Parameters.Count == 0) return;
            rows.Flush(c);
            commands.Add(c);
            c = new OwnedCommand(dialect);
        }
        string Text(IEntityType type, string column, string? value) => value is null ? "NULL" : c.P(type.FindProperty(column)!, value);
        // The key of a message row as MailKey spells it for this engine; EventMatch and Done are the tokens EF checked. A key
        // with a match is a statement of its own; the others are gathered by action into one statement over a key list.
        var marked = false;
        void Keyed(RowGroups.Kind kind, string action, InboxKey id, bool inSet = true)
        {
            Room(exact ? 3 : 2);
            var key = MailKey(id);
            var where = inSet ? bound : "";
            marked |= where.Length > 0;
            string[] k = exact
                ? [c.P(messages.FindProperty("Id")!, key.Id), Lit(key.InboxScope), c.P(messages.FindProperty("IssuedWire")!, key.IssuedWire)]
                : [c.P(messages.FindProperty("Id")!, key.Id), Lit(key.InboxScope)];
            if (key.EventMatch is null) rows.Row(kind, action, k, where);
            else rows.Checked(kind, $"""{action} WHERE "Id" = {k[0]} AND "InboxScope" = {k[1]}{(exact ? $" AND \"IssuedWire\" = {k[2]}" : "")} AND "EventMatch" = {c.P(messages.FindProperty("EventMatch")!, key.EventMatch)} AND "Done" = {dialect.False}{where}""");
        }

        var sent = 0;
        foreach (var turn in batch.Turns)
        {
            var home = batch.PartitionOf(turn);
            if (turn.Delete && turn.Row is not { Retired: true })
            {
                Room(1);
                rows.Checked(RowGroups.Kind.InstanceDelete, $"""DELETE FROM "Instances" WHERE "Id" = {c.P(instances.FindProperty("Id")!, turn.Instance)} AND "Revision" = {Lit(turn.Expected!.Value)}""");
            }
            if (turn.Row is { } row)
            {
                Room(12);
                var alias = turn.Instance != row.Id ? turn.Instance : null;     // a provider-equal spelling: both must name the stored row (AliasUpdateGuard)
                var id = c.P(instances.FindProperty("Id")!, row.Id);
                var state = c.P(instances.FindProperty("State")!, StoredState.Encode(row));
                var (model, definition) = (c.P(instances.FindProperty("Model")!, row.Model), c.P(instances.FindProperty("Definition")!, row.Definition));
                var (jobs, handles, parent, fault, children) = (Text(instances, "Jobs", row.Jobs), Text(instances, "CustomHandles", row.CustomHandles), Text(instances, "Parent", row.Parent), Text(instances, "Fault", row.Fault), Text(instances, "Children", row.Children));
                var nextJobGeneration = row.NextJobGeneration is { } gen ? Lit(gen) : "NULL";
                if (turn.Expected is not { } expected)
                {
                    rows.Row(RowGroups.Kind.InstanceInsert, RowGroups.Instances, [id, Lit(home), model, definition, Lit((int)row.Status), state, jobs, handles, Lit(row.Revision), parent, fault, Lit((int)row.History), Bool(row.Suspended), children, Lit(row.StateVersion), Bool(false), nextJobGeneration]);
                    if (alias is not null) rows.AliasCheck(id, c.P(instances.FindProperty("Id")!, alias));
                }
                else
                {
                    var actor = alias is null ? "" : $" AND \"Id\" = {c.P(instances.FindProperty("Id")!, alias)}";
                    rows.Checked(RowGroups.Kind.InstanceUpdate, $"""UPDATE "Instances" SET "Partition" = {Lit(home)}, "Model" = {model}, "Definition" = {definition}, "Status" = {Lit((int)row.Status)}, "State" = {state}, "Jobs" = {jobs}, "CustomHandles" = {handles}, "Revision" = {Lit(row.Revision)}, "Parent" = {parent}, "Fault" = {fault}, "History" = {Lit((int)row.History)}, "Suspended" = {Bool(row.Suspended)}, "Children" = {children}, "StateVersion" = {Lit(row.StateVersion)}, "Retired" = {Bool(row.Retired)}, "NextJobGeneration" = {nextJobGeneration} WHERE "Id" = {id}{actor} AND "Revision" = {Lit(expected)}""");
                }
            }
            foreach (var key in turn.Consumed) Keyed(RowGroups.Kind.MessageConsume, """DELETE FROM "Messages" """, key);
            foreach (var key in turn.Done) Keyed(RowGroups.Kind.MessageDone, $"""UPDATE "Messages" SET "Done" = {dialect.True}""", key);
            foreach (var key in turn.Held ?? []) Keyed(RowGroups.Kind.MessageHeld, """UPDATE "Messages" SET "Trial" = 0""", key, inSet: false);      // its turn ran: no longer on trial
            foreach (var message in turn.Sent)
            {
                Room(9);
                var m = ToEntity(message, batch.HandOver && epochs.TryGetValue(message.Target.Partition, out var mine) ? mine : 0, 0);
                // seq: identity on the server engines; on SQLite the store assigns max + i inside the statement (SqliteCommitDialect.Seq).
                rows.Row(RowGroups.Kind.MessageInsert, RowGroups.Messages, [Lit(m.InboxScope), c.P(messages.FindProperty("Id")!, m.Id), Lit(m.Partition), c.P(messages.FindProperty("Target")!, m.Target), c.P(messages.FindProperty("Kind")!, m.Kind), Text(messages, "Payload", m.Payload), Text(messages, "Sender", m.Sender), Text(messages, "Start", m.Start),
                    Lit(m.Due), Lit(m.Seen), "0", "0", m.KeepUntil is { } keep ? Lit(keep) : "NULL", dialect.False, Text(messages, "ReplyTo", m.ReplyTo), Lit(m.JobGeneration), m.EventMatch is null ? "NULL" : c.P(messages.FindProperty("EventMatch")!, m.EventMatch),
                    .. exact ? [c.P(messages.FindProperty("IssuedWire")!, m.IssuedWire)] : Array.Empty<string>(), .. dialect.Seq(++sent) is { } seq ? [seq] : Array.Empty<string>()]);
            }
            if (turn.Journal is not null)
            {
                Room(2);
                rows.Row(RowGroups.Kind.JournalInsert, RowGroups.Journal, [Lit(home), Lit(++next[home]), c.P(journal.FindProperty("Instance")!, turn.Instance), Lit(turn.Delete ? -1 : root?.RevisionFor(turn.Instance) ?? turn.Row?.Revision ?? 0), c.P(journal.FindProperty("Entries")!, turn.Journal)]);
            }
            // Merge with round 3: the set-based deletes of a terminate and a delete, written last (E1, E9: nothing before the fence held).
            if (turn.Unsubscribe) { Room(1); rows.Free(RowGroups.Kind.UnsubscribeAll, $"""DELETE FROM "Subscriptions" WHERE "Instance" = {c.P(subscriptions.FindProperty("Instance")!, turn.Instance)}"""); }
            if (turn.Delete) { Room(1); rows.Free(RowGroups.Kind.JournalDelete, $"""DELETE FROM "Journal" WHERE "Partition" = {Lit(home)} AND "Instance" = {c.P(journal.FindProperty("Instance")!, turn.Instance)} AND "Revision" > 0"""); }
        }
        // FINDINGS 25.2 (M6): a wait that is there stays one wait; removing one that is not there changes nothing. Written once each, however often named.
        foreach (var ((key, instance, wait), there) in waits)
        {
            Room(3);
            var (k, i, w) = (c.P(subscriptions.FindProperty("Key")!, key), c.P(subscriptions.FindProperty("Instance")!, instance), c.P(subscriptions.FindProperty("Wait")!, wait));
            var partition = Home(instance);
            rows.Free(RowGroups.Kind.Wait, there ? dialect.SubscribeIfAbsent(k, i, w, partition) : $"""DELETE FROM "Subscriptions" WHERE "Key" = {k} AND "Instance" = {i} AND "Wait" = {w} AND "Partition" = {Lit(partition)}""");
        }
        foreach (var key in batch.Seen) Keyed(RowGroups.Kind.MessageSeen, $"""UPDATE "Messages" SET "Seen" = {markEpoch}""", key);
        foreach (var key in batch.Trial) Keyed(RowGroups.Kind.MessageTrial, $"""UPDATE "Messages" SET "Seen" = {markEpoch}, "Trial" = {markEpoch}""", key);
        rows.Flush(c);
        if (c.Text.Length > 0) commands.Add(c);

        var (now, seqs) = (0L, new List<long>());
        foreach (var command in commands)
        {
            var result = await RunAsync(db, dialect, command, cancellationToken);
            if (!result.Landed)
            {
                // A count that did not match while every fence held, in an owned batch with bound keys: a key outside the
                // fenced set is refused as definite, as the in-memory store refuses it; anything else is "not landed".
                if (!result.FenceHeld || !marked) return new Committed(false, 0, []);     // disposing the transaction rolls it back
                await transaction.RollbackAsync(cancellationToken);
                if (await OutsideAsync(db, batch, epochs.Keys, cancellationToken)) throw new OutsideFencedSet("a mark, consume or done names a message outside the batch's fenced partitions");
                return new Committed(false, 0, []);
            }
            if (command.HasFence) now = result.Now;
            seqs.AddRange(result.Seqs);
        }
        if (batch.ClearJobTrials.Count != 0 && !await ClearJobTrialsAsync(db, batch, cancellationToken))
            return new Committed(false, 0, []);
        if (root is not null && !await root.BindCheckpointAsync(db, cancellationToken)) return new Committed(false, 0, []);
        if (PastCommitStep(sentAt, batch)) return new Committed(false, 0, []);      // disposing the transaction rolls it back
        await transaction.CommitAsync(cancellationToken);
        return new Committed(true, now, seqs);
    }

    /// <summary>
    /// The fence alone, for k rows, as a transaction of its own: positions move where every
    /// (Id, Epoch, Position) is what the owner believes; one row that does not match refuses the whole statement. Returns
    /// database time, or null when refused. The probe <c>x fence</c> runs it; the Worker refactor to range ownership
    /// (PARTITIONS.md) is its consumer.
    /// </summary>
    public async ValueTask<long?> FenceAsync(IReadOnlyList<FenceRow> rows, CancellationToken cancellationToken = default)
    {
        var dialect = RequireDialect();
        await using var db = create();
        await using var transaction = await (Engine?.BeginAsync(db, cancellationToken) ?? db.Database.BeginTransactionAsync(cancellationToken));
        var c = new OwnedCommand(dialect) { HasFence = true };
        dialect.Fence(c, rows.OrderBy(r => r.Id).ToArray(), null, Unfenced, FenceIgnoresCount);
        var result = await RunAsync(db, dialect, c, cancellationToken);
        if (!result.Landed) return null;
        await transaction.CommitAsync(cancellationToken);
        return result.Now;
    }

    /// <summary>
    /// The fence rows of a batch, in ascending partition id (the lock order), each moving by its own partition's journal
    /// rows. One for a positional batch; k for an owned set. UnorderedFenceLocks keeps the order the batch lists them in.
    /// </summary>
    private FenceRow[] Fenced(CommitBatch batch)
    {
        var moves = batch.Turns.Where(t => t.Journal is not null).GroupBy(batch.PartitionOf).ToDictionary(g => g.Key, g => g.Count());
        var order = UnorderedFenceLocks && batch.Fences is { } listed ? listed : batch.Fenced;
        return [.. order.Select(f => new FenceRow(f.Partition, f.Epoch, f.Position, f.Position + moves.GetValueOrDefault(f.Partition)))];
    }

    /// <summary>After a refused owned batch: whether one of its bound keys names a message row outside the fenced partitions (rows never change partition).</summary>
    private async Task<bool> OutsideAsync(KernelDb db, CommitBatch batch, IEnumerable<int> fenced, CancellationToken cancellationToken)
    {
        var exact = Engine is SqlServerEngine or MySqlEngine;
        var keys = batch.Seen.Concat(batch.Trial).Concat(batch.Turns.SelectMany(t => t.Consumed.Concat(t.Done))).Select(k => MailKey(k)).ToList();
        foreach (var chunk in keys.Chunk(300))
        {
            var (match, values) = (new List<string>(), new List<object>());
            foreach (var key in chunk)
            {
                var at = values.Count;
                match.Add(exact ? $$"""("Id" = {{{at}}} AND "InboxScope" = {{{at + 1}}} AND "IssuedWire" = {{{at + 2}}})""" : $$"""("Id" = {{{at}}} AND "InboxScope" = {{{at + 1}}})""");
                values.AddRange(exact ? [key.Id, key.InboxScope, key.IssuedWire] : [key.Id, key.InboxScope]);
            }
            var sql = Q($"""SELECT {(Engine is SqlServerEngine ? "COUNT_BIG(*)" : "COUNT(*)")} AS "Value" FROM "Messages" WHERE "Partition" NOT IN ({string.Join(", ", fenced)}) AND ({string.Join(" OR ", match)})""");
            if ((await db.Database.SqlQueryRaw<long>(sql, [.. values]).ToListAsync(cancellationToken))[0] > 0) return true;
        }
        return false;
    }

    /// <summary>What one command said: landed, or not; and whether the fence had held when it did not (a later count failed).</summary>
    private readonly record struct Ran(bool Landed, bool FenceHeld, long Now, List<long> Seqs);

    /// <summary>Runs one command and reads its result sets in order. Not landed when a count did not match.</summary>
    private async Task<Ran> RunAsync(KernelDb db, CommitDialect dialect, OwnedCommand command, CancellationToken cancellationToken)
    {
        var builder = db.GetService<IRelationalCommandBuilderFactory>().Create().Append(dialect.Prelude).Append(command.Text.ToString());
        var values = new Dictionary<string, object?>();
        foreach (var (name, value, mapping) in command.Parameters)
        {
            builder.AddParameter(name, name, mapping, value is null);
            values[name] = value;
        }
        var (now, seqs, landed, held) = (0L, new List<long>(), true, !command.HasFence);
        RelationalDataReader? reader = null;
        try
        {
            reader = await builder.Build().ExecuteReaderAsync(
                new RelationalCommandParameterObject(db.GetService<IRelationalConnection>(), values, null, db, db.GetService<IRelationalCommandDiagnosticsLogger>()), cancellationToken);
            var data = reader.DbDataReader;
            var first = true;
            foreach (var (kind, count) in command.Results)
            {
                if (!first && !await data.NextResultAsync(cancellationToken)) throw new StoreException(Failure.Unknown, "the owned commit returned fewer result sets than it has statements");
                first = false;
                var (rows, got) = await dialect.ReadAsync(data, kind, count, cancellationToken);
                if (kind == Expect.Fence) now = got.Count > 0 ? got[0] : 0;
                if (rows != count && (kind != Expect.Fence || !FenceIgnoresCount) && kind != Expect.Skip) { landed = false; break; }      // a count that did not match: not landed, whatever the later statements say
                held |= kind == Expect.Fence;
                if (kind == Expect.Seq) seqs.AddRange(got);
            }
            if (landed) while (await data.NextResultAsync(cancellationToken)) { }
        }
        catch (Exception e) when (dialect.NotLanded(e)) { (landed, held) = (false, dialect.FenceHeldAfter(e) ?? held); }
        finally
        {
            // Not landed: the rest of the batch is of no interest, and an error a later statement raised (E1: a stale owner's
            // key violation) must not turn "not landed" into a failure. The transaction is rolled back by its disposal.
            if (reader is not null)
                try { await reader.DisposeAsync(); }
                catch (System.Data.Common.DbException) when (!landed) { }
        }
        return new(landed, held, now, seqs);
    }

    // ---- K1: the set operations over an owned set of virtual partitions, each one statement (PARTITIONS.md) --------------

    /// <summary>Owned sets need the owned commit, which fences k rows; EF's SaveChanges path fences one partition per commit.</summary>
    public bool OwnedSetsAvailable => OwnedCommit;

    private sealed class SetRow
    {
        public int Id { get; set; }
        public string? Owner { get; set; }
        public long Epoch { get; set; }
        public long Position { get; set; }
        public long Now { get; set; }
        public bool Died { get; set; }
    }

    private const string Pid = "\"Partitions\".\"Id\"";
    private static string List(IEnumerable<int> ids) => string.Join(", ", ids);
    private static string EpochOf(IEnumerable<PartitionFence> fences, string column) =>
        $"CASE {column} {string.Join(" ", fences.Select(f => FormattableString.Invariant($"WHEN {f.Partition} THEN {f.Epoch}")))} END";

    /// <summary>
    /// PostgreSQL: a locking read in ascending id that the statement's update joins, so the k rows are locked in ascending
    /// partition id (LockRows above Sort); a plain UPDATE over an id list may visit them in heap order. SQL Server and MySQL
    /// read an IN list over the clustered key in ascending order; SQLite has one writer.
    /// </summary>
    private static string Locked(string ids) => $"""WITH l AS (SELECT "Id" FROM "Partitions" WHERE "Id" IN ({ids}) ORDER BY "Id" FOR UPDATE) """;

    /// <summary>A take of a set: one statement, each row decided as the one-partition take decides it; the death count as that take's.</summary>
    public async ValueTask<OwnedLease> TakeAsync(IReadOnlyCollection<int> partitions, string node, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        await using var caller = ProtectMySqlCaller(cancellationToken);
        try { return await TakeSetAsync(partitions, node, lease, caller?.Token ?? cancellationToken); }
        catch (Exception error) when (caller is { CallerCancelled: true })
        {
            throw await caller.CallerExceptionAsync(error);
        }
        catch (Exception e) when (TransientTake(e) is { } transient) { throw transient; }
    }

    private async ValueTask<OwnedLease> TakeSetAsync(IReadOnlyCollection<int> partitions, string node, TimeSpan lease, CancellationToken cancellationToken)
    {
        if (partitions.Count == 0) return new([], 0);
        var ids = List(partitions.Distinct().Order());
        await using var db = create();
        var begun = await BeginNodeTakeAsync(db, lease, cancellationToken);
        await using var transaction = begun.Transaction;
        await using var reset = begun.Reset;       // disposed before the transaction, on its open connection
        // The node lease (K1b): the owners' node records read first, then this node's own written, then the rows below.
        var expired = await ReadAndRenewAsync(db, NodeReadInUpdate ? null : ids, node, lease, cancellationToken);
        var take = """ "Owner" = {0}, "Epoch" = "Epoch" + 1""";
        var free = NodeReadInUpdate ? $"""("Owner" IS NULL OR "Owner" = {"{0}"} OR NOT EXISTS (SELECT 1 FROM "Nodes" n WHERE n."Id" = "Partitions"."Owner" AND n."LeaseUntil" >= {Now}))"""     // MUTATION
            : $"""("Owner" IS NULL OR "Owner" = {"{0}"}{(expired.Count == 0 ? "" : $" OR \"Owner\" IN ({string.Join(", ", expired.Select((_, i) => $"{{{i + 1}}}"))})")})""";
        object[] parameters = [node, .. expired];
        var died = """("Owner" IS NOT NULL AND "Owner" <> {0})""";
        var sql = Engine switch
        {
            SqlServerEngine => $"""UPDATE "Partitions" SET "Died" = CASE WHEN {died} THEN 1 ELSE 0 END,{take} OUTPUT inserted."Id", inserted."Owner", inserted."Epoch", inserted."Position", {Now} AS "Now", inserted."Died" WHERE "Id" IN ({ids}) AND {free}""",
            // No RETURNING: every row of the set this node owns after the update was taken by it, in this transaction.
            MySqlEngine => Q($"""UPDATE "Partitions" SET "Died" = {died},{take} WHERE "Id" IN ({ids}) AND {free}; SELECT "Id", "Owner", "Epoch", "Position", {Now} AS "Now", "Died" FROM "Partitions" WHERE "Id" IN ({ids}) AND "Owner" = {"{0}"}"""),
            _ when postgres => $"""{Locked(ids)}UPDATE "Partitions" SET "Died" = {died},{take} FROM l WHERE {Pid} = l."Id" AND {free} RETURNING {Pid}, "Owner", "Epoch", "Position", {Now} AS "Now", "Died" """,
            _ => $"""UPDATE "Partitions" SET "Died" = {died},{take} WHERE "Id" IN ({ids}) AND {free} RETURNING "Id", "Owner", "Epoch", "Position", {Now} AS "Now", "Died" """,
        };
        var rows = await db.Database.SqlQueryRaw<SetRow>(sql, parameters).ToListAsync(cancellationToken);
        // Taken from another owner, so its lease had expired: one more attempt for what it had on trial in that partition.
        if (rows.Where(r => r.Died).Select(r => FormattableString.Invariant($"""("Partition" = {r.Id} AND "Trial" = {r.Epoch - 1})""")).ToList() is { Count: > 0 } dead)
            await db.Database.ExecuteSqlRawAsync(Q($"""UPDATE "Messages" SET "Attempts" = "Attempts" + 1 WHERE "Done" = {False} AND ({string.Join(" OR ", dead)})"""), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new([.. rows.OrderBy(r => r.Id).Select(r => new PartitionFence(r.Id, r.Epoch, r.Position))], rows.Count > 0 ? rows.Min(r => r.Now) : 0);
    }

    /// <summary>
    /// A take after a reset, only if this node still holds the row at <paramref name="epoch"/>: one conditional UPDATE on
    /// owner and epoch that bumps the epoch, renews the lease and keeps the position. No death: no attempt is counted.
    /// </summary>
    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, CancellationToken cancellationToken = default) =>
        RetakeInTransactionAsync(partition, node, epoch, lease, countDeaths: false, cancellationToken);

    /// <summary>The retake, counting a death on the previous epoch's unfinished trial messages when asked (#4837, a pending partition).</summary>
    public ValueTask<Lease?> RetakeAsync(int partition, string node, long epoch, TimeSpan lease, bool countDeaths, CancellationToken cancellationToken = default) =>
        RetakeInTransactionAsync(partition, node, epoch, lease, countDeaths, cancellationToken);

    /// <summary>A release of a set: one statement frees each row still at its epoch; the kept rows are not written.</summary>
    public async ValueTask ReleaseAsync(IReadOnlyCollection<PartitionFence> owned, CancellationToken cancellationToken = default)
    {
        if (owned.Count == 0) return;
        var ids = List(owned.Select(f => f.Partition).Distinct().Order());
        var mine = string.Join(" OR ", owned.Select(f => FormattableString.Invariant($"""({Pid} = {f.Partition} AND "Epoch" = {f.Epoch})""")));
        await using var db = create();
        await db.Database.ExecuteSqlRawAsync(postgres && Engine is null
            ? $"""{Locked(ids)}UPDATE "Partitions" SET "Owner" = NULL FROM l WHERE {Pid} = l."Id" AND ({mine})"""
            : Q($"""UPDATE "Partitions" SET "Owner" = NULL WHERE "Id" IN ({ids}) AND ({mine})"""), cancellationToken);
    }

    /// <summary>One query over an owned set: the one-partition poll with its predicate over the set, each row compared with its own partition's epoch.</summary>
    public async ValueTask<Polled> PollAsync(IReadOnlyCollection<PartitionFence> owned, TimeSpan horizon, int limit, CancellationToken cancellationToken = default)
    {
        StoreBatchRules.RequireDistinct(owned);
        if (owned.Count == 0) return new([], 0);
        if (_ordinaryRoots?.FixedPartition is not null)
        {
            var fence = owned.Single();
            return await PollAsync(fence.Partition, fence.Epoch, horizon, limit, cancellationToken);
        }
        var single = Q("""m."Partition" = {0} AND m."Seen" < {1}""");
        await using var db = create();
        var poll = PollSql(db.UsesRootOperatorProtocol);
        if (!poll.Contains(single, StringComparison.Ordinal)) throw new InvalidOperationException("The poll has no partition predicate where the set poll replaces it.");
        var set = Q($"""m."Partition" IN ({List(owned.Select(f => f.Partition).Order())}) AND m."Seen" < {EpochOf(owned, "m.\"Partition\"")}""");
        var sql = string.Format(System.Globalization.CultureInfo.InvariantCulture, poll.Replace(single, set, StringComparison.Ordinal), 0, 0, (long)horizon.TotalMilliseconds, limit);
        if (_ordinaryRoots is { } root) return ToGeneratedPoll(await db.Database.SqlQueryRaw<GeneratedPollRow>(sql).ToListAsync(cancellationToken), root, db.UsesRootOperatorProtocol, db.UsesRootReplyProtocol);
        return ToPolled(await db.Database.SqlQueryRaw<PollRow>(sql).ToListAsync(cancellationToken));
    }
}

/// <summary>
/// The rows a command has gathered, written when flushed in a fixed order (<see cref="Kind"/>: per table deletes, then
/// updates, then inserts, as EF's SaveChanges ordered them; Instances, then Messages, Journal and Subscriptions), rows of
/// one kind as one multi-row statement: inserts of instances, messages and journal rows; deletes and marks of messages by
/// key. So a flush of n new instances is three statements, not 3n, and the order of turns in the batch does not change
/// the statements. The rows' parameters belong to the command they were gathered for, so a group is flushed before its
/// command ends.
/// </summary>
internal sealed class RowGroups(CommitDialect dialect, bool exact)
{
    public const string Instances = """INSERT INTO "Instances" ("Id", "Partition", "Model", "Definition", "Status", "State", "Jobs", "CustomHandles", "Revision", "Parent", "Fault", "History", "Suspended", "Children", "StateVersion", "Retired", "NextJobGeneration") VALUES """;
    public const string Journal = """INSERT INTO "Journal" ("Partition", "Position", "Instance", "Revision", "Entries") VALUES """;
    public const string Messages = """INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Payload", "Sender", "Start", "Due", "Seen", "Attempts", "Trial", "KeepUntil", "Done", "ReplyTo", "JobGeneration", "EventMatch" """;

    /// <summary>The order statements are written in.</summary>
    public enum Kind { InstanceDelete, InstanceUpdate, InstanceInsert, AliasCheck, MessageConsume, MessageDone, MessageHeld, MessageSeen, MessageTrial, MessageInsert, JournalInsert, Wait, UnsubscribeAll, JournalDelete }

    private readonly SortedDictionary<Kind, List<object>> _groups = [];      // string: a statement of its own; string[]: a row of a multi-row statement; (string, string): an alias check
    private readonly Dictionary<Kind, (string Action, string Where)> _actions = [];

    private List<object> Of(Kind kind) => _groups.TryGetValue(kind, out var rows) ? rows : _groups[kind] = [];

    /// <summary>A row of a multi-row statement: an insert, or a key of a delete or mark whose SQL is <paramref name="action"/>, bound by <paramref name="where"/>.</summary>
    public void Row(Kind kind, string action, string[] values, string where = "") { _actions[kind] = (action, where); Of(kind).Add(values); }

    /// <summary>A statement with a predicate of its own, checked to affect one row (a count of 0 says "not landed").</summary>
    public void Checked(Kind kind, string dml) => Of(kind).Add(dml);

    /// <summary>A statement whose count does not matter.</summary>
    public void Free(Kind kind, string dml) => Of(kind).Add((dml, false));

    /// <summary>After the inserts: the row the database holds under the alias must be equal to both spellings (SQL Server, MySQL).</summary>
    public void AliasCheck(string row, string actor) => Of(Kind.AliasCheck).Add((row, actor));

    /// <summary>Writes everything gathered to <paramref name="c"/>, multi-row statements of at most <see cref="CommitDialect.RowsPerStatement"/> rows.</summary>
    public void Flush(OwnedCommand c)
    {
        foreach (var (kind, items) in _groups)
        {
            foreach (var chunk in items.OfType<string[]>().Chunk(kind == Kind.MessageInsert ? dialect.MessageRowsPerStatement : dialect.RowsPerStatement))
            {
                var values = string.Join(", ", chunk.Select(r => "(" + string.Join(", ", r) + ")"));
                switch (kind)
                {
                    case Kind.MessageInsert: dialect.InsertMessages(c, Messages + (exact ? ", \"IssuedWire\"" : "") + (dialect.Seq(1) is null ? "" : ", \"Seq\"") + ") VALUES " + values, chunk.Length); break;
                    case Kind.InstanceInsert: dialect.Checked(c, Instances + values, chunk.Length); break;
                    case Kind.JournalInsert: dialect.Checked(c, Journal + values, chunk.Length); break;
                    default: dialect.KeyedMessages(c, _actions[kind].Action, chunk, exact, _actions[kind].Where); break;
                }
            }
            foreach (var item in items)
                switch (item)
                {
                    case string dml: dialect.Checked(c, dml, 1); break;
                    case (string dml, false): dialect.Free(c, dml); break;
                    case (string row, string actor): dialect.AliasCheck(c, row, actor); break;
                }
        }
        _groups.Clear();
    }
}

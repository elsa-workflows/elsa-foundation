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

    private CommitDialect? Dialect => field ??= PgCommitDialect.For(Engine, postgres);

    /// <summary>True when this store writes its commits with its own SQL rather than EF's SaveChanges.</summary>
    public bool OwnedCommit => (CommitSql ?? (Dialect is null ? Kernel.EntityFramework.CommitSql.EntityFramework : Kernel.EntityFramework.CommitSql.Owned)) == Kernel.EntityFramework.CommitSql.Owned;

    private CommitDialect RequireDialect() => Dialect ?? throw new NotSupportedException("This engine has no owned commit dialect yet; use CommitSql.EntityFramework.");

    private async Task<Committed> CommitOwnedAsync(CommitBatch batch, Dictionary<(string Key, string Instance, string Wait), bool> waits, CancellationToken cancellationToken)
    {
        var dialect = RequireDialect();
        await using var db = create();
        db.Database.AutoSavepointsEnabled = false;
        await using var transaction = await (Engine?.BeginAsync(db, cancellationToken) ?? db.Database.BeginTransactionAsync(cancellationToken));
        var commands = new List<OwnedCommand>();
        var c = new OwnedCommand(dialect) { HasFence = true };
        var (lease, window) = ((long)batch.Lease.TotalMilliseconds, batch.Window);
        // Statement 1: the fence, before any other row is touched (E9: the partition row is locked first). Checked, it is a
        // round trip of its own and nothing else is sent until it held; in the batch, it is the first statement of the one command.
        dialect.Fence(c, Fenced(batch), lease, window, Unfenced);
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
        void Keyed(RowGroups.Kind kind, string action, InboxKey id)
        {
            Room(exact ? 3 : 2);
            var key = MailKey(id);
            string[] k = exact
                ? [c.P(messages.FindProperty("Id")!, key.Id), Lit(key.InboxScope), c.P(messages.FindProperty("IssuedWire")!, key.IssuedWire)]
                : [c.P(messages.FindProperty("Id")!, key.Id), Lit(key.InboxScope)];
            if (key.EventMatch is null) rows.Row(kind, action, k);
            else rows.Checked(kind, $"""{action} WHERE "Id" = {k[0]} AND "InboxScope" = {k[1]}{(exact ? $" AND \"IssuedWire\" = {k[2]}" : "")} AND "EventMatch" = {c.P(messages.FindProperty("EventMatch")!, key.EventMatch)} AND "Done" = {dialect.False}""");
        }

        var position = batch.Position;
        foreach (var turn in batch.Turns)
        {
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
                if (turn.Expected is not { } expected)
                {
                    rows.Row(RowGroups.Kind.InstanceInsert, RowGroups.Instances, [id, Lit(batch.Partition), model, definition, Lit((int)row.Status), state, jobs, handles, Lit(row.Revision), parent, fault, Lit((int)row.History), Bool(row.Suspended), children, Lit(row.StateVersion), Bool(false)]);
                    if (alias is not null) rows.AliasCheck(id, c.P(instances.FindProperty("Id")!, alias));
                }
                else
                {
                    var actor = alias is null ? "" : $" AND \"Id\" = {c.P(instances.FindProperty("Id")!, alias)}";
                    rows.Checked(RowGroups.Kind.InstanceUpdate, $"""UPDATE "Instances" SET "Partition" = {Lit(batch.Partition)}, "Model" = {model}, "Definition" = {definition}, "Status" = {Lit((int)row.Status)}, "State" = {state}, "Jobs" = {jobs}, "CustomHandles" = {handles}, "Revision" = {Lit(row.Revision)}, "Parent" = {parent}, "Fault" = {fault}, "History" = {Lit((int)row.History)}, "Suspended" = {Bool(row.Suspended)}, "Children" = {children}, "StateVersion" = {Lit(row.StateVersion)}, "Retired" = {Bool(row.Retired)} WHERE "Id" = {id}{actor} AND "Revision" = {Lit(expected)}""");
                }
            }
            foreach (var key in turn.Consumed) Keyed(RowGroups.Kind.MessageConsume, """DELETE FROM "Messages" """, key);
            foreach (var key in turn.Done) Keyed(RowGroups.Kind.MessageDone, $"""UPDATE "Messages" SET "Done" = {dialect.True}""", key);
            foreach (var key in turn.Held ?? []) Keyed(RowGroups.Kind.MessageHeld, """UPDATE "Messages" SET "Trial" = 0""", key);      // its turn ran: no longer on trial
            foreach (var message in turn.Sent)
            {
                Room(9);
                var m = ToEntity(message, batch.HandOver && message.Target.Partition == batch.Partition ? batch.Epoch : 0, 0);
                rows.Row(RowGroups.Kind.MessageInsert, RowGroups.Messages, [Lit(m.InboxScope), c.P(messages.FindProperty("Id")!, m.Id), Lit(m.Partition), c.P(messages.FindProperty("Target")!, m.Target), c.P(messages.FindProperty("Kind")!, m.Kind), Text(messages, "Payload", m.Payload), Text(messages, "Sender", m.Sender), Text(messages, "Start", m.Start),
                    Lit(m.Due), Lit(m.Seen), "0", "0", m.KeepUntil is { } keep ? Lit(keep) : "NULL", dialect.False, Text(messages, "ReplyTo", m.ReplyTo), m.EventMatch is null ? "NULL" : c.P(messages.FindProperty("EventMatch")!, m.EventMatch),
                    .. exact ? [c.P(messages.FindProperty("IssuedWire")!, m.IssuedWire)] : Array.Empty<string>()]);
            }
            if (turn.Journal is not null)
            {
                Room(2);
                rows.Row(RowGroups.Kind.JournalInsert, RowGroups.Journal, [Lit(batch.Partition), Lit(++position), c.P(journal.FindProperty("Instance")!, turn.Instance), Lit(turn.Delete ? -1 : turn.Row?.Revision ?? 0), c.P(journal.FindProperty("Entries")!, turn.Journal)]);
            }
            // Merge with round 3: the set-based deletes of a terminate and a delete, written last (E1, E9: nothing before the fence held).
            if (turn.Unsubscribe) { Room(1); rows.Free(RowGroups.Kind.UnsubscribeAll, $"""DELETE FROM "Subscriptions" WHERE "Instance" = {c.P(subscriptions.FindProperty("Instance")!, turn.Instance)}"""); }
            if (turn.Delete) { Room(1); rows.Free(RowGroups.Kind.JournalDelete, $"""DELETE FROM "Journal" WHERE "Partition" = {Lit(batch.Partition)} AND "Instance" = {c.P(journal.FindProperty("Instance")!, turn.Instance)} AND "Revision" > 0"""); }
        }
        // FINDINGS 25.2 (M6): a wait that is there stays one wait; removing one that is not there changes nothing. Written once each, however often named.
        foreach (var ((key, instance, wait), there) in waits)
        {
            Room(3);
            var (k, i, w) = (c.P(subscriptions.FindProperty("Key")!, key), c.P(subscriptions.FindProperty("Instance")!, instance), c.P(subscriptions.FindProperty("Wait")!, wait));
            rows.Free(RowGroups.Kind.Wait, there ? dialect.SubscribeIfAbsent(k, i, w, batch.Partition) : $"""DELETE FROM "Subscriptions" WHERE "Key" = {k} AND "Instance" = {i} AND "Wait" = {w} AND "Partition" = {Lit(batch.Partition)}""");
        }
        foreach (var key in batch.Seen) Keyed(RowGroups.Kind.MessageSeen, $"""UPDATE "Messages" SET "Seen" = {Lit(batch.Epoch)}""", key);
        foreach (var key in batch.Trial) Keyed(RowGroups.Kind.MessageTrial, $"""UPDATE "Messages" SET "Seen" = {Lit(batch.Epoch)}, "Trial" = {Lit(batch.Epoch)}""", key);
        rows.Flush(c);
        if (c.Text.Length > 0) commands.Add(c);

        var (now, seqs) = (0L, new List<long>());
        foreach (var command in commands)
        {
            var result = await RunAsync(db, dialect, command, cancellationToken);
            if (result is null) return new Committed(false, 0, []);     // disposing the transaction rolls it back
            if (command.HasFence) now = result.Value.Now;
            seqs.AddRange(result.Value.Seqs);
        }
        await transaction.CommitAsync(cancellationToken);
        return new Committed(true, now, seqs);
    }

    /// <summary>
    /// The fence alone, for k rows, as a transaction of its own: positions move and leases are renewed where every
    /// (Id, Epoch, Position) is what the owner believes; one row that does not match refuses the whole statement. Returns
    /// database time, or null when refused. The probe <c>x fence</c> runs it; the Worker refactor to range ownership
    /// (PARTITIONS.md) is its consumer.
    /// </summary>
    public async ValueTask<long?> FenceAsync(IReadOnlyList<FenceRow> rows, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        var dialect = RequireDialect();
        await using var db = create();
        await using var transaction = await (Engine?.BeginAsync(db, cancellationToken) ?? db.Database.BeginTransactionAsync(cancellationToken));
        var c = new OwnedCommand(dialect) { HasFence = true };
        dialect.Fence(c, rows.OrderBy(r => r.Id).ToArray(), (long)lease.TotalMilliseconds, null, Unfenced);
        var result = await RunAsync(db, dialect, c, cancellationToken);
        if (result is null) return null;
        await transaction.CommitAsync(cancellationToken);
        return result.Value.Now;
    }

    /// <summary>The fence rows of a batch, in ascending Id order. One today; k after the Worker refactor to range ownership (PARTITIONS.md).</summary>
    private static FenceRow[] Fenced(CommitBatch batch) => [new(batch.Partition, batch.Epoch, batch.Position, batch.Position + batch.Positions)];

    /// <summary>Runs one command and reads its result sets in order. Null when a count did not match: the commit did not land.</summary>
    private async Task<(long Now, List<long> Seqs)?> RunAsync(KernelDb db, CommitDialect dialect, OwnedCommand command, CancellationToken cancellationToken)
    {
        var builder = db.GetService<IRelationalCommandBuilderFactory>().Create().Append(dialect.Prelude).Append(command.Text.ToString());
        var values = new Dictionary<string, object?>();
        foreach (var (name, value, mapping) in command.Parameters)
        {
            builder.AddParameter(name, name, mapping, value is null);
            values[name] = value;
        }
        var (now, seqs, landed) = (0L, new List<long>(), true);
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
                if (kind == Expect.Seq) seqs.AddRange(got);
            }
            if (landed) while (await data.NextResultAsync(cancellationToken)) { }
        }
        catch (Exception e) when (dialect.NotLanded(e)) { landed = false; }
        finally
        {
            // Not landed: the rest of the batch is of no interest, and an error a later statement raised (E1: a stale owner's
            // key violation) must not turn "not landed" into a failure. The transaction is rolled back by its disposal.
            if (reader is not null)
                try { await reader.DisposeAsync(); }
                catch (System.Data.Common.DbException) when (!landed) { }
        }
        return landed ? (now, seqs) : null;
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
    public const string Instances = """INSERT INTO "Instances" ("Id", "Partition", "Model", "Definition", "Status", "State", "Jobs", "CustomHandles", "Revision", "Parent", "Fault", "History", "Suspended", "Children", "StateVersion", "Retired") VALUES """;
    public const string Journal = """INSERT INTO "Journal" ("Partition", "Position", "Instance", "Revision", "Entries") VALUES """;
    public const string Messages = """INSERT INTO "Messages" ("InboxScope", "Id", "Partition", "Target", "Kind", "Payload", "Sender", "Start", "Due", "Seen", "Attempts", "Trial", "KeepUntil", "Done", "ReplyTo", "EventMatch" """;

    /// <summary>The order statements are written in.</summary>
    public enum Kind { InstanceDelete, InstanceUpdate, InstanceInsert, AliasCheck, MessageConsume, MessageDone, MessageHeld, MessageSeen, MessageTrial, MessageInsert, JournalInsert, Wait, UnsubscribeAll, JournalDelete }

    private readonly SortedDictionary<Kind, List<object>> _groups = [];      // string: a statement of its own; string[]: a row of a multi-row statement; (string, string): an alias check
    private readonly Dictionary<Kind, string> _actions = [];

    private List<object> Of(Kind kind) => _groups.TryGetValue(kind, out var rows) ? rows : _groups[kind] = [];

    /// <summary>A row of a multi-row statement: an insert, or a key of a delete or mark whose SQL is <paramref name="action"/>.</summary>
    public void Row(Kind kind, string action, string[] values) { _actions[kind] = action; Of(kind).Add(values); }

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
            foreach (var chunk in items.OfType<string[]>().Chunk(dialect.RowsPerStatement))
            {
                var values = string.Join(", ", chunk.Select(r => "(" + string.Join(", ", r) + ")"));
                switch (kind)
                {
                    case Kind.MessageInsert: dialect.InsertMessages(c, Messages + (exact ? ", \"IssuedWire\") VALUES " : ") VALUES ") + values, chunk.Length); break;
                    case Kind.InstanceInsert: dialect.Checked(c, Instances + values, chunk.Length); break;
                    case Kind.JournalInsert: dialect.Checked(c, Journal + values, chunk.Length); break;
                    default: dialect.KeyedMessages(c, _actions[kind], chunk, exact); break;
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

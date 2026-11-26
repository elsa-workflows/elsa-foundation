using System.Data;
using System.Data.Common;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;
using Reader = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryOccurrenceReader;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class RuntimeHistoryOccurrenceReaderTests
{
    [Fact]
    public async Task Normal_owned_occurrence_read_progresses_under_a_held_writer_and_refreshes_after_commit()
    {
        await using var seed = await Database.Create(); await seed.Append([Event(1, "a"), Event(2, "b")]);
        await using var file = await Copy(seed);
        await using var writer = OpenFile(file); await using var writeTransaction = await writer.Database.BeginTransactionAsync();
        await StageThird(writer); await writer.SaveChangesAsync();
        await using var read = OpenFile(file);
        var old = await Reader.ReadAsync(read, "scope", Chain, Codec, new("run")).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(new[] { "a", "b" }, Ids(old)); Assert.Equal(2, old.TotalCount); Assert.Equal(ConnectionState.Closed, read.Database.GetDbConnection().State);
        await writeTransaction.CommitAsync();
        var frozen = await Reader.ReadAsync(read, "scope", Chain, Codec, new("run", sequenceToken: old.SequenceToken));
        Assert.Equal(2, frozen.TotalCount); Assert.True(frozen.HasNewerHistory);
        Assert.Equal(new[] { "a", "b", "c" }, Ids(await Reader.ReadAsync(read, "scope", Chain, Codec, new("run"))));
    }

    [Fact]
    public async Task Normal_owned_occurrence_read_keeps_one_old_snapshot_across_a_concurrent_committed_append()
    {
        await using var seed = await Database.Create(); await seed.Append([Event(1, "a"), Event(2, "b")]); await using var file = await Copy(seed);
        var gate = new OwnedReadGate(); await using var context = OpenFile(file, gate);
        var pending = Reader.ReadAsync(context, "scope", Chain, Codec, new("run")).AsTask(); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using (var writer = OpenFile(file))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(); await StageThird(writer); await writer.SaveChangesAsync(); await transaction.CommitAsync();
        }
        gate.Release.SetResult(); var old = await pending.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(2, old.Head!.LastSequence); Assert.Equal(2, old.TotalCount); Assert.Equal(new[] { "a", "b" }, Ids(old));
        Assert.Null(context.Database.CurrentTransaction); Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Equal(3, (await Reader.ReadAsync(context, "scope", Chain, Codec, new("run"))).TotalCount);
    }

    [Fact]
    public async Task An_already_open_connection_remains_owned_by_its_caller_after_an_owned_read_transaction()
    {
        await using var db = await Database.Create(); await db.Append([Event(1, "a")]); await using var context = db.Open();
        var connection = context.Database.GetDbConnection(); Assert.Equal(ConnectionState.Open, connection.State);
        await Reader.ReadAsync(context, "scope", Chain, Codec, new("run"));
        Assert.Equal(ConnectionState.Open, connection.State); Assert.Null(context.Database.CurrentTransaction);
    }

    private static HistoryContext OpenFile(TemporarySqliteDatabase file, DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<HistoryContext>().UseSqlite(file.ConnectionString);
        if (interceptor is not null) options.AddInterceptors(interceptor); return new(options.Options);
    }
    private static async Task<TemporarySqliteDatabase> Copy(Database seed)
    {
        var file = new TemporarySqliteDatabase("occurrence-deferred-reader");
        try
        {
            await using var connection = new SqliteConnection(file.ConnectionString); await connection.OpenAsync(); seed.BackupTo(connection);
            await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA journal_mode=WAL"; await command.ExecuteNonQueryAsync(); return file;
        }
        catch { await file.DisposeAsync(); throw; }
    }
    private static async Task StageThird(HistoryContext context)
    {
        var mapping = new Mapping(Chain); var row = await context.Set<RuntimeHistoryHeadEntity>().SingleAsync(); var head = mapping.Read(row, "scope", "run");
        var next = mapping.Row("scope", head with { LastSequence = 3 }); next.Revision = row.Revision + 1; context.Entry(row).CurrentValues.SetValues(next);
        var item = Event(3, "c"); context.Add(mapping.Row("scope", item, new string('a', 64)));
        await EfRuntimeHistoryOccurrenceProjection.StageAsync(context, "scope", "run", Chain, [item]);
    }
    private sealed class OwnedReadGate : DbCommandInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool used;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken token = default)
        {
            if (!used && command.CommandText.Contains(RuntimeHistoryProjectionSchema.HeadTable, StringComparison.Ordinal))
            { used = true; Entered.SetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), token); }
            return result;
        }
    }
}

using System.Collections;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_older_valid_SQLite_snapshot_cannot_replace_a_newer_checked_gate_record(bool revisionOnly)
    {
        await using var fixture = await SharedMembershipConfirmationFixture.CreateAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await fixture.FinalizeElsewhereAsync(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        await fixture.Gate.ConfirmAsync(fixture.Context, RuntimeArtifactEfModule.SchemaFamily);
        await using var snapshotContext = fixture.OpenRuntime(fixture.Database.ConnectionString);
        await snapshotContext.Database.OpenConnectionAsync();
        await using var snapshot = ((SqliteConnection)snapshotContext.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await snapshotContext.Database.UseTransactionAsync(snapshot);
        var read = (await new EfSchemaFinalizationStore(snapshotContext).FindAsync(RuntimeArtifactEfModule.SchemaFamily))!;
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, read.FinalizedVersion);

        await using var peer = fixture.OpenRuntime(fixture.Database.ConnectionString);
        var peerStore = new EfSchemaFinalizationStore(peer);
        if (revisionOnly)
            await peerStore.PlaceHoldAsync(read.Family, read.Revision, null, "A later checked record at the same version", "ordering-control",
                RuntimeArtifactEfModule.Chain.ReadableVersions);
        else
            await EfSchemaFinalizationTestSupport.FinalizeAsync(peerStore, read.Family,
                RuntimeArtifactEfModule.Chain.ReadableVersions, RuntimeArtifactEfModule.PreparationSchemaVersion);
        // The real background-refresh method adopts through an independent context while A's
        // read-only WAL snapshot still has the older, otherwise valid family record.
        await fixture.Gate.RefreshAsync(peer);
        var observed = fixture.Gate.ObservedRecordOf(read.Family)!.Value.Record;
        Assert.True(observed.Revision > read.Revision);
        Assert.Equal(revisionOnly ? read.FinalizedVersion : RuntimeArtifactEfModule.PreparationSchemaVersion, observed.FinalizedVersion);
        Assert.Equal(read.Revision, (await new EfSchemaFinalizationStore(snapshotContext).FindAsync(read.Family))!.Revision);
        var current = ConfirmationOrderingSnapshot(fixture);
        var reports = fixture.Boundary.ReportSaves;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Gate.ConfirmAsync(snapshotContext, read.Family).AsTask());
        Assert.Equal(current, ConfirmationOrderingSnapshot(fixture));
        Assert.Equal(reports, fixture.Boundary.ReportSaves);
        await snapshot.RollbackAsync();

        // The refusal is specific to the stale read: a fresh checked record still confirms.
        Assert.Equal(observed.FinalizedVersion, (await fixture.Gate.ConfirmAsync(peer, read.Family)).WriteVersion);
        Assert.Equal(observed.Revision, fixture.Gate.ObservedRecordOf(read.Family)!.Value.Record.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_barrier_between_the_actual_family_read_and_adoption_cannot_regress_a_concurrent_refresh(bool rawHelper)
    {
        await using var fixture = await SharedMembershipConfirmationFixture.CreateAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        await fixture.FinalizeElsewhereAsync(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        var barrier = new ConfirmationFamilyReadBarrier();
        var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(fixture.Database.ConnectionString)
            .UseApplicationServiceProvider(fixture.Services).AddInterceptors(barrier);
        await using var caller = new RuntimeSqliteDbContext(builder.Options);
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        // This bound only prevents a broken hook from hanging the test. Correctness is asserted
        // from the deterministic read/disposal and explicit release, never an elapsed wait.
        Func<Task> start = rawHelper
            ? () => RuntimeSchemaWriteVersion.ConfirmArtifactUpdateAsync(caller, RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, bound.Token).AsTask()
            : () => fixture.Gate.ConfirmAsync(caller, RuntimeArtifactEfModule.SchemaFamily, bound.Token).AsTask();
        var confirmation = start();
        try
        {
            await barrier.ReadClosed.Task.WaitAsync(bound.Token);
            await using var peer = fixture.OpenRuntime(fixture.Database.ConnectionString);
            await EfSchemaFinalizationTestSupport.FinalizeAsync(new EfSchemaFinalizationStore(peer), RuntimeArtifactEfModule.SchemaFamily,
                RuntimeArtifactEfModule.Chain.ReadableVersions, RuntimeArtifactEfModule.PreparationSchemaVersion);
            await fixture.Gate.RefreshAsync(peer);
            Assert.Equal(RuntimeArtifactEfModule.PreparationSchemaVersion, fixture.Gate.StateOf(RuntimeArtifactEfModule.SchemaFamily)!.WriteVersion);
            var current = ConfirmationOrderingSnapshot(fixture);
            var reports = fixture.Boundary.ReportSaves;
            barrier.Release.TrySetResult(true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => confirmation);
            Assert.Equal(1, barrier.CapturedReads);
            Assert.Equal(current, ConfirmationOrderingSnapshot(fixture));
            Assert.Equal(reports, fixture.Boundary.ReportSaves);
        }
        finally
        {
            barrier.Release.TrySetResult(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmation_validates_ordinal_family_identity_even_when_the_provider_query_is_case_insensitive(bool wrongCase)
    {
        await using var fixture = await SharedMembershipConfirmationFixture.CreateAsync();
        const string family = RuntimeArtifactEfModule.SchemaFamily;
        var storedFamily = wrongCase ? family.ToLowerInvariant() : family;
        if (wrongCase)
            await fixture.Context.Database.ExecuteSqlRawAsync(
                $"UPDATE \"{EfSchemaFinalization.RecordTableName(RuntimeEfModule.HistoryModuleName)}\" SET \"Family\" = {{0}} WHERE \"Family\" = {{1}}",
                storedFamily, family);
        var insensitive = new CaseInsensitiveConfirmationFamilyQuery();
        var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(fixture.Database.ConnectionString)
            .UseApplicationServiceProvider(fixture.Services).AddInterceptors(insensitive);
        await using var caller = new RuntimeSqliteDbContext(builder.Options);
        // This proves the specimen actually crosses the query boundary; it does not rely on
        // default SQLite collation to stand in for a case-insensitive provider.
        var returned = (await new EfSchemaFinalizationStore(caller).FindAsync(family))!;
        Assert.Equal(storedFamily, returned.Family);
        Assert.True(insensitive.RewrittenQueries > 0);
        var current = ConfirmationOrderingSnapshot(fixture);
        var reports = fixture.Boundary.ReportSaves;
        if (wrongCase)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Gate.ConfirmAsync(caller, family).AsTask());
            Assert.Equal(current, ConfirmationOrderingSnapshot(fixture));
        }
        else
        {
            Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, (await fixture.Gate.ConfirmAsync(caller, family)).WriteVersion);
            Assert.Equal(family, fixture.Gate.ObservedRecordOf(family)!.Value.Record.Family);
        }
        Assert.True(insensitive.RewrittenQueries >= 2);
        Assert.Equal(reports, fixture.Boundary.ReportSaves);
    }

    private static string ConfirmationOrderingSnapshot(SharedMembershipConfirmationFixture fixture) => JsonSerializer.Serialize(
        fixture.Gate.Families.Chains.Select(chain => new
        {
            chain.Family, State = fixture.Gate.StateOf(chain.Family), Status = fixture.Gate.Observe(chain.Family),
            Record = fixture.Gate.ObservedRecordOf(chain.Family)?.Record,
            At = fixture.Gate.ObservedRecordOf(chain.Family)?.At,
            SharedObservation = fixture.Observations.Find(chain.Family)
        }).ToArray());

    private sealed class CaseInsensitiveConfirmationFamilyQuery : DbCommandInterceptor
    {
        public int RewrittenQueries { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(EfSchemaFinalization.RecordTableName(RuntimeEfModule.HistoryModuleName), StringComparison.Ordinal) &&
                command.CommandText.Contains("\"Family\" = ", StringComparison.Ordinal))
            {
                command.CommandText = command.CommandText.Replace("\"Family\" = ", "\"Family\" COLLATE NOCASE = ", StringComparison.Ordinal);
                RewrittenQueries++;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ConfirmationFamilyReadBarrier : DbCommandInterceptor
    {
        public TaskCompletionSource<bool> ReadClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CapturedReads { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (CapturedReads != 0 || !command.CommandText.Contains(
                    EfSchemaFinalization.RecordTableName(RuntimeEfModule.HistoryModuleName), StringComparison.Ordinal))
                return ValueTask.FromResult(result);
            CapturedReads++;
            // EF has to materialize the exact record and dispose this reader before FindAsync
            // returns. Close the underlying reader first so B can advance without a reader lock.
            return ValueTask.FromResult<DbDataReader>(new ConfirmationBarrierReader(result, async () =>
            {
                ReadClosed.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }));
        }
    }

    private sealed class ConfirmationBarrierReader(DbDataReader inner, Func<Task> afterClose) : DbDataReader
    {
        private bool closed;
        public override async ValueTask DisposeAsync()
        {
            if (closed) return;
            closed = true;
            await inner.DisposeAsync();
            await afterClose();
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override void Close() => inner.Close();
        public override bool Read() => inner.Read();
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => inner.ReadAsync(cancellationToken);
        public override bool NextResult() => inner.NextResult();
        public override Task<bool> NextResultAsync(CancellationToken cancellationToken) => inner.NextResultAsync(cancellationToken);
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override int VisibleFieldCount => inner.VisibleFieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override IEnumerator GetEnumerator() => ((IEnumerable)inner).GetEnumerator();
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override T GetFieldValue<T>(int ordinal) => inner.GetFieldValue<T>(ordinal);
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) => inner.GetFieldValueAsync<T>(ordinal, cancellationToken);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override DataTable? GetSchemaTable() => inner.GetSchemaTable();
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => inner.IsDBNullAsync(ordinal, cancellationToken);
    }
}

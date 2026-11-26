using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class PublishingLedgerReplaySchemaTests
{
    [Fact]
    public async Task A_held_predecessor_keeps_all_ledger_writers_usable_but_refuses_new_replay_evidence()
    {
        await using var fixture = await Fixture.Create();
        var record = EfPublicationRecordStoreTests.Record("legacy", "slot");
        var receipt = EfActivityPublicationReceiptStoreTests.Receipt("tenant-a", "publication");
        var run = EfActivityDraftTestRunStoreTests.Receipt("tenant-a", "draft", "test");
        await using (var context = fixture.Open())
        {
            var records = new EfPublicationRecordStore(context, Access);
            await records.SaveAsync(record);
            Assert.True(await records.TryTransitionAsync(record with { Status = PublicationStatus.Active }, PublicationStatus.Candidate));
            Assert.True(await new EfActivityPublicationReceiptStore(context, Access).TryCreateAsync(receipt));
            var runs = new EfActivityDraftTestRunStore(context, Access);
            Assert.True((await runs.TryCreateAsync(run)).Created);
            Assert.True(await runs.TryUpdateAsync(run with { Revision = run.Revision + 1 }, run.Revision));
            Assert.All(await context.PublicationRecords.AsNoTracking().ToListAsync(), row => Assert.Equal("1", row.SchemaVersion));
            Assert.All(await context.ActivityPublicationReceipts.AsNoTracking().ToListAsync(), row => Assert.Equal("1", row.SchemaVersion));
            Assert.All(await context.ActivityDraftTestRuns.AsNoTracking().ToListAsync(), row => Assert.Equal("1", row.SchemaVersion));
            await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => records.SaveAsync(record with
                { PublicationId = "new-evidence", RequestFingerprint = new string('a', 64) }).AsTask());
            Assert.Null(await records.FindAsync("new-evidence"));
        }
        await fixture.Finalize();
        await using (var context = fixture.Open())
        {
            var records = new EfPublicationRecordStore(context, Access);
            var legacy = (await records.FindAsync("legacy"))!;
            Assert.Null(legacy.RequestFingerprint);
            Assert.True(await records.TryTransitionAsync(legacy with { Status = PublicationStatus.Retired }, PublicationStatus.Active));
            Assert.NotNull(await new EfActivityPublicationReceiptStore(context, Access).FindAsync("tenant-a", receipt.IdempotencyKey));
            var runs = new EfActivityDraftTestRunStore(context, Access);
            var retained = (await runs.FindAsync(run.TestRunId))!;
            Assert.True(await runs.TryUpdateAsync(retained with { Revision = retained.Revision + 1 }, retained.Revision));
            await records.SaveAsync(record with { PublicationId = "new-evidence", RequestFingerprint = new string('a', 64) });
            Assert.Equal(new string('a', 64), (await records.FindAsync("new-evidence"))!.RequestFingerprint);
            Assert.All(await context.PublicationRecords.AsNoTracking().ToListAsync(), row => Assert.Equal("2", row.SchemaVersion));
            Assert.Equal("2", (await context.ActivityDraftTestRuns.AsNoTracking().SingleAsync()).SchemaVersion);
        }
    }

    [Fact]
    public async Task A_predecessor_stamp_cannot_assert_new_request_evidence()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Open();
        var record = EfPublicationRecordStoreTests.Record("forged", "slot");
        await new EfPublicationRecordStore(context, Access).SaveAsync(record);
        // Raw retained corruption, outside the store, must not become valid replay authority.
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_publication_records SET RequestFingerprint={EfRelationalIdentity.Encode(new string('a', 64))}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfPublicationRecordStore(context, Access).FindAsync("forged").AsTask());
    }

    [Fact]
    public async Task The_existing_baseline_upgrades_additively_without_losing_a_publication()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new PublishingSnapshotReviewSqliteDbContext(
            new DbContextOptionsBuilder<PublishingSnapshotReviewSqliteDbContext>().UseSqlite(connection).Options);
        var migrator = context.GetService<IMigrator>();
        const string baseline = "20260928040708_Initial";
        await migrator.MigrateAsync(baseline);
        Assert.Equal(new[] { baseline }, await context.Database.GetAppliedMigrationsAsync());
        var record = EfPublicationRecordStoreTests.Record("retained-before-upgrade", "slot");
        var values = new Dictionary<string, object?>
        {
            ["Id"] = EfPublishingStoreSupport.PhysicalId("tenant-a", record.PublicationId),
            ["PublicationId"] = EfRelationalIdentity.Encode(record.PublicationId),
            ["PublicationIdHash"] = EfPublishingStoreSupport.Hash(record.PublicationId),
            ["SlotId"] = EfRelationalIdentity.Encode(record.SlotId), ["SlotIdHash"] = EfPublishingStoreSupport.Hash(record.SlotId),
            ["SlotName"] = EfRelationalIdentity.Encode(record.SlotName),
            ["WorkflowDefinitionId"] = EfRelationalIdentity.Encode(record.WorkflowDefinitionId),
            ["WorkflowDefinitionVersionId"] = EfRelationalIdentity.Encode(record.WorkflowDefinitionVersionId),
            ["ArtifactId"] = EfRelationalIdentity.Encode(record.ArtifactId),
            ["SourceReferenceId"] = EfRelationalIdentity.Encode(record.SourceReferenceId!),
            ["ExpectedSlotRevision"] = record.ExpectedSlotRevision, ["Status"] = record.Status.ToString(),
            ["CreatedAtUtcTicks"] = record.CreatedAt.UtcTicks, ["CreatedAtOffsetMinutes"] = (int)record.CreatedAt.Offset.TotalMinutes,
            ["TenantId"] = EfRelationalIdentity.Encode("tenant-a"), ["TenantIdHash"] = EfPublishingStoreSupport.TenantHash("tenant-a"),
            ["SchemaVersion"] = "1", ["Revision"] = 1L
        };
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO elsa_publication_records (" + string.Join(",", values.Keys) + ") VALUES (" +
                string.Join(",", values.Keys.Select(key => "@" + key)) + ")";
            foreach (var value in values) command.Parameters.AddWithValue("@" + value.Key, value.Value ?? DBNull.Value);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
        await migrator.MigrateAsync();
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(record, await new EfPublicationRecordStore(context, Access).FindAsync(record.PublicationId));
        var next = record with { PublicationId = "new-request", RequestFingerprint = new string('d', 64) };
        await new EfPublicationRecordStore(context, Access).SaveAsync(next);
        Assert.Equal(next, await new EfPublicationRecordStore(context, Access).FindAsync(next.PublicationId));
    }

    [Fact]
    public void Old_publication_wire_omits_the_optional_fingerprint()
    {
        var record = EfPublicationRecordStoreTests.Record("legacy", "slot");
        Assert.DoesNotContain("RequestFingerprint", JsonSerializer.Serialize(record));
        Assert.Contains("RequestFingerprint", JsonSerializer.Serialize(record with { RequestFingerprint = new string('a', 64) }));
        Assert.Equal(new[] { "1", "2" }, PublishingLedgerEfModule.Chain.ReadableVersions);
        PublishingLedgerEfModule.Chain.EnsureSound();
    }

    private static IPersistenceAccessContextAccessor Access => new Accessor();
    private sealed class Accessor : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("tenant-a")); }
    private sealed class Fleet : IEfSchemaFleet
    {
        public EfSchemaFleetStanding GetLocalStanding() => new(new("host", "incarnation"), false);
        public ValueTask PublishAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public ValueTask<EfSchemaFleetAnswer> CountObservingAsync(string family, IReadOnlyList<string> versions, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public TimeSpan SettleMargin => TimeSpan.Zero;
        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly EfSchemaFinalizationGates gates = new();
        private readonly ServiceProvider services;
        private readonly EfSchemaModuleGate gate;
        private Fixture()
        {
            services = new ServiceCollection().AddSingleton(gates).BuildServiceProvider();
            gate = new(EfSchemaModuleFamilies.ForContext(typeof(PublishingSnapshotReviewDbContext))!, new Fleet(), new(), new());
        }
        public static async Task<Fixture> Create()
        {
            var fixture = new Fixture(); await fixture.connection.OpenAsync();
            await using var context = fixture.Open(); await context.Database.EnsureCreatedAsync();
            var store = new EfSchemaFinalizationStore(context);
            var readable = PublishingLedgerEfModule.Chain.ReadableVersions;
            var record = await store.GetOrCreateAsync(PublishingLedgerEfModule.SchemaFamily, "1", readable, SchemaFinalizationActor.OfOperator("test"));
            await store.PlaceHoldAsync(PublishingLedgerEfModule.SchemaFamily, record.Revision, null, "controlled rollout", "test", readable);
            await fixture.gate.ActivateAsync(context); fixture.gates.Register(typeof(PublishingSnapshotReviewDbContext), fixture.gate);
            return fixture;
        }
        public PublishingSnapshotReviewSqliteDbContext Open()
        {
            var options = new DbContextOptionsBuilder<PublishingSnapshotReviewSqliteDbContext>().UseSqlite(connection).UseApplicationServiceProvider(services);
            EfSchemaWriteGateInterceptor.EnsureAdded(options);
            return new(options.Options);
        }
        public async Task Finalize()
        {
            await using var context = Open(); var store = new EfSchemaFinalizationStore(context);
            var record = (await store.FindAsync(PublishingLedgerEfModule.SchemaFamily))!;
            await store.ReleaseHoldAsync(PublishingLedgerEfModule.SchemaFamily, record.Revision, null, "test");
            await gate.EvaluateAsync(context); await gate.RefreshAsync(context);
            Assert.Equal("2", gate.StateOf(PublishingLedgerEfModule.SchemaFamily)!.WriteVersion);
        }
        public async ValueTask DisposeAsync() { await services.DisposeAsync(); await connection.DisposeAsync(); }
    }
}

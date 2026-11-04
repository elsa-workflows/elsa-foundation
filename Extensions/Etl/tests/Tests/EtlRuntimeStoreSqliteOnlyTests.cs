using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Core;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Design;
using Elsa.Etl.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Http;
using Elsa.Etl.Runtime.Output;
using Elsa.Etl.Runtime.Preparation;
using Elsa.Etl.Runtime.Transformation;
using Elsa.Etl.Runtime;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;

namespace Elsa.Etl.Tests;

// Runtime-store cases only SQLite can stage: its catalog table, its begin-immediate writer lock, raw SQL in its dialect and
// steps through its retained migration chain.
public sealed partial class EtlRuntimeStoreTests
{
    [Fact]
    public async Task Runtime_module_migrates_only_its_tables_and_registers_no_design_services()
    {
        await using var database = await CreateSqliteDatabaseAsync();
        await using var context = database.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        var tables = await context.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToListAsync();
        Assert.Contains(EtlRuntimeEfModule.RunTableName, tables);
        Assert.Contains(EtlRuntimeEfModule.ManifestTableName, tables);
        Assert.DoesNotContain(EtlDesignEfModule.DraftTableName, tables);

        var services = new ServiceCollection();
        new EtlRuntimeEntityFrameworkCoreFeature().ConfigureServices(services);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IEtlRuntimeStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IEtlDefinitionStore));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IEtlArtifactCompiler));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_final_batch_acceptance_and_cancellation_serialize_real_transactions(bool acceptanceWins)
    {
        await using var database = await CreateSqliteDatabaseAsync();
        var scope = new EtlScope("tenant-a", "partition-overlap-race");
        var pipeline = EtlTestPipelines.Create() with
        {
            Limits = EtlTestPipelines.Create().Limits with { PartitionCount = 1, MaxBatchRecords = 2 }
        };
        var admission = CreateAdmission($"run-overlap-race-{acceptanceWins}", scope, pipeline);
        var clock = new MutableTimeProvider();
        var (lease, acceptance) = await SeedFinalBatchAsync(database, admission, clock);

        var acquiredTransaction = new AcquiredTransactionGate();
        var secondStartAttempt = new TransactionStartAttemptObserver();
        await using var firstContext = database.CreateContext(acquiredTransaction);
        await using var secondContext = database.CreateContext(secondStartAttempt);
        var firstStore = database.CreateStore(firstContext, clock);
        var secondStore = database.CreateStore(secondContext, clock);
        using var operationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        Task<EtlAcceptanceResult>? acceptanceTask = null;
        Task<EtlTerminalSettlementResult>? cancellationTask = null;
        if (acceptanceWins)
            acceptanceTask = Task.Run(async () => await firstStore.AcceptBatchAsync(lease, acceptance, operationTimeout.Token));
        else
            cancellationTask = Task.Run(async () => await firstStore.CancelAsync(admission.Invocation, operationTimeout.Token));

        Exception? synchronizationFailure = null;
        try
        {
            await acquiredTransaction.Acquired.WaitAsync(TimeSpan.FromSeconds(30));
            if (acceptanceWins)
                cancellationTask = Task.Run(async () => await secondStore.CancelAsync(admission.Invocation, operationTimeout.Token));
            else
                acceptanceTask = Task.Run(async () => await secondStore.AcceptBatchAsync(lease, acceptance, operationTimeout.Token));

            // The first SQLite transaction is already open and held at TransactionStartedAsync.
            // Observe the competing EF transaction-start path before allowing the first to commit.
            await secondStartAttempt.Attempted.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception exception)
        {
            synchronizationFailure = exception;
        }
        finally
        {
            acquiredTransaction.Release();
            var operations = new List<Task>(2);
            if (acceptanceTask is not null) operations.Add(acceptanceTask);
            if (cancellationTask is not null) operations.Add(cancellationTask);
            if (operations.Count > 0)
                await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(60));
        }

        if (synchronizationFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(synchronizationFailure).Throw();

        await AssertFinalBatchRaceStateAsync(database, admission, acceptance,
            await acceptanceTask!, await cancellationTask!, acceptanceWins, clock);
    }

    [Fact]
    public async Task Request_ledger_keys_cannot_be_rewritten_after_a_page_references_them()
    {
        await using var database = await CreateSqliteDatabaseAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var admission = ConnectorAdmission("immutable-request-ledger", new("tenant", "immutable-request-ledger"), httpShardIds: ["shard"]);
        var lease = Assert.Single(await StartHttpRunAsync(store, admission, "worker"));
        var originalRequestHash = EtlRuntimeIdentity.HttpRequestHash(Assert.IsType<EtlConnectorHttpPosition>(lease.Position).Request);
        var attached = await store.AttachTestHttpPageAsync(lease, RetainedPage(lease, "page-0", 10, 0, "cursor-1"));
        Assert.Equal(EtlStoreStatus.Applied, attached.Status);

        var request = await context.ConnectorHttpRequests.SingleAsync(row => row.PageSequence == 0);
        request.RequestHash = new string('d', 64);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        request = await context.ConnectorHttpRequests.AsNoTracking().SingleAsync(row => row.PageSequence == 0);
        var tableName = $"\"{EtlRuntimeEfModule.ConnectorHttpRequestTableName}\"";
        var update = $"UPDATE {tableName} SET \"RequestHash\" = {{0}} WHERE \"ScopeKey\" = {{1}} AND \"RunKey\" = {{2}} AND \"PartitionOrdinal\" = {{3}} AND \"PageSequence\" = {{4}}";
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlRawAsync(update,
            new string('d', 64), request.ScopeKey, request.RunKey, request.PartitionOrdinal, request.PageSequence));

        Assert.Equal(originalRequestHash,
            (await context.ConnectorHttpRequests.AsNoTracking().SingleAsync(row => row.PageSequence == 0)).RequestHash);
        Assert.Equal(originalRequestHash,
            (await context.ConnectorHttpPages.AsNoTracking().SingleAsync(page => page.PageSequence == 0)).RequestHash);
    }

    [Fact]
    public async Task A_csv_route_stored_with_the_http_mechanism_is_refused_on_read()
    {
        await using var fixture = await CsvOutputFixture.StartAsync(this, long.MaxValue / 4, Accepted(1, 1, "Ada"));
        Assert.True((await fixture.TransformAsync()).Succeeded);
        await fixture.Context.Database.ExecuteSqlRawAsync("UPDATE elsa_etl_connector_batches SET MainEffectKind = 1");

        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Inspector.ListConnectorPartitionsAsync(
            fixture.Admission.Invocation.Scope, fixture.Admission.Invocation.RunId, -1, 1));
    }

    [Fact]
    public async Task Connector_inspection_rejects_effect_kind_columns_that_disagree_with_retained_output()
    {
        await using var database = await CreateSqliteDatabaseAsync();
        await using var context = database.CreateContext();
        var store = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        var admission = ConnectorAdmission("inspection-corrupt-kind", new("tenant-a", "inspection-corrupt-kind"), httpShardIds: ["shard-a"]);
        var (_, batch, _) = await PrepareHttpOutputBatchAsync(store, admission, "worker");
        // The column holds the delivery mechanism (1 HTTP, 2 file) since CSV delivery; these values predate it unchanged.
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET MainEffectKind = {FileEffectMechanism} WHERE BatchId = {batch.BatchId}");

        var inspector = (IEtlConnectorRuntimeInspector)database.CreateStore(context);
        await Assert.ThrowsAsync<InvalidDataException>(() => inspector.ListConnectorPartitionsAsync(
            admission.Invocation.Scope, admission.Invocation.RunId, -1, 1).AsTask());
    }

    [Fact]
    public async Task Connector_inspection_effect_migration_backfills_route_kinds_without_rewriting_retained_output()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.Runtime.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var database = new EtlRuntimeTestDatabase(directory);
        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        var migrator = await SeedConnectorBatchThroughOutputsAsync(context);
        await migrator.MigrateAsync("20261021140000_ConnectorHttpDeliveryAttempts");

        const string batchId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        var descriptor = new EtlConnectorBatchDescriptor(batchId,
            new EtlConnectorHttpBatchRange(0, new string('b', 64), new string('d', 64), 0, 2), 12, new string('e', 64));
        var descriptorJson = EtlConnectorRecordCodec.Write(descriptor);
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET DescriptorJson = {descriptorJson}, DescriptorDigest = {EtlRuntimeRecordCodec.HashText(descriptorJson)} WHERE BatchId = {batchId}");
        await migrator.MigrateAsync("20261021150000_ConnectorBatchAcceptance");
        await migrator.MigrateAsync("20261021160000_ConnectorSourceFetch");

        var main = new EtlConnectorRouteOutput(EtlConnectorKind.Http,
            new EtlObjectReference("retained-main", EtlRuntimeRecordCodec.HashText("main"), 17), 1,
            EtlRuntimeRecordCodec.HashText("main-effect"));
        var errors = new EtlConnectorRouteOutput(EtlConnectorKind.Jsonl,
            new EtlObjectReference("retained-errors", EtlRuntimeRecordCodec.HashText("errors"), 19), 1,
            EtlRuntimeRecordCodec.HashText("error-effect"));
        var outputJson = EtlConnectorRecordCodec.Write(new EtlConnectorBatchOutput(1, 1, 0, main, errors));
        var outputDigest = EtlRuntimeRecordCodec.HashText(outputJson);
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_etl_connector_batches SET OutputJson = {outputJson}, OutputDigest = {outputDigest}, OutputBytes = 36, RecordCount = 2 WHERE BatchId = {batchId}");

        await migrator.MigrateAsync("20261021170000_ConnectorInspectionEffects");

        var mainKind = await context.Database.SqlQuery<int?>($"SELECT MainEffectKind AS Value FROM elsa_etl_connector_batches WHERE BatchId = {batchId}").SingleAsync();
        var errorKind = await context.Database.SqlQuery<int?>($"SELECT ErrorEffectKind AS Value FROM elsa_etl_connector_batches WHERE BatchId = {batchId}").SingleAsync();
        var migratedOutput = await context.Database.SqlQuery<string>($"SELECT OutputJson AS Value FROM elsa_etl_connector_batches WHERE BatchId = {batchId}").SingleAsync();
        var migratedDigest = await context.Database.SqlQuery<string>($"SELECT OutputDigest AS Value FROM elsa_etl_connector_batches WHERE BatchId = {batchId}").SingleAsync();
        var migratedBytes = await context.Database.SqlQuery<long>($"SELECT OutputBytes AS Value FROM elsa_etl_connector_batches WHERE BatchId = {batchId}").SingleAsync();
        var migratedSchema = await context.Database.SqlQuery<string>($"SELECT SchemaVersion AS Value FROM elsa_etl_connector_batches WHERE BatchId = {batchId}").SingleAsync();
        // The migration backfill wrote the delivery mechanism: HTTP delivery and JSONL file publication.
        Assert.Equal(HttpEffectMechanism, mainKind);
        Assert.Equal(FileEffectMechanism, errorKind);
        Assert.Equal(outputJson, migratedOutput);
        Assert.Equal(outputDigest, migratedDigest);
        Assert.Equal(36, migratedBytes);
        Assert.Equal(EtlRuntimeEfModule.SchemaVersion, migratedSchema);
    }

    [Fact]
    public async Task Connector_and_file_capabilities_share_the_same_scoped_provider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEtlRuntimeEntityFrameworkCore(new EtlRuntimeEntityFrameworkCoreOptions { ConnectionString = "Data Source=:memory:" });
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        Assert.Same(scope.ServiceProvider.GetRequiredService<IEtlRuntimeStore>(), scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>());
    }
}

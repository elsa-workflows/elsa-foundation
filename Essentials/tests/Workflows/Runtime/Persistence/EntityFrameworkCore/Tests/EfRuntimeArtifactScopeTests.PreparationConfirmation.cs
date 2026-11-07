using System.Data.Common;
using System.Text.Json;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.EntityFrameworkCore;
using Elsa.Cluster.Readability;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactScopeTests
{
    [Theory]
    [InlineData("1.1.0", "commit")]
    [InlineData("1.1.0", "rollback")]
    [InlineData("1.1.0", "unfinalized")]
    [InlineData("1.1.0", "cancelled")]
    [InlineData("1.2.0", "commit")]
    [InlineData("1.2.0", "rollback")]
    [InlineData("1.2.0", "unfinalized")]
    [InlineData("1.2.0", "cancelled")]
    public async Task Raw_confirmation_with_real_EF_membership_never_saves_a_report_inside_the_Runtime_transaction(string version, string outcome)
    {
        await using var fixture = await SharedMembershipConfirmationFixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        await fixture.Executables.SaveAsync(Executable("shared-membership-artifact"));
        var lease = Assert.IsType<WorkflowExecutableRootWriteLease>(await fixture.Executables.TryAcquireRootWriteLeaseAsync(
            "shared-membership-artifact", "shared-membership-lease", now.AddMinutes(5), now));
        // This mixed state is the causal case: fencing locks legacy coordination BEFORE seeing
        // the newer lease. A uniform newer fixture could refresh before taking a write lock.
        await fixture.Context.WorkflowExecutableRootWriteLeases.ExecuteUpdateAsync(update => update.SetProperty(row => row.SchemaVersion, version));
        fixture.Context.ChangeTracker.Clear();
        var coordination = await fixture.Context.WorkflowExecutableCoordinations.AsNoTracking().SingleAsync();
        var before = await fixture.Context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, coordination.SchemaVersion);
        if (outcome != "unfinalized") await fixture.FinalizeElsewhereAsync(version);
        var reports = fixture.Boundary.ReportSaves;
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Boundary.ObserveRuntimeTransaction = true;
            using var stop = new CancellationTokenSource();
            if (outcome == "cancelled")
            {
                // Hold the Runtime writer first, then cancel confirmation; cancellation must not
                // turn into publication on the membership connection or an adopted observation.
                await fixture.Context.WorkflowExecutableCoordinations.ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, row => row.Revision + 1));
                stop.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RuntimeSchemaWriteVersion.ConfirmArtifactUpdateAsync(
                    fixture.Context, version, stop.Token).AsTask());
            }
            else if (outcome == "unfinalized")
                await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => fixture.Executables.FenceRootWriteLeasesAsync(
                    [lease], TimeProvider.System, CancellationToken.None).AsTask());
            else
                Assert.Single(await fixture.Executables.FenceRootWriteLeasesAsync([lease], TimeProvider.System, CancellationToken.None));

            Assert.True(fixture.Boundary.RuntimeCoordinationWrites > 0);
            Assert.Empty(fixture.Boundary.ReportSavesDuringRuntimeTransaction);
            Assert.Equal(reports, fixture.Boundary.ReportSaves);
            Assert.Same(transaction, fixture.Context.Database.CurrentTransaction);
            Assert.Equal(outcome is "commit" or "rollback" ? version : RuntimeArtifactEfModule.SchemaVersion,
                fixture.Gate.StateOf(RuntimeArtifactEfModule.SchemaFamily)!.WriteVersion);
            if (outcome == "commit") await transaction.CommitAsync();
            else await transaction.RollbackAsync();
            fixture.Boundary.ObserveRuntimeTransaction = false;
        }
        var after = await fixture.Context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        var afterCoordination = await fixture.Context.WorkflowExecutableCoordinations.AsNoTracking().SingleAsync();
        Assert.Equal(before.Revision + (outcome == "commit" ? 1 : 0), after.Revision);
        Assert.Equal(coordination.Revision + (outcome == "commit" ? 1 : 0), afterCoordination.Revision);
        Assert.Equal(before.SchemaVersion, after.SchemaVersion);
        Assert.Equal(before.ExpiresAtUtcTicks, after.ExpiresAtUtcTicks);
        Assert.Equal(coordination.ContentJson, afterCoordination.ContentJson);
        if (outcome is "commit" or "rollback")
        {
            // Normal publication remains possible after disposal. This also proves the boundary
            // instrument observes a real EF report save, not a null/fake/no-op fleet.
            await fixture.Membership.PublishReportAsync();
            Assert.True(fixture.Boundary.ReportSaves > reports);
            Assert.Empty(fixture.Boundary.ReportSavesDuringRuntimeTransaction);
        }
    }

    [Theory]
    [InlineData("different-database")]
    [InlineData("wrong-gate")]
    [InlineData("wrong-module")]
    [InlineData("unadmitted")]
    [InlineData("no-registry")]
    [InlineData("missing-identity")]
    [InlineData("missing-record")]
    [InlineData("record-other-database")]
    [InlineData("unreadable-record")]
    [InlineData("refused-family")]
    [InlineData("unknown-family")]
    [InlineData("cancelled")]
    public async Task Public_confirmation_rejects_context_or_record_mismatches_without_changing_any_gate_state_or_observation(string defect)
    {
        await using var fixture = await SharedMembershipConfirmationFixture.CreateAsync();
        await using var otherDatabase = new TemporarySqliteDatabase("confirmation-unrelated");
        await using var otherContext = fixture.OpenRuntime(otherDatabase.ConnectionString);
        await using var noRegistry = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
            .UseSqlite(fixture.Database.ConnectionString).Options);
        await using var memberScope = fixture.Services.CreateAsyncScope();
        var membershipContext = memberScope.ServiceProvider.GetRequiredService<ClusterMembershipDbContext>();
        var gate = fixture.Gate;
        DbContext suppliedContext = fixture.Context;
        var family = RuntimeArtifactEfModule.SchemaFamily;
        var store = new EfSchemaFinalizationStore(fixture.Context);
        if (defect == "different-database")
        {
            await otherContext.Database.EnsureCreatedAsync();
            await new EfSchemaFinalizationStore(otherContext).GetOrCreateAsync(family, RuntimeArtifactEfModule.SchemaVersion,
                RuntimeArtifactEfModule.Chain.ReadableVersions, SchemaFinalizationActor.OfOperator("other-database"));
            Assert.NotEqual(gate.DatabaseIdentity, await new EfSchemaFinalizationStore(otherContext).FindDatabaseIdentityAsync());
            // Same CLR context type, same provider/registry and gate: persisted identity still differs.
            suppliedContext = otherContext;
        }
        if (defect is "wrong-gate" or "unadmitted")
        {
            gate = new EfSchemaModuleGate(fixture.Gate.Families, null, fixture.Observations, new EfSchemaFinalizationOptions());
            if (defect == "unadmitted") fixture.Registry.Register(typeof(RuntimeDbContext), gate);
        }
        if (defect == "wrong-module")
        {
            // Even an incorrect registry entry cannot turn a membership context into a Runtime context.
            fixture.Registry.Register(typeof(ClusterMembershipDbContext), gate);
            suppliedContext = membershipContext;
        }
        if (defect == "no-registry") suppliedContext = noRegistry;
        if (defect == "unknown-family") family = "not-an-owned-family";
        if (defect == "missing-identity") await fixture.Context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM \"{EfSchemaFinalization.DatabaseIdentityTableName(RuntimeEfModule.HistoryModuleName)}\"");
        if (defect == "missing-record") await fixture.Context.Database.ExecuteSqlRawAsync(
            $"DELETE FROM \"{EfSchemaFinalization.RecordTableName(RuntimeEfModule.HistoryModuleName)}\" WHERE \"Family\" = {{0}}", family);
        if (defect == "record-other-database") await fixture.Context.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{EfSchemaFinalization.RecordTableName(RuntimeEfModule.HistoryModuleName)}\" SET \"DatabaseIdentity\" = {{0}} WHERE \"Family\" = {{1}}", "other-record-identity", family);
        if (defect is "unreadable-record" or "refused-family")
        {
            await fixture.FinalizeElsewhereAsync("9.0.0");
            if (defect == "refused-family") await gate.RefreshAsync(fixture.Context);
        }
        var originalState = fixture.StateAndObservations();
        var suppliedState = gate.Observe().Select(status => JsonSerializer.Serialize(status)).ToArray();
        var reports = fixture.Boundary.ReportSaves;
        using var stop = new CancellationTokenSource();
        if (defect == "cancelled") stop.Cancel();
        var confirmation = () => gate.ConfirmAsync(suppliedContext, family, stop.Token).AsTask();
        if (defect == "cancelled") await Assert.ThrowsAnyAsync<OperationCanceledException>(confirmation);
        else if (defect is "refused-family" or "unreadable-record") await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(confirmation);
        else await Assert.ThrowsAsync<InvalidOperationException>(confirmation);
        Assert.Equal(originalState, fixture.StateAndObservations());
        Assert.Equal(suppliedState, gate.Observe().Select(status => JsonSerializer.Serialize(status)).ToArray());
        Assert.Equal(reports, fixture.Boundary.ReportSaves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Public_confirmation_adopts_only_the_single_verified_record_and_honors_cancellation_during_its_read(bool cancelDuringRead)
    {
        await using var fixture = await SharedMembershipConfirmationFixture.CreateAsync();
        await fixture.FinalizeElsewhereAsync(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        var before = fixture.StateAndObservations();
        var otherFamilies = fixture.Gate.Families.Chains.Where(chain => chain.Family != RuntimeArtifactEfModule.SchemaFamily)
            .Select(chain => JsonSerializer.Serialize(fixture.Gate.Observe(chain.Family))).ToArray();
        using var stop = new CancellationTokenSource();
        fixture.Boundary.CountFamilyReads = true;
        fixture.Boundary.CancelAfterFamilyRead = cancelDuringRead ? stop : null;
        if (cancelDuringRead)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Gate.ConfirmAsync(
                fixture.Context, RuntimeArtifactEfModule.SchemaFamily, stop.Token).AsTask());
            Assert.Equal(before, fixture.StateAndObservations());
        }
        else
        {
            var confirmed = await fixture.Gate.ConfirmAsync(fixture.Context, RuntimeArtifactEfModule.SchemaFamily, stop.Token);
            Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, confirmed.WriteVersion);
            Assert.Equal(otherFamilies, fixture.Gate.Families.Chains.Where(chain => chain.Family != RuntimeArtifactEfModule.SchemaFamily)
                .Select(chain => JsonSerializer.Serialize(fixture.Gate.Observe(chain.Family))).ToArray());
        }
        Assert.Equal(1, fixture.Boundary.FamilyReads);
        Assert.Empty(fixture.Boundary.ReportSavesDuringRuntimeTransaction);
    }

    private sealed class SharedMembershipConfirmationFixture : IAsyncDisposable
    {
        public TemporarySqliteDatabase Database { get; } = new("runtime-membership-confirmation");
        public ServiceProvider Services { get; private set; } = null!;
        public EfSchemaFinalizationGates Registry { get; } = new();
        public EfSchemaFinalizationObservations Observations { get; } = new();
        public RuntimeSqliteDbContext Context { get; private set; } = null!;
        public EfSchemaModuleGate Gate { get; private set; } = null!;
        public EfWorkflowExecutableStore Executables { get; private set; } = null!;
        public EfClusterMembership Membership { get; private set; } = null!;
        public ConfirmationReportBoundary Boundary { get; } = new();

        public static async Task<SharedMembershipConfirmationFixture> CreateAsync()
        {
            var fixture = new SharedMembershipConfirmationFixture();
            try
            {
                var services = new ServiceCollection().AddSingleton(fixture.Registry).AddSingleton(fixture.Observations);
                services.Configure<ClusterMembershipOptions>(options =>
                {
                    options.HostId = $"confirmation-{Guid.NewGuid():N}";
                    options.ExpiryPeriod = TimeSpan.FromMinutes(10);
                });
                services.AddEfClusterMembership(new EfClusterMembershipOptions { Provider = "Sqlite", Pooling = false,
                    ConnectionString = fixture.Database.ConnectionString, CleanupPeriod = TimeSpan.FromHours(1) });
                services.ConfigureDbContext<ClusterMembershipSqliteDbContext>((_, builder) => builder.AddInterceptors(fixture.Boundary));
                services.AddEfSchemaReadability();
                fixture.Services = services.BuildServiceProvider();
                fixture.Context = fixture.OpenRuntime(fixture.Database.ConnectionString);
                fixture.Boundary.Runtime = fixture.Context;
                // Keep Runtime's physical connection open while membership uses independently
                // scoped connections. Sharing a filename never means sharing a native connection.
                await fixture.Context.Database.OpenConnectionAsync();
                await fixture.Context.Database.EnsureCreatedAsync();
                await using (var scope = fixture.Services.CreateAsyncScope())
                {
                    var membership = scope.ServiceProvider.GetRequiredService<ClusterMembershipDbContext>();
                    await membership.Database.OpenConnectionAsync();
                    Assert.NotSame(fixture.Context.Database.GetDbConnection(), membership.Database.GetDbConnection());
                    Assert.NotSame(((SqliteConnection)fixture.Context.Database.GetDbConnection()).Handle,
                        ((SqliteConnection)membership.Database.GetDbConnection()).Handle);
                    // Fresh fixture mapping only. Both modules have their own finalization tables;
                    // this is not migration generation or old-schema upgrade qualification.
                    await membership.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
                }
                fixture.Membership = Assert.IsType<EfClusterMembership>(fixture.Services.GetRequiredService<IClusterMembership>());
                await fixture.Membership.JoinAsync();
                await fixture.Membership.ActivateAsync();
                var store = new EfSchemaFinalizationStore(fixture.Context);
                var record = await store.GetOrCreateAsync(RuntimeArtifactEfModule.SchemaFamily, RuntimeArtifactEfModule.SchemaVersion,
                    RuntimeArtifactEfModule.Chain.ReadableVersions, SchemaFinalizationActor.OfOperator("confirmation-controls"));
                await store.PlaceHoldAsync(record.Family, record.Revision, null, "Keep the writer at its predecessor", "confirmation-controls",
                    RuntimeArtifactEfModule.Chain.ReadableVersions);
                fixture.Gate = new(EfSchemaModuleFamilies.ForContext(typeof(RuntimeSqliteDbContext))!,
                    fixture.Services.GetRequiredService<IEfSchemaFleet>(), fixture.Observations, new EfSchemaFinalizationOptions());
                await fixture.Gate.ActivateAsync(fixture.Context);
                fixture.Registry.Register(typeof(RuntimeDbContext), fixture.Gate);
                Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, fixture.Gate.StateOf(RuntimeArtifactEfModule.SchemaFamily)!.WriteVersion);
                fixture.Executables = new(fixture.Context, new Accessor(PersistenceAccessContext.Scoped(new("tenant-a"))));
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public RuntimeSqliteDbContext OpenRuntime(string connectionString)
        {
            var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connectionString)
                .UseApplicationServiceProvider(Services).AddInterceptors(Boundary, Boundary.RuntimeCommands);
            EfSchemaWriteGateInterceptor.EnsureAdded(builder);
            return new(builder.Options);
        }

        public async Task FinalizeElsewhereAsync(string version)
        {
            // An independent context, not the stale writer's scheduled/full refresh.
            await using var peer = OpenRuntime(Database.ConnectionString);
            var store = new EfSchemaFinalizationStore(peer);
            var record = (await store.FindAsync(RuntimeArtifactEfModule.SchemaFamily))!;
            await store.ReleaseHoldAsync(record.Family, record.Revision, null, "confirmation-controls");
            await EfSchemaFinalizationTestSupport.FinalizeAsync(store, record.Family,
                [.. RuntimeArtifactEfModule.Chain.ReadableVersions, "9.0.0"], version);
            Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, Gate.StateOf(record.Family)!.WriteVersion);
        }

        public string StateAndObservations() => JsonSerializer.Serialize(Gate.Families.Chains.Select(chain => new
        {
            chain.Family, State = Gate.StateOf(chain.Family), Status = Gate.Observe(chain.Family), Observation = Observations.Find(chain.Family)
        }).ToArray());

        public async ValueTask DisposeAsync()
        {
            if (Context is not null) await Context.DisposeAsync();
            if (Services is not null) await Services.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed class ConfirmationReportBoundary : SaveChangesInterceptor
    {
        public RuntimeDbContext? Runtime { get; set; }
        public bool ObserveRuntimeTransaction { get; set; }
        public int ReportSaves { get; private set; }
        public int RuntimeCoordinationWrites => commands.CoordinationWrites;
        public bool CountFamilyReads { get => commands.CountFamilyReads; set => commands.CountFamilyReads = value; }
        public int FamilyReads => commands.FamilyReads;
        public CancellationTokenSource? CancelAfterFamilyRead { get => commands.CancelAfterFamilyRead; set => commands.CancelAfterFamilyRead = value; }
        public List<string> ReportSavesDuringRuntimeTransaction { get; } = [];
        private readonly ConfirmationRuntimeCommands commands = new();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ClusterMembershipDbContext membership)
            {
                membership.ChangeTracker.DetectChanges();
                if (membership.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Modified && entry.Property("ReportJson").IsModified))
                {
                    ReportSaves++;
                    Assert.NotSame(Runtime?.Database.GetDbConnection(), membership.Database.GetDbConnection());
                    if (ObserveRuntimeTransaction && Runtime?.Database.CurrentTransaction is not null)
                    {
                        ReportSavesDuringRuntimeTransaction.Add("real EF membership report-save boundary");
                        // Fail at the independent write boundary, before a provider lock timeout.
                        // Full Refresh's quiet-publication catch cannot hide the recorded attempt.
                        throw new InvalidOperationException("Membership attempted a report save during the Runtime transaction.");
                    }
                }
            }
            return ValueTask.FromResult(result);
        }

        public IInterceptor RuntimeCommands => commands;

        private sealed class ConfirmationRuntimeCommands : DbCommandInterceptor
        {
            public int CoordinationWrites { get; private set; }
            public bool CountFamilyReads { get; set; }
            public int FamilyReads { get; private set; }
            public CancellationTokenSource? CancelAfterFamilyRead { get; set; }
            public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
                DbDataReader result, CancellationToken cancellationToken = default)
            {
                if (CountFamilyReads && command.CommandText.Contains(EfSchemaFinalization.RecordTableName(RuntimeEfModule.HistoryModuleName), StringComparison.Ordinal))
                {
                    FamilyReads++;
                    CancelAfterFamilyRead?.Cancel();
                }
                return ValueTask.FromResult(result);
            }
            public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
                int result, CancellationToken cancellationToken = default)
            {
                if (result > 0 && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                    command.CommandText.Contains(RuntimeArtifactEfModule.WorkflowExecutableCoordinationTableName, StringComparison.Ordinal))
                    CoordinationWrites++;
                return ValueTask.FromResult(result);
            }
        }
    }
}

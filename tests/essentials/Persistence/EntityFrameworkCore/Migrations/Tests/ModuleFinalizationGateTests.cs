using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Studio.Preferences.Core;
using Elsa.Studio.Preferences.Core.Contracts;
using Elsa.Studio.Preferences.Core.Models;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// The finalization gate through real modules (spec 181, FR-009, FR-012 and FR-015): a module's own migrator admits it
/// after its migrations, a module whose family is finalized at a version this build cannot read is refused there and
/// writes nothing, and once admitted every store write passes the write check, which refuses the family's writes when a
/// refresh finds a finalized version this build cannot read. And for every module of every engine, each stamped table
/// belongs to exactly one of its module's families, so the check knows which write version a row must carry.
/// </summary>
public sealed class ModuleFinalizationGateTests : IAsyncLifetime
{
    private static readonly string[] NewerChain = [StudioPreferencesEfModule.SchemaVersion, "2.0.0"];
    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-module-gate-{Guid.NewGuid():N}.db");
    private ServiceProvider _host = null!;

    private string ConnectionString => $"Data Source={_file}";

    public Task InitializeAsync()
    {
        _host = Compose();
        return Task.CompletedTask;
    }

    private ServiceProvider Compose(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddStudioPreferencesEntityFrameworkCore(new StudioPreferencesEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = ConnectionString });
        services.AddEfModuleMigrations<StudioPreferencesDbContext>("Sqlite");
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _file, _file + "-journal", _file + "-wal", _file + "-shm" })
            File.Delete(file);
    }

    [Fact]
    public async Task The_migrator_admits_a_module_on_a_fresh_database_creating_its_records_and_its_store_writes_through_the_check()
    {
        await Migrator.StartAsync(CancellationToken.None);

        var gate = _host.GetRequiredService<EfSchemaFinalizationGates>().FindModuleGate(typeof(StudioPreferencesSqliteDbContext));
        Assert.Same(Migrator.Gate, gate);
        Assert.Equal(StudioPreferencesEfModule.SchemaVersion, (await RecordAsync())!.FinalizedVersion);
        Assert.Equal(StudioPreferencesEfModule.SchemaVersion, gate!.StateOf(StudioPreferencesEfModule.SchemaFamily)!.WriteVersion);
        Assert.Equal(StudioPreferenceStoreWriteStatus.Saved, (await WriteAsync("first")).Status);
    }

    /// <summary>
    /// Spec 182, FR-014 and FR-022: the migrator gives the gate it registers a way to open its module's contexts, so the
    /// shared dormancy check can have the gate refresh, or read its status, when nothing else hands it a context.
    /// </summary>
    [Fact]
    public async Task The_migrator_lets_its_gate_refresh_and_read_its_status_on_demand()
    {
        await Migrator.StartAsync(CancellationToken.None);

        Assert.True(await Migrator.Gate!.RefreshIfOlderThanAsync(TimeSpan.Zero));
        Assert.Contains(await Migrator.Gate.ReadStatusAsync(), status => status.Family == StudioPreferencesEfModule.SchemaFamily && status.WriteVersion == StudioPreferencesEfModule.SchemaVersion);
    }

    /// <summary>
    /// CShells resolves a shell's initializers in a scope it disposes once they have run (#2143). The migrator it
    /// initialized there outlives that scope, so its gate keeps refreshing: a family finalized afterwards at a version this
    /// build cannot read refuses the module's writes with no restart (spec 181, FR-012). Disposed with that scope, the
    /// gate stopped right after activation and the module kept writing a version the fleet had left behind, with nothing
    /// logged: the direction that looks like success.
    /// </summary>
    [Fact]
    public async Task The_gate_admitted_in_a_shells_initializer_scope_keeps_refreshing_after_that_scope_ends()
    {
        await _host.DisposeAsync();
        _host = Compose(services => services.Configure<EfSchemaFinalizationOptions>(options => options.RefreshInterval = TimeSpan.FromMilliseconds(50)));
        await using (var scope = _host.CreateAsyncScope())
            await Assert.Single(scope.ServiceProvider.GetServices<IShellInitializer>().OfType<EfModuleMigrator<StudioPreferencesDbContext>>()).InitializeAsync();

        await FinalizeNewerAsync();

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (Migrator.Gate!.StateOf(StudioPreferencesEfModule.SchemaFamily) is not { WritesRefused: true })
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "The gate did not refresh after the initializer scope ended.");
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => WriteAsync("refused").AsTask());
    }

    [Fact]
    public async Task Once_a_refresh_finds_a_finalized_version_this_build_cannot_read_every_store_write_is_refused()
    {
        await Migrator.StartAsync(CancellationToken.None);
        await FinalizeNewerAsync();

        await using (var scope = _host.CreateAsyncScope())
            await Migrator.Gate!.RefreshAsync(scope.ServiceProvider.GetRequiredService<StudioPreferencesDbContext>());

        var refusal = await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => WriteAsync("refused").AsTask());
        Assert.Equal((StudioPreferencesEfModule.SchemaFamily, "2.0.0"), (refusal.Family, refusal.RequiredVersion));
        Assert.Equal(EfSchemaWriteRefusedException.RefusalCode, refusal.Code);
        await using var reading = _host.CreateAsyncScope();
        Assert.Empty(await reading.ServiceProvider.GetRequiredService<StudioPreferencesDbContext>().Set<Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.Entities.StudioPreferenceRecord>().ToListAsync());
    }

    [Fact]
    public async Task A_module_whose_family_is_finalized_at_a_version_this_build_cannot_read_is_refused_by_its_migrator_and_changes_nothing()
    {
        await using (var scope = _host.CreateAsyncScope())
            await EfDatabaseMigrator.ApplyAsync(scope.ServiceProvider.GetRequiredService<StudioPreferencesDbContext>(), EfRelationalProviderBinding.ExpectedProviderName("Sqlite"));
        await FinalizeNewerAsync();
        var before = await SnapshotAsync();

        var refusal = await Assert.ThrowsAsync<EfSchemaActivationRefusedException>(() => Migrator.InitializeAsync());

        Assert.Equal((EfSchemaActivationRefusal.FinalizedUnreadable, "Studio.Preferences", "2.0.0"), (refusal.Refusal, refusal.Module, refusal.Version));
        Assert.Null(Migrator.Gate);
        Assert.Null(_host.GetRequiredService<EfSchemaFinalizationGates>().FindForContext(typeof(StudioPreferencesSqliteDbContext)));
        Assert.Equal(before, await SnapshotAsync());
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_stamped_table_of_every_module_belongs_to_exactly_one_family_of_its_module_and_every_family_stamps_one(string provider)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
            var families = EfSchemaModuleFamilies.ForContext(type) ?? throw new InvalidOperationException($"{type.Name} belongs to no EF module.");
            var stamped = context.Model.GetEntityTypes().Where(EfSchemaModuleFamilies.IsStamped).ToArray();

            Assert.NotEmpty(families.Chains);
            foreach (var entity in stamped)
                Assert.True(families.FamilyOf(entity.ClrType) is not null,
                    $"{type.Name}: '{entity.ClrType.Name}' belongs to no single family of '{families.Module}'.");
            foreach (var chain in families.Chains)
                Assert.True(stamped.Any(entity => families.FamilyOf(entity.ClrType) == chain),
                    $"{type.Name}: family '{chain.Family}' of '{families.Module}' claims no stamped table.");
        }
    }

    public static TheoryData<string> Providers() => new(ModuleContextCatalog.Providers);

    private EfModuleMigrator<StudioPreferencesDbContext> Migrator => _host.GetRequiredService<EfModuleMigrator<StudioPreferencesDbContext>>();

    private async Task<SchemaFinalizationRecord?> RecordAsync()
    {
        await using var scope = _host.CreateAsyncScope();
        return await new EfSchemaFinalizationStore(scope.ServiceProvider.GetRequiredService<StudioPreferencesDbContext>()).FindAsync(StudioPreferencesEfModule.SchemaFamily);
    }

    /// <summary>A newer build, reading 1.0.0 and 2.0.0, finalizes 2.0.0 in this database.</summary>
    private async Task FinalizeNewerAsync()
    {
        await using var scope = _host.CreateAsyncScope();
        await EfSchemaFinalizationTestSupport.FinalizeAsync(
            new EfSchemaFinalizationStore(scope.ServiceProvider.GetRequiredService<StudioPreferencesDbContext>()),
            StudioPreferencesEfModule.SchemaFamily, NewerChain, "2.0.0");
    }

    /// <summary>Every row of every table, as text. The file's bytes are no measure: EF's own migration lock writes to it.</summary>
    private async Task<string> SnapshotAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        var rows = new List<string>();
        foreach (var table in tables)
        {
            await using var select = connection.CreateCommand();
            select.CommandText = $"SELECT * FROM \"{table}\"";
            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                rows.Add(table + ":" + string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetValue(index)?.ToString())));
        }

        return string.Join("\n", rows.Order(StringComparer.Ordinal));
    }

    private async ValueTask<StudioPreferenceStoreWriteResult> WriteAsync(string subject)
    {
        await using var scope = _host.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IStudioPreferenceStore>().WriteAsync(
            new StudioPreferenceKey(subject, "tenant", "host", StudioPreferenceNamespaces.Dashboard),
            new StudioPreferenceWrite(1, JsonDocument.Parse("{}").RootElement),
            StudioPreferenceWriteCondition.MustNotExist,
            DateTimeOffset.UnixEpoch);
    }
}

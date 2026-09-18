using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

public sealed class ModuleMigrationTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"elsa-ef-migrations-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    public static TheoryData<string> Providers() => [.. ModuleContextCatalog.Providers];

    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_module_context_has_migrations_that_match_its_model(string provider)
    {
        var contexts = ModuleContextCatalog.Contexts(provider);
        Assert.NotEmpty(contexts);
        foreach (var type in contexts)
        {
            // Building the model never opens this placeholder connection.
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
            Assert.True(context.Database.GetMigrations().Any(), $"{type.Name} has no migrations. Run tools/ef/generate-module-migrations.sh.");
            Assert.False(context.Database.HasPendingModelChanges(), $"{type.Name} has model changes without a migration. Run tools/ef/generate-module-migrations.sh.");
        }
    }

    /// <summary>
    /// A value-converted collection without a value comparer is compared by reference, so EF never sees an in-place
    /// change and silently skips the write; EF reports it only as warning 10620. Every module model must build with
    /// that warning raised as an error.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_module_context_gives_converted_collections_a_value_comparer(string provider)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider))
        {
            using var context = ModuleContextCatalog.Create(
                type,
                ModuleContextCatalog.PlaceholderConnection(provider),
                options => options.ConfigureWarnings(warnings => warnings.Throw(CoreEventId.CollectionWithoutComparer)));
            var failure = Record.Exception(() => context.Model);
            Assert.True(failure is null, $"{type.Name}: {failure?.Message}");
        }
    }

    [Fact]
    public void Every_module_derives_one_context_per_provider()
    {
        var bases = ModuleContextCatalog.AllContexts().GroupBy(type => type.BaseType!);
        Assert.All(bases, group => Assert.Equal(ModuleContextCatalog.Providers.Order(), group.Select(ModuleContextCatalog.ProviderOf).Order()));
    }

    [Fact]
    public async Task Every_sqlite_module_installs_into_one_fresh_database_with_its_own_history()
    {
        await ModuleContextCatalog.InstallAllAsync("Sqlite", ConnectionString);
    }

    [Fact]
    public async Task Validate_refuses_a_database_with_pending_migrations_until_they_are_applied()
    {
        await using var context = ModuleContextCatalog.Create(typeof(WorkflowsDesignSqliteDbContext), ConnectionString);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite, EfMigratePolicy.Validate));
        await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite, EfMigratePolicy.AutoMigrate);
        await EfDatabaseMigrator.ApplyAsync(context, EfProviderNames.Sqlite, EfMigratePolicy.Validate);
    }

    [Fact]
    public async Task Shell_initialization_applies_a_module_through_its_registered_migrator()
    {
        var services = new ServiceCollection();
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = ConnectionString });
        services.AddEfModuleMigrations<ActivitiesDesignDbContext>("Sqlite");
        services.AddEfModuleMigrations<ActivitiesDesignDbContext>("Sqlite");
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // The binding validator initializes alongside the migrator; repeat registration still keeps one migrator.
        var initializer = Assert.Single(provider.GetServices<IShellInitializer>().OfType<EfModuleMigrator<ActivitiesDesignDbContext>>());
        await initializer.InitializeAsync();

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ActivitiesDesignDbContext>();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await context.ActivityDefinitions.CountAsync());
    }

    [Fact]
    public async Task Validate_mode_stops_shell_initialization_before_a_stale_module_is_used()
    {
        var services = new ServiceCollection();
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = ConnectionString });
        services.AddEfModuleMigrations<ActivitiesDesignDbContext>("Sqlite");
        services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.Validate);
        await using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<IShellInitializer>().InitializeAsync());
    }

    [Fact]
    public void Migrations_are_registered_in_the_prepare_phase_ahead_of_shell_tasks()
    {
        var services = new ServiceCollection();
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = ConnectionString });
        services.AddEfModuleMigrations<ActivitiesDesignDbContext>("Sqlite");

        var registration = Assert.Single(services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ShellInitializerRegistration>()
            .Where(candidate => candidate.InitializerType == typeof(EfModuleMigrator<ActivitiesDesignDbContext>)));

        // CShells runs initializers by phase. Shell tasks and seeders register at Start, so a module's schema
        // must be applied in an earlier phase or they read tables that do not exist yet.
        Assert.Equal(LifecyclePhase.Prepare, registration.Phase);
        Assert.True(registration.Phase < LifecyclePhase.Start);
        Assert.Equal(0, registration.Order);
    }

    [Fact]
    public async Task A_migrator_left_behind_by_a_backend_switch_does_nothing()
    {
        var services = new ServiceCollection();
        services.AddEfModuleMigrations<ActivitiesDesignDbContext>("Sqlite");
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IShellInitializer>().InitializeAsync();
        Assert.False(File.Exists(databasePath));
    }

    public void Dispose()
    {
        if (File.Exists(databasePath))
            File.Delete(databasePath);
    }

    private sealed class ReadsActivitiesOnInitialization(IServiceScopeFactory scopes) : IShellInitializer
    {
        public int? Count { get; private set; }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await using var scope = scopes.CreateAsyncScope();
            Count = await scope.ServiceProvider.GetRequiredService<ActivitiesDesignDbContext>().ActivityDefinitions.CountAsync(cancellationToken);
        }
    }
}

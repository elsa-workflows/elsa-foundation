using Elsa.Jobs.Core;
using Elsa.Jobs.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Elsa.Jobs.Persistence.ProviderTests;

public sealed class JobsPostgreSqlFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    internal string? ConnectionString { get; private set; }
    internal string? Unavailable { get; private set; }
    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("jobs_tests").WithUsername("postgres").WithPassword("postgres").Build();
            await _container.StartAsync(); ConnectionString = _container.GetConnectionString();
        }
        catch (Exception error) { Unavailable = $"Owned PostgreSQL test container unavailable ({error.GetType().Name})."; }
    }
    public async Task DisposeAsync() { if (_container is not null) await _container.DisposeAsync(); }
}

[CollectionDefinition("jobs-postgresql")]
public sealed class JobsPostgreSqlCollection : ICollectionFixture<JobsPostgreSqlFixture> { }

internal sealed class JobsProviderDatabase : IAsyncDisposable
{
    private readonly string _directory;
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    internal IJobCommands Commands { get; }
    internal IJobQueries Queries { get; }
    internal IDbContextFactory<JobsDbContext> Contexts { get; }
    private JobsProviderDatabase(string directory, ServiceProvider services, AsyncServiceScope scope)
    {
        _directory = directory; _services = services; _scope = scope;
        Commands = scope.ServiceProvider.GetRequiredService<IJobCommands>(); Queries = scope.ServiceProvider.GetRequiredService<IJobQueries>();
        Contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JobsDbContext>>();
    }
    internal static async Task<JobsProviderDatabase> Create(string provider, string? connectionString)
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-jobs-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IJobRecurrenceCalculator>(new IntervalCalculator());
        services.AddJobsEntityFrameworkCore(options =>
        {
            options.Provider = provider; options.ConnectionString = provider == "Sqlite" ? "Data Source=" + Path.Combine(directory, "jobs.db") : connectionString;
            options.Schema = provider == "PostgreSql" ? "jobs_" + Guid.NewGuid().ToString("N") : null;
            options.MaximumClaimScan = 3;
        });
        // Every engine case deliberately owns a different database/schema. Crossing EF's global
        // provider-cache warning threshold is expected for this fixture, not a product configuration.
        services.ConfigureDbContext<JobsSqliteDbContext>(options => options.ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.ConfigureDbContext<JobsPostgreSqlDbContext>(options => options.ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        var container = services.BuildServiceProvider(); var scope = container.CreateAsyncScope();
        var fixture = new JobsProviderDatabase(directory, container, scope);
        await using var db = await fixture.Contexts.CreateDbContextAsync();
        await EfDatabaseMigrator.ApplyAsync(db, EfRelationalProviderBinding.ExpectedProviderName(provider), EfMigratePolicy.AutoMigrate);
        await EfDatabaseMigrator.ApplyAsync(db, EfRelationalProviderBinding.ExpectedProviderName(provider), EfMigratePolicy.Validate);
        Assert.NotEmpty(db.Database.GetMigrations());
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        return fixture;
    }
    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync(); await _services.DisposeAsync();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
    internal sealed class IntervalCalculator : IJobRecurrenceCalculator
    {
        public DateTimeOffset? Next(JobRecurrence recurrence, DateTimeOffset after) =>
            after + TimeSpan.Parse(recurrence.Expression, System.Globalization.CultureInfo.InvariantCulture);
    }
}

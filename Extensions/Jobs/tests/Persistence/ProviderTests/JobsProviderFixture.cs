using System.Text.Json;
using System.Text.RegularExpressions;
using Elsa.Jobs;
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
    private readonly string _ownershipRun = Guid.NewGuid().ToString("N");
    private Dictionary<string, string>? _ownershipLabels;
    internal string? ConnectionString { get; private set; }
    internal string? Unavailable { get; private set; }
    public async Task InitializeAsync()
    {
        try
        {
            var owner = Environment.GetEnvironmentVariable("ELSA_JOBS_QUALIFICATION_OWNER") ?? "local-elsa-3586";
            if (!Regex.IsMatch(owner, "^[A-Za-z0-9_.-]{1,128}$")) throw new InvalidOperationException("The Jobs qualification owner label is invalid.");
            _ownershipLabels = new()
            {
                ["elsa.qualification.namespace"] = "background-workflows",
                ["elsa.qualification.program"] = "3586",
                ["elsa.qualification.owner"] = owner,
                ["elsa.qualification.run"] = _ownershipRun
            };
            var builder = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("jobs_tests").WithUsername("postgres").WithPassword("postgres");
            foreach (var label in _ownershipLabels) builder = builder.WithLabel(label.Key, label.Value);
            _container = builder.Build();
            try { await _container.StartAsync(); }
            finally { RecordOwnership(); }
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception error) { Unavailable = $"Owned PostgreSQL test container unavailable ({error.GetType().Name})."; }
    }
    public async Task DisposeAsync() { if (_container is not null) await _container.DisposeAsync(); }

    private void RecordOwnership()
    {
        var directory = Environment.GetEnvironmentVariable("ELSA_JOBS_OWNERSHIP_RECEIPT_DIR");
        if (string.IsNullOrWhiteSpace(directory) || _container is null) return;
        string id;
        try { id = _container.Id; }
        catch (InvalidOperationException) { return; } // Creation did not return an ID; exact owner labels remain the cleanup fallback.
        if (string.IsNullOrWhiteSpace(id)) return;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "owned-container-" + _ownershipRun + ".json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { id, labels = _ownershipLabels, image = "postgres:16-alpine", source = nameof(JobsPostgreSqlFixture) }));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, overwrite: true);
    }
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
    internal IJobRecurrenceCalculator Recurrence => _scope.ServiceProvider.GetRequiredService<IJobRecurrenceCalculator>();
    private JobsProviderDatabase(string directory, ServiceProvider services, AsyncServiceScope scope)
    {
        _directory = directory; _services = services; _scope = scope;
        Commands = scope.ServiceProvider.GetRequiredService<IJobCommands>(); Queries = scope.ServiceProvider.GetRequiredService<IJobQueries>();
        Contexts = scope.ServiceProvider.GetRequiredService<IDbContextFactory<JobsDbContext>>();
    }
    internal static async Task<JobsProviderDatabase> Create(string provider, string? connectionString, bool defaultArithmetic = false)
    {
        var directory = Path.Combine(Path.GetTempPath(), "elsa-jobs-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var services = new ServiceCollection(); services.AddLogging();
        if (defaultArithmetic) services.AddElsaJobs();
        else services.AddSingleton<IJobRecurrenceCalculator>(new IntervalCalculator());
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

using Elsa.Persistence.Spike.VariantB;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using FluentMigrator.Runner.Processors;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Spike.VariantB.Host;

public enum SpikeSqlProvider
{
    Sqlite,
    PostgreSql
}

public static class FluentMigratorApply
{
    public static void Apply(string connectionString, SpikeSqlProvider provider)
    {
        using var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(runner =>
            {
                _ = provider switch
                {
                    SpikeSqlProvider.Sqlite => runner.AddSQLite(),
                    SpikeSqlProvider.PostgreSql => runner.AddPostgres(),
                    _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
                };

                runner
                    .WithGlobalConnectionString(connectionString)
                    .ScanIn(typeof(SecretsDbContext).Assembly).For.Migrations()
                    .ScanIn(typeof(SecretsVersionTable).Assembly).For.VersionTableMetaData();
            })
            .Configure<RunnerOptions>(options => options.AllowBreakingChange = true)
            .BuildServiceProvider(validateScopes: false);

        using var scope = services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IProcessorAccessor>().Processor;
        var expected = provider switch
        {
            SpikeSqlProvider.Sqlite => "SQLite",
            SpikeSqlProvider.PostgreSql => "PostgreSQL",
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

        if (!string.Equals(processor.DatabaseType, expected, StringComparison.OrdinalIgnoreCase) &&
            !processor.DatabaseType.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"FluentMigrator processor is '{processor.DatabaseType}', expected '{expected}'.");
        }

        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}

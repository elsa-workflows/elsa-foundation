#pragma warning disable EF1001 // design-time scaffolding services are internal; documented as a spike footgun.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Spike.VariantB.Host;

/// <summary>
/// CI-style drift check after FluentMigrator applies schema.
/// <para>
/// Exact APIs:
/// <list type="bullet">
/// <item><see cref="IMigrationsModelDiffer.GetDifferences"/> — runtime EF service, produces the operations the current compiled model expects versus an empty source.</item>
/// <item><see cref="IDatabaseModelFactory.Create(System.Data.Common.DbConnection, DatabaseModelFactoryOptions)"/> — reverse-engineers the live database (registered by the provider's <c>IDesignTimeServices</c>).</item>
/// </list>
/// A match means every <see cref="CreateTableOperation"/> / column from the EF model exists on the live database
/// (history/version tables excluded). Extra database tables are ignored so FM's version table is not drift.
/// </para>
/// </summary>
public static class EfModelDrift
{
    public static IReadOnlyList<string> GetDifferences(DbContext context)
    {
        var differ = context.GetService<IMigrationsModelDiffer>();
        var current = context.GetService<IDesignTimeModel>().Model;
        var expected = differ.GetDifferences(source: null, target: current.GetRelationalModel());

        var databaseModel = ReverseEngineer(context);
        var liveTables = databaseModel.Tables.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
        var differences = new List<string>();

        foreach (var create in expected.OfType<CreateTableOperation>())
        {
            if (!liveTables.TryGetValue(create.Name, out var live))
            {
                differences.Add($"MissingTable:{create.Name}");
                continue;
            }

            foreach (var column in create.Columns)
            {
                if (live.Columns.All(c => !string.Equals(c.Name, column.Name, StringComparison.OrdinalIgnoreCase)))
                    differences.Add($"MissingColumn:{create.Name}.{column.Name}");
            }
        }

        return differences;
    }

    private static DatabaseModel ReverseEngineer(DbContext context)
    {
        var factory = ResolveDatabaseModelFactory(context);
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
            connection.Open();

        try
        {
            return factory.Create(connection, new DatabaseModelFactoryOptions());
        }
        finally
        {
            if (openedHere)
                connection.Close();
        }
    }

    private static IDatabaseModelFactory ResolveDatabaseModelFactory(DbContext context)
    {
        try
        {
            var existing = context.GetService<IDatabaseModelFactory>();
            if (existing is not null)
                return existing;
        }
        catch (InvalidOperationException)
        {
            // Design-time scaffolding is not in the runtime provider by default.
        }

        var services = new ServiceCollection();
        ConfigureDesignTime(context, services);
        return services.BuildServiceProvider().GetRequiredService<IDatabaseModelFactory>();
    }

    private static void ConfigureDesignTime(DbContext context, IServiceCollection services)
    {
        var providerName = context.Database.ProviderName ?? "";
        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            new Microsoft.EntityFrameworkCore.Sqlite.Design.Internal.SqliteDesignTimeServices()
                .ConfigureDesignTimeServices(services);
            return;
        }

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            new Npgsql.EntityFrameworkCore.PostgreSQL.Design.Internal.NpgsqlDesignTimeServices()
                .ConfigureDesignTimeServices(services);
            return;
        }

        throw new NotSupportedException($"No design-time scaffolding services for '{providerName}'.");
    }
}

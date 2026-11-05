using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// Captures are observed before the standard host migrates its own copy. Keep the original read-only: the optional
// claim-delivery projection did not exist in those stores, and ReadChecked already accepts null for a legacy row.
internal static class ReadOnlyRuntimeSnapshot
{
    internal static async Task ConfigureServicesAsync(IServiceCollection services, string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        if (!await HasClaimDeliveryProjectionAsync(connection))
            services.ConfigureDbContext<RuntimeSqliteDbContext>((_, options) =>
                options.ReplaceService<IModelCustomizer, LegacyOutboxModelCustomizer>());
    }

    internal static async Task<DbContextOptions<RuntimeSqliteDbContext>> OptionsAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection);
        if (!await HasClaimDeliveryProjectionAsync(connection))
            options.ReplaceService<IModelCustomizer, LegacyOutboxModelCustomizer>();
        return options.Options;
    }

    private static async Task<bool> HasClaimDeliveryProjectionAsync(SqliteConnection connection)
    {
        if (new SqliteConnectionStringBuilder(connection.ConnectionString).Mode != SqliteOpenMode.ReadOnly)
            throw new InvalidOperationException("A captured runtime observer requires a read-only connection.");
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{RuntimePostCommitOutboxEfModule.TableName}\")";
        await using var columns = await command.ExecuteReaderAsync();
        var foundTable = false;
        while (await columns.ReadAsync())
        {
            foundTable = true;
            if (columns.GetString(1) == nameof(RuntimePostCommitOutboxEntity.ClaimDeliveredAtUtcTicks))
                return true;
        }
        if (!foundTable)
            throw new InvalidDataException("The snapshot has no runtime outbox table.");
        return false;
    }
}

// A distinct EF service configuration keeps the legacy model separate from the current model cache. All existing
// provider mappings, payload conversion and checked reads remain composed by the ordinary RuntimeSqliteDbContext.
internal sealed class LegacyOutboxModelCustomizer : RelationalModelCustomizer
{
    public LegacyOutboxModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<RuntimePostCommitOutboxEntity>().Ignore(row => row.ClaimDeliveredAtUtcTicks);
    }
}

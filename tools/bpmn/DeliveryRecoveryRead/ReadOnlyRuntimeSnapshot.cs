using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// Observe captures before the standard host migrates its copy. Absent optional projections keep their legacy null
// meaning; present projections remain mapped and pass through the ordinary checked runtime readers.
internal static class ReadOnlyRuntimeSnapshot
{
    private static readonly string[] SourceProjectionColumns =
    [
        nameof(RuntimePostCommitOutboxEntity.SourceKind),
        nameof(RuntimePostCommitOutboxEntity.SourceId),
        nameof(RuntimePostCommitOutboxEntity.TriggerIdHash),
        nameof(RuntimePostCommitOutboxEntity.SourceKeyHash)
    ];

    internal static async Task ConfigureServicesAsync(IServiceCollection services, string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        var shape = await ReadProjectionShapeAsync(connection);
        if (!shape.ClaimDelivery || !shape.Source)
            services.ConfigureDbContext<RuntimeSqliteDbContext>((_, options) =>
                ConfigureModel(options, shape));
    }

    internal static async Task<DbContextOptions<RuntimeSqliteDbContext>> OptionsAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection);
        ConfigureModel(options, await ReadProjectionShapeAsync(connection));
        return options.Options;
    }

    private static void ConfigureModel(DbContextOptionsBuilder options, (bool ClaimDelivery, bool Source) shape)
    {
        if (!shape.ClaimDelivery && !shape.Source)
            options.ReplaceService<IModelCustomizer, LegacyOutboxModelCustomizer>();
        else if (!shape.ClaimDelivery)
            options.ReplaceService<IModelCustomizer, LegacyClaimDeliveryOutboxModelCustomizer>();
        else if (!shape.Source)
            options.ReplaceService<IModelCustomizer, LegacyWorkflowSourceOutboxModelCustomizer>();
    }

    private static async Task<(bool ClaimDelivery, bool Source)> ReadProjectionShapeAsync(SqliteConnection connection)
    {
        if (new SqliteConnectionStringBuilder(connection.ConnectionString).Mode != SqliteOpenMode.ReadOnly)
            throw new InvalidOperationException("A captured runtime observer requires a read-only connection.");
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{RuntimePostCommitOutboxEfModule.TableName}\")";
        await using var columns = await command.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await columns.ReadAsync())
            names.Add(columns.GetString(1));
        if (names.Count == 0)
            throw new InvalidDataException("The snapshot has no runtime outbox table.");
        var sourceColumns = SourceProjectionColumns.Count(names.Contains);
        if (sourceColumns != 0 && sourceColumns != SourceProjectionColumns.Length)
            throw new InvalidDataException("The snapshot has partial standalone-source outbox projections.");
        return (names.Contains(nameof(RuntimePostCommitOutboxEntity.ClaimDeliveredAtUtcTicks)), sourceColumns != 0);
    }
}

// Distinct service types isolate each recognized shape in EF's model cache. Keep all other provider mappings,
// payload conversion and checked reads composed by the ordinary RuntimeSqliteDbContext.
internal abstract class ReadOnlyOutboxModelCustomizer : RelationalModelCustomizer
{
    private readonly bool _omitClaimDelivery;
    private readonly bool _omitSource;

    protected ReadOnlyOutboxModelCustomizer(ModelCustomizerDependencies dependencies, bool omitClaimDelivery, bool omitSource)
        : base(dependencies)
    {
        _omitClaimDelivery = omitClaimDelivery;
        _omitSource = omitSource;
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        var outbox = modelBuilder.Entity<RuntimePostCommitOutboxEntity>();
        if (_omitClaimDelivery)
            outbox.Ignore(row => row.ClaimDeliveredAtUtcTicks);
        if (!_omitSource) return;
        outbox.Ignore(row => row.SourceKind);
        outbox.Ignore(row => row.SourceId);
        outbox.Ignore(row => row.TriggerIdHash);
        outbox.Ignore(row => row.SourceKeyHash);
    }
}

internal sealed class LegacyOutboxModelCustomizer : ReadOnlyOutboxModelCustomizer
{
    public LegacyOutboxModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies, true, true) { }
}

internal sealed class LegacyClaimDeliveryOutboxModelCustomizer : ReadOnlyOutboxModelCustomizer
{
    public LegacyClaimDeliveryOutboxModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies, true, false) { }
}

internal sealed class LegacyWorkflowSourceOutboxModelCustomizer : ReadOnlyOutboxModelCustomizer
{
    public LegacyWorkflowSourceOutboxModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies, false, true) { }
}

using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// Captures are observed before the standard host migrates its own copy. Keep the original read-only: columns added
// after a capture was taken did not exist in that store, so the observer maps the model to the columns it finds.
// - The optional claim-delivery projection on the outbox; ReadChecked already accepts null for a legacy row.
// - Spec 1229's commit-sequence range and lost-update count on activity-execution state; both are nullable projections
//   of the row's content, and the observer never queries by them.
internal static class ReadOnlyRuntimeSnapshot
{
    internal static async Task ConfigureServicesAsync(IServiceCollection services, string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        var legacy = await LegacyColumnsAsync(connection);
        if (legacy != LegacyColumns.None)
            services.ConfigureDbContext<RuntimeSqliteDbContext>((_, options) => Apply(options, legacy));
    }

    internal static async Task<DbContextOptions<RuntimeSqliteDbContext>> OptionsAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection);
        Apply(options, await LegacyColumnsAsync(connection));
        return options.Options;
    }

    // One customizer type per shape: EF caches a model per internal service provider, and a replaced service type is
    // what keeps each legacy model apart from the current one.
    private static void Apply(DbContextOptionsBuilder options, LegacyColumns legacy)
    {
        switch (legacy)
        {
            case LegacyColumns.Outbox: options.ReplaceService<IModelCustomizer, LegacyOutboxModelCustomizer>(); break;
            case LegacyColumns.ActivityExecution: options.ReplaceService<IModelCustomizer, LegacyActivityExecutionModelCustomizer>(); break;
            case LegacyColumns.Outbox | LegacyColumns.ActivityExecution:
                options.ReplaceService<IModelCustomizer, LegacyOutboxAndActivityExecutionModelCustomizer>(); break;
        }
    }

    private static async Task<LegacyColumns> LegacyColumnsAsync(SqliteConnection connection)
    {
        if (new SqliteConnectionStringBuilder(connection.ConnectionString).Mode != SqliteOpenMode.ReadOnly)
            throw new InvalidOperationException("A captured runtime observer requires a read-only connection.");
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        var legacy = LegacyColumns.None;
        var outbox = await ColumnsAsync(connection, RuntimePostCommitOutboxEfModule.TableName)
                     ?? throw new InvalidDataException("The snapshot has no runtime outbox table.");
        if (!outbox.Contains(nameof(RuntimePostCommitOutboxEntity.ClaimDeliveredAtUtcTicks)))
            legacy |= LegacyColumns.Outbox;
        // A snapshot without the table at all has no rows for EF to read either; only an existing table can be legacy.
        var activity = await ColumnsAsync(connection, RuntimeActivityExecutionEfModule.ActivityExecutionStateTableName);
        if (activity is not null && !activity.Contains(nameof(ActivityExecutionStateEntity.FirstCommitSequence)))
            legacy |= LegacyColumns.ActivityExecution;
        return legacy;
    }

    private static async Task<HashSet<string>?> ColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var columns = await command.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await columns.ReadAsync())
            names.Add(columns.GetString(1));
        return names.Count == 0 ? null : names;
    }

    [Flags]
    private enum LegacyColumns
    {
        None = 0,
        Outbox = 1,
        ActivityExecution = 2
    }

    internal static void IgnoreOutbox(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<RuntimePostCommitOutboxEntity>().Ignore(row => row.ClaimDeliveredAtUtcTicks);

    internal static void IgnoreActivityExecution(ModelBuilder modelBuilder)
    {
        var state = modelBuilder.Entity<ActivityExecutionStateEntity>();
        var commitRange = state.Metadata.GetIndexes().Where(index => index.Properties.Any(property =>
            property.Name is nameof(ActivityExecutionStateEntity.FirstCommitSequence) or nameof(ActivityExecutionStateEntity.LastCommitSequence))).ToList();
        foreach (var index in commitRange)
            state.Metadata.RemoveIndex(index);
        state.Ignore(row => row.FirstCommitSequence);
        state.Ignore(row => row.LastCommitSequence);
        state.Ignore(row => row.LostUpdateWarningCount);
    }
}

// Distinct EF service configurations keep each legacy model separate from the current model cache. All existing
// provider mappings, payload conversion and checked reads remain composed by the ordinary RuntimeSqliteDbContext.
internal sealed class LegacyOutboxModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        ReadOnlyRuntimeSnapshot.IgnoreOutbox(modelBuilder);
    }
}

internal sealed class LegacyActivityExecutionModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        ReadOnlyRuntimeSnapshot.IgnoreActivityExecution(modelBuilder);
    }
}

internal sealed class LegacyOutboxAndActivityExecutionModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        ReadOnlyRuntimeSnapshot.IgnoreOutbox(modelBuilder);
        ReadOnlyRuntimeSnapshot.IgnoreActivityExecution(modelBuilder);
    }
}

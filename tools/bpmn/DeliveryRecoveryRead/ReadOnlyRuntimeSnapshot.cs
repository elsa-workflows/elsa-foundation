using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// Observe captures before the standard host migrates its copy. Absent optional projections keep their legacy null
// meaning; present projections remain mapped and pass through the ordinary checked runtime readers. The outbox's
// claim-delivery and standalone-source projections and the activity's commit-sequence projections vary independently.
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
        var legacy = await ReadProjectionShapeAsync(connection);
        if (legacy != LegacyColumns.None)
            services.ConfigureDbContext<RuntimeSqliteDbContext>((_, options) => ConfigureModel(options, legacy));
    }

    internal static async Task<DbContextOptions<RuntimeSqliteDbContext>> OptionsAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection);
        ConfigureModel(options, await ReadProjectionShapeAsync(connection));
        return options.Options;
    }

    // A distinct service type for every shape keeps EF's cached models apart.
    private static void ConfigureModel(DbContextOptionsBuilder options, LegacyColumns legacy)
    {
        switch (legacy)
        {
            case LegacyColumns.None:
                break;
            case LegacyColumns.ClaimDelivery:
                options.ReplaceService<IModelCustomizer, LegacyClaimDeliveryOutboxModelCustomizer>();
                break;
            case LegacyColumns.WorkflowSource:
                options.ReplaceService<IModelCustomizer, LegacyWorkflowSourceOutboxModelCustomizer>();
                break;
            case LegacyColumns.ClaimDelivery | LegacyColumns.WorkflowSource:
                options.ReplaceService<IModelCustomizer, LegacyOutboxModelCustomizer>();
                break;
            case LegacyColumns.ActivityExecution:
                options.ReplaceService<IModelCustomizer, LegacyActivityExecutionModelCustomizer>();
                break;
            case LegacyColumns.ClaimDelivery | LegacyColumns.ActivityExecution:
                options.ReplaceService<IModelCustomizer, LegacyClaimActivityCustomizer>();
                break;
            case LegacyColumns.WorkflowSource | LegacyColumns.ActivityExecution:
                options.ReplaceService<IModelCustomizer, LegacySourceActivityCustomizer>();
                break;
            case LegacyColumns.ClaimDelivery | LegacyColumns.WorkflowSource | LegacyColumns.ActivityExecution:
                options.ReplaceService<IModelCustomizer, LegacyOutboxAndActivityExecutionModelCustomizer>();
                break;
        }
    }

    private static async Task<LegacyColumns> ReadProjectionShapeAsync(SqliteConnection connection)
    {
        if (new SqliteConnectionStringBuilder(connection.ConnectionString).Mode != SqliteOpenMode.ReadOnly)
            throw new InvalidOperationException("A captured runtime observer requires a read-only connection.");
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        var legacy = LegacyColumns.None;
        var outbox = await ColumnsAsync(connection, RuntimePostCommitOutboxEfModule.TableName)
                     ?? throw new InvalidDataException("The snapshot has no runtime outbox table.");
        if (!outbox.Contains(nameof(RuntimePostCommitOutboxEntity.ClaimDeliveredAtUtcTicks)))
            legacy |= LegacyColumns.ClaimDelivery;
        var sourceColumns = SourceProjectionColumns.Count(outbox.Contains);
        if (sourceColumns != 0 && sourceColumns != SourceProjectionColumns.Length)
            throw new InvalidDataException("The snapshot has partial standalone-source outbox projections.");
        if (sourceColumns == 0)
            legacy |= LegacyColumns.WorkflowSource;
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
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await columns.ReadAsync())
            names.Add(columns.GetString(1));
        return names.Count == 0 ? null : names;
    }

    [Flags]
    private enum LegacyColumns
    {
        None = 0,
        ClaimDelivery = 1,
        ActivityExecution = 2,
        WorkflowSource = 4
    }

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

// All other provider mappings, payload conversion and checked reads remain composed by RuntimeSqliteDbContext.
internal abstract class ReadOnlyOutboxModelCustomizer : RelationalModelCustomizer
{
    private readonly bool _omitClaimDelivery;
    private readonly bool _omitSource;
    private readonly bool _omitActivityExecution;

    protected ReadOnlyOutboxModelCustomizer(ModelCustomizerDependencies dependencies, bool omitClaimDelivery, bool omitSource,
        bool omitActivityExecution = false)
        : base(dependencies)
    {
        _omitClaimDelivery = omitClaimDelivery;
        _omitSource = omitSource;
        _omitActivityExecution = omitActivityExecution;
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        var outbox = modelBuilder.Entity<RuntimePostCommitOutboxEntity>();
        if (_omitClaimDelivery)
            outbox.Ignore(row => row.ClaimDeliveredAtUtcTicks);
        if (_omitSource)
        {
            outbox.Ignore(row => row.SourceKind);
            outbox.Ignore(row => row.SourceId);
            outbox.Ignore(row => row.TriggerIdHash);
            outbox.Ignore(row => row.SourceKeyHash);
        }
        if (_omitActivityExecution)
            ReadOnlyRuntimeSnapshot.IgnoreActivityExecution(modelBuilder);
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

internal sealed class LegacyActivityExecutionModelCustomizer : ReadOnlyOutboxModelCustomizer
{
    public LegacyActivityExecutionModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies, false, false, true) { }
}

internal sealed class LegacyOutboxAndActivityExecutionModelCustomizer : ReadOnlyOutboxModelCustomizer
{
    public LegacyOutboxAndActivityExecutionModelCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies, true, true, true) { }
}

internal sealed class LegacyClaimActivityCustomizer : ReadOnlyOutboxModelCustomizer
{
    public LegacyClaimActivityCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies, true, false, true) { }
}

internal sealed class LegacySourceActivityCustomizer : ReadOnlyOutboxModelCustomizer
{
    public LegacySourceActivityCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies, false, true, true) { }
}

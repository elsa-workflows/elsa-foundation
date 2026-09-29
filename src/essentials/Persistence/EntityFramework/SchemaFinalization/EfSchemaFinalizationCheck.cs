using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// The read-only half of the finalization gate's activation check (spec 181, FR-015 and FR-016): whether a family's
/// record names a finalized or completion version this host cannot read. The gate applies it at Prepare, and the
/// enable-time guard applies it before anything is saved, so the two refuse exactly the same modules.
/// </summary>
public static class EfSchemaFinalizationCheck
{
    /// <summary>
    /// The refusal <paramref name="record"/> forces on a host whose build reads <paramref name="chain"/>'s versions, or
    /// null when the host can read everything the record says may be in the database. An intent is not judged here:
    /// only an activation, which can wait for it, can (FR-014).
    /// </summary>
    public static EfSchemaActivationRefusedException? Refusal(string module, EfSchemaChain chain, SchemaFinalizationRecord record)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(record);
        if (!chain.IsReadable(record.FinalizedVersion))
            return new EfSchemaActivationRefusedException(module, chain.Family, EfSchemaActivationRefusal.FinalizedUnreadable, record.FinalizedVersion, chain.ReadableVersions);
        if (record.Finish is { } finish && !chain.IsReadable(finish.CompletionVersion))
            return new EfSchemaActivationRefusedException(module, chain.Family, EfSchemaActivationRefusal.CompletionUnreadable, finish.CompletionVersion, chain.ReadableVersions);
        return null;
    }

    /// <summary>
    /// Reads every family's record in <paramref name="context"/>'s database and returns the first refusal, or null.
    /// A database or record table that does not exist yet has finalized nothing and refuses nothing, because the
    /// module's own migrator creates it at Prepare, where the gate checks again. A record that exists but cannot be read
    /// throws, so a caller fails closed.
    /// </summary>
    public static async Task<EfSchemaActivationRefusedException?> FindRefusalAsync(
        DbContext context,
        EfSchemaModuleFamilies families,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(families);
        if (families.Chains.Count == 0 || !await RecordTableExistsAsync(context, cancellationToken))
            return null;

        var store = new EfSchemaFinalizationStore(context);
        foreach (var chain in families.Chains)
        {
            if (await store.FindAsync(chain.Family, cancellationToken) is { } record &&
                Refusal(families.Module, chain, record) is { } refusal)
                return refusal;
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="context"/>'s database holds the module's finalization record table, found through the
    /// engine's own catalog rather than the migrations-history table, which a host under <c>AutoMigrate</c> never reads
    /// before Prepare (spec 171, FR-069). A database that does not exist is not created by asking.
    /// </summary>
    public static async Task<bool> RecordTableExistsAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var entity = context.Model.FindEntityType(typeof(EfSchemaFinalizationRecordRow))
                     ?? throw new InvalidOperationException($"{context.GetType().Name} does not map the finalization record.");
        var table = entity.GetTableName()!;
        var schema = entity.GetSchema();
        if (context.GetService<IRelationalDatabaseCreator>() is { } creator && !await creator.ExistsAsync(cancellationToken))
            return false;

        var provider = context.Database.ProviderName ?? "";
        var sql = provider switch
        {
            _ when provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) =>
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name",
            _ when provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) =>
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @name AND TABLE_SCHEMA = COALESCE(@schema, SCHEMA_NAME())",
            _ when provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) || provider.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase) =>
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @name AND table_schema = COALESCE(@schema, current_schema())",
            _ when provider.Contains("MySql", StringComparison.OrdinalIgnoreCase) =>
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = @name AND table_schema = DATABASE()",
            _ => throw new NotSupportedException($"The finalization record table cannot be looked up on the '{provider}' provider.")
        };

        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            AddParameter(command, "@name", table);
            if (sql.Contains("@schema", StringComparison.Ordinal))
                AddParameter(command, "@schema", schema);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) > 0;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, string? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Value = (object?)value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

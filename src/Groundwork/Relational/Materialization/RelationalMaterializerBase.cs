using System.Data;
using System.Data.Common;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;

namespace Groundwork.Relational.Materialization;

public abstract class RelationalMaterializerBase(DbConnection connection)
{
    protected virtual string ParameterPrefix => "@";

    protected abstract IReadOnlyList<string> SchemaStatements { get; }

    protected abstract string InsertSchemaHistorySql { get; }

    protected abstract IReadOnlyList<string> CreateOptimizedProjectionStatements(StorageUnit unit, IReadOnlyList<PhysicalizedFieldPlan> fields);

    public async Task MaterializeAsync(StorageManifest manifest, ProviderIdentity provider, CancellationToken cancellationToken = default)
    {
        await EnsureOpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var statement in SchemaStatements)
            await ExecuteAsync(statement, transaction, cancellationToken);

        foreach (var unit in manifest.StorageUnits)
        {
            var fields = PhysicalizationProjection.EligibleFields(unit);
            if (fields.Count == 0)
                continue;

            foreach (var statement in CreateOptimizedProjectionStatements(unit, fields))
                await ExecuteAsync(statement, transaction, cancellationToken);
        }

        await using var command = CreateCommand(InsertSchemaHistorySql, transaction);
        AddParameter(command, "manifestId", manifest.Identity.Value);
        AddParameter(command, "manifestVersion", manifest.Version.Value);
        AddParameter(command, "providerName", provider.Name);
        AddParameter(command, "providerVersion", provider.Version);
        AddParameter(command, "appliedUtc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ExecuteAsync(string sql, DbTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(sql, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(string commandText, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        return command;
    }

    private void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"{ParameterPrefix}{name}";
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private async Task EnsureOpenAsync(CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
    }
}

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Runtime.Output;

/// <summary>One SQL route delivery: its target, typed columns and retained body.</summary>
internal sealed record EtlSqlRouteDelivery(
    string ConnectionString, string LedgerTable, bool CreateLedger, string Table, IReadOnlyList<string> Key,
    IReadOnlyList<EtlColumn> Columns, ReadOnlyMemory<byte> Body, string EffectId, string RunId, string BatchId,
    string ContentHash)
{
    // The connection string holds credentials; never print it.
    public override string ToString() => nameof(EtlSqlRouteDelivery);
}

internal enum EtlSqlWriteOutcome { Applied, Replay, Yield }

/// <summary>
/// Writes one SQL route body in one transaction: the ledger row first, then one upsert per record in order (spec 1197).
/// </summary>
/// <remarks>
/// The ledger's primary key makes every effect apply once: a redelivery finds its row and writes nothing, and two
/// overlapping writers serialize on the key. Per-record statements let the database resolve a key repeated within a batch
/// under its own collation, the later record winning. Driver exceptions are wrapped here: a transient failure yields, and
/// anything deterministic fails the run with a stable code.
/// </remarks>
internal sealed class EtlSqlRouteWriter(IEtlSqlEngine engine, TimeProvider clock)
{
    public async ValueTask<EtlSqlWriteOutcome> WriteAsync(EtlSqlRouteDelivery delivery, CancellationToken cancellationToken)
    {
        var rows = ReadRows(delivery);
        try
        {
            await using var connection = engine.CreateConnection(delivery.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            if (engine.SessionSetup is { } setup)
                await ExecuteAsync(connection, null, setup, cancellationToken);
            if (delivery.CreateLedger)
                await EnsureLedgerAsync(connection, delivery.LedgerTable, cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            if (!await InsertLedgerAsync(connection, transaction, delivery, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return await ReplayAsync(connection, delivery, cancellationToken);
            }

            var keys = Convert.ToInt64(await ScalarAsync(connection, transaction, engine.CountUniqueKeys(delivery.Table, delivery.Key), [],
                cancellationToken), CultureInfo.InvariantCulture);
            if (keys < 0)
                throw new EtlEffectException("etl.sql-write-rejected", "The SQL destination table does not exist or is not a base table.");
            if (keys == 0)
                throw new EtlEffectException("etl.sql-key-constraint-missing",
                    "The SQL destination table has no unique constraint on exactly its key columns.");

            var columns = delivery.Columns.Select(column => column.Name).ToArray();
            var rowsPerCommand = Math.Max(1, engine.MaxParameters / columns.Length);
            for (var offset = 0; offset < rows.Count; offset += rowsPerCommand)
                await UpsertAsync(connection, transaction, delivery, columns, rows, offset,
                    Math.Min(rowsPerCommand, rows.Count - offset), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return EtlSqlWriteOutcome.Applied;
        }
        catch (EtlEffectException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            return engine.Classify(exception) switch
            {
                EtlSqlErrorKind.Transient => EtlSqlWriteOutcome.Yield,
                EtlSqlErrorKind.MissingConstraint => throw new EtlEffectException("etl.sql-key-constraint-missing",
                    "The SQL destination table has no unique constraint on exactly its key columns.", exception),
                _ => throw new EtlEffectException("etl.sql-write-rejected", "The SQL destination refused the batch.", exception)
            };
        }
    }

    private async ValueTask EnsureLedgerAsync(DbConnection connection, string ledgerTable, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(connection, null, engine.CreateLedger(ledgerTable), cancellationToken);
        }
        catch (DbException exception) when (engine.Classify(exception) is EtlSqlErrorKind.AlreadyExists or EtlSqlErrorKind.DuplicateKey)
        {
            // A concurrent creator won the race; the ledger exists either way.
        }
    }

    private async ValueTask<bool> InsertLedgerAsync(DbConnection connection, DbTransaction transaction,
        EtlSqlRouteDelivery delivery, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, engine.InsertLedger(delivery.LedgerTable));
        Add(command, "effect", DbType.String, delivery.EffectId);
        Add(command, "run", DbType.String, delivery.RunId);
        Add(command, "batch", DbType.String, delivery.BatchId);
        Add(command, "hash", DbType.String, delivery.ContentHash);
        Add(command, "applied", DbType.Int64, clock.GetUtcNow().UtcTicks);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (DbException exception) when (engine.Classify(exception) == EtlSqlErrorKind.DuplicateKey)
        {
            // Already applied, possibly by a writer whose commit this one waited on.
            return false;
        }
        catch (DbException exception) when (engine.Classify(exception) == EtlSqlErrorKind.MissingObject)
        {
            // Only the ledger insert distinguishes a missing ledger; a missing target table or column is a rejection.
            throw new EtlEffectException("etl.sql-ledger-missing", "The SQL destination's ledger table does not exist.", exception);
        }
    }

    private async ValueTask<EtlSqlWriteOutcome> ReplayAsync(DbConnection connection, EtlSqlRouteDelivery delivery,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, null, engine.ReadLedgerHash(delivery.LedgerTable));
        Add(command, "effect", DbType.String, delivery.EffectId);
        var recorded = await command.ExecuteScalarAsync(cancellationToken) as string;
        return StringComparer.Ordinal.Equals(recorded, delivery.ContentHash)
            ? EtlSqlWriteOutcome.Replay
            : throw new EtlEffectException("etl.effect-conflict", "The SQL ledger holds this effect with different content.");
    }

    private async ValueTask UpsertAsync(DbConnection connection, DbTransaction transaction, EtlSqlRouteDelivery delivery,
        IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows, int offset, int count, CancellationToken cancellationToken)
    {
        var text = string.Join(";\n", Enumerable.Range(0, count).Select(row => engine.Upsert(delivery.Table, columns, delivery.Key, row)));
        await using var command = Command(connection, transaction, text);
        for (var row = 0; row < count; row++)
        {
            var values = rows[offset + row];
            for (var index = 0; index < columns.Count; index++)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = engine.Parameter(columns[index], row);
                parameter.DbType = DbTypeFor(delivery.Columns[index].Type);
                parameter.Value = values[index] ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static List<object?[]> ReadRows(EtlSqlRouteDelivery delivery)
    {
        try
        {
            using var document = JsonDocument.Parse(delivery.Body);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("A SQL route body is a JSON array.");
            var rows = new List<object?[]>(document.RootElement.GetArrayLength());
            foreach (var record in document.RootElement.EnumerateArray())
            {
                var values = new object?[delivery.Columns.Count];
                for (var index = 0; index < delivery.Columns.Count; index++)
                {
                    var column = delivery.Columns[index];
                    values[index] = record.TryGetProperty(column.Name, out var value) ? Value(value, column.Type) : null;
                }
                rows.Add(values);
            }
            return rows;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or OverflowException)
        {
            throw new EtlEffectException("etl.connector-output-invalid", "The retained SQL route body is invalid.", exception);
        }
    }

    private static object? Value(JsonElement value, EtlColumnType type) => value.ValueKind == JsonValueKind.Null ? null : type switch
    {
        EtlColumnType.String => value.GetString(),
        EtlColumnType.Int64 => value.GetInt64(),
        EtlColumnType.Decimal => value.GetDecimal(),
        EtlColumnType.Boolean => value.GetBoolean(),
        _ => throw new InvalidOperationException("The SQL column type is unsupported.")
    };

    private static DbType DbTypeFor(EtlColumnType type) => type switch
    {
        EtlColumnType.String => DbType.String,
        EtlColumnType.Int64 => DbType.Int64,
        EtlColumnType.Decimal => DbType.Decimal,
        EtlColumnType.Boolean => DbType.Boolean,
        _ => throw new InvalidOperationException("The SQL column type is unsupported.")
    };

    private static DbCommand Command(DbConnection connection, DbTransaction? transaction, string text)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = text;
        return command;
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@" + name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static async ValueTask ExecuteAsync(DbConnection connection, DbTransaction? transaction, string text,
        CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, text);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async ValueTask<object?> ScalarAsync(DbConnection connection, DbTransaction transaction, string text,
        IReadOnlyList<DbParameter> parameters, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, text);
        foreach (var parameter in parameters) command.Parameters.Add(parameter);
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}

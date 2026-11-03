using System.Data.Common;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;

namespace Elsa.Etl.Runtime.Preparation;

/// <summary>Thrown when a SQL snapshot read failed transiently; preparation is retried once its lease lapses.</summary>
internal sealed class EtlSqlTransientReadException(Exception cause) : Exception("A SQL snapshot read failed transiently.", cause);

/// <summary>
/// Reads a SQL source's table as of one point in time and writes each row as one JSON Lines object (spec 1198). After
/// preparation the run processes that snapshot as a JSON Lines source whose fields are the columns.
/// </summary>
internal static class EtlSqlSnapshotReader
{
    // Snapshot lines are data read back by the JSON Lines reader, never HTML, so non-ASCII text stays raw UTF-8 instead
    // of tripling in size as \uXXXX escapes.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly byte[] LineFeed = [(byte)'\n'];

    /// <summary>The JSON Lines source a prepared SQL snapshot is processed as: one field per column, selected by name.</summary>
    internal static EtlJsonlSource AsJsonl(EtlSqlSource source) => new(source.Binding,
        Array.AsReadOnly(source.Columns.Select(column => new EtlJsonField(column.Name, "/" + column.Name, column.Type, column.Required)).ToArray()));

    /// <summary>The pipeline as it is processed: a SQL source becomes its JSON Lines view; any other pipeline is unchanged.</summary>
    internal static EtlPipelineV2 ProcessingView(EtlPipelineV2 pipeline) =>
        pipeline.Source is EtlSqlSource sql ? pipeline with { Source = AsJsonl(sql) } : pipeline;

    /// <summary>
    /// Streams the ordered rows in one transaction at the engine's snapshot isolation. A deterministic refusal is a
    /// preparation failure; a transient one is an <see cref="EtlSqlTransientReadException"/>.
    /// </summary>
    internal static async ValueTask WriteAsync(HostSqlResolvedBinding binding, EtlSqlSource source, long maxBytes,
        Stream destination, CancellationToken cancellationToken)
    {
        var engine = binding.Engine;
        var columns = source.Columns.Select(column => column.Name).ToArray();
        try
        {
            await using var connection = engine.CreateConnection(binding.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(engine.SnapshotIsolation, cancellationToken);
            if (engine.SnapshotSetup is { } setup)
                await ExecuteAsync(connection, transaction, setup, cancellationToken);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = engine.SelectSnapshot(source.Table, columns, source.Key);
                // A large ordered read may run longer than the driver's default timeout; the lease token bounds it instead.
                command.CommandTimeout = 0;
                // The reader is closed before the commit: PostgreSQL and SQL Server allow one open reader per connection.
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                await CopyRowsAsync(reader, source, columns, maxBytes, destination, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (EtlConnectorPreparationFailure)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            if (engine.Classify(exception) == EtlSqlErrorKind.Transient)
                throw new EtlSqlTransientReadException(exception);
            throw new EtlConnectorPreparationFailure(EtlPreparationDiagnostic.Fault("etl.sql-read-rejected", exception));
        }
        catch (Exception exception) when (exception is not (IOException or OutOfMemoryException or OperationCanceledException or EtlSqlTransientReadException))
        {
            // Any other provider failure is deterministic: retrying the same read would fail the same way. Snapshot-root I/O
            // failures propagate unwrapped, so the preparer reports them as write failures.
            throw new EtlConnectorPreparationFailure(EtlPreparationDiagnostic.Fault("etl.sql-read-rejected", exception));
        }
    }

    private static async ValueTask CopyRowsAsync(DbDataReader reader, EtlSqlSource source, string[] columns, long maxBytes,
        Stream destination, CancellationToken cancellationToken)
    {
        if (reader.FieldCount != columns.Length)
            throw Failure("etl.sql-read-rejected");
        using var line = new MemoryStream();
        long written = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            line.SetLength(0);
            try
            {
                await using (var writer = new Utf8JsonWriter(line, WriterOptions))
                {
                    writer.WriteStartObject();
                    for (var index = 0; index < columns.Length; index++)
                    {
                        writer.WritePropertyName(columns[index]);
                        WriteValue(writer, reader, index, source.Columns[index], maxBytes - written);
                    }
                    writer.WriteEndObject();
                }
            }
            catch (Exception exception) when (exception is IOException or ArgumentException && exception is not ArgumentNullException)
            {
                // A row too large for one in-memory line (the line buffer or a JSON token limit) cannot fit the snapshot.
                throw Failure("etl.snapshot-too-large");
            }
            line.Write(LineFeed);
            if (line.Length > maxBytes - written)
                throw Failure("etl.snapshot-too-large");
            await destination.WriteAsync(line.GetBuffer().AsMemory(0, (int)line.Length), cancellationToken);
            written += line.Length;
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, DbDataReader reader, int index, EtlColumn column, long remainingBytes)
    {
        if (reader.IsDBNull(index))
        {
            writer.WriteNullValue();
            return;
        }
        object value;
        try
        {
            value = reader.GetValue(index);
        }
        catch (Exception exception) when (exception is InvalidCastException or OverflowException or NotSupportedException)
        {
            // A value the provider cannot materialise, such as a numeric beyond decimal range or an unmapped type.
            throw Failure("etl.sql-column-type-unsupported", column.Name);
        }
        switch (column.Type)
        {
            case EtlColumnType.String:
                switch (value)
                {
                    case string text:
                        // Every character takes at least one byte, so a longer value can never fit what is left.
                        if (text.Length > remainingBytes)
                            throw Failure("etl.snapshot-too-large");
                        writer.WriteStringValue(text);
                        return;
                    case Guid guid: writer.WriteStringValue(guid.ToString("D", CultureInfo.InvariantCulture)); return;
                    // Both drivers return DateTime for a date column; write the date alone so it compares as one.
                    case DateTime date when string.Equals(reader.GetDataTypeName(index), "date", StringComparison.OrdinalIgnoreCase):
                        writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                        return;
                    case DateTime dateTime: writer.WriteStringValue(dateTime.ToString("O", CultureInfo.InvariantCulture)); return;
                    case DateTimeOffset offset: writer.WriteStringValue(offset.ToString("O", CultureInfo.InvariantCulture)); return;
                    case DateOnly date: writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture)); return;
                    case TimeOnly time: writer.WriteStringValue(time.ToString("O", CultureInfo.InvariantCulture)); return;
                    case TimeSpan span: writer.WriteStringValue(span.ToString("c", CultureInfo.InvariantCulture)); return;
                }
                break;
            case EtlColumnType.Int64:
                switch (value)
                {
                    case long number: writer.WriteNumberValue(number); return;
                    case int number: writer.WriteNumberValue(number); return;
                    case short number: writer.WriteNumberValue(number); return;
                    case byte number: writer.WriteNumberValue(number); return;
                }
                break;
            case EtlColumnType.Decimal:
                switch (value)
                {
                    case decimal number: writer.WriteNumberValue(number); return;
                    case long number: writer.WriteNumberValue(number); return;
                    case int number: writer.WriteNumberValue(number); return;
                    case short number: writer.WriteNumberValue(number); return;
                    case byte number: writer.WriteNumberValue(number); return;
                }
                break;
            case EtlColumnType.Boolean when value is bool flag:
                writer.WriteBooleanValue(flag);
                return;
        }
        throw Failure("etl.sql-column-type-unsupported", column.Name);
    }

    private static async ValueTask ExecuteAsync(DbConnection connection, DbTransaction transaction, string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static EtlConnectorPreparationFailure Failure(string code, string? field = null) =>
        new(new EtlPreparationDiagnostic(code, Field: field));
}

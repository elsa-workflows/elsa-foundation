using System.Data.Common;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;

namespace Elsa.Etl.Runtime.Preparation;

/// <summary>Thrown when a SQL snapshot read failed transiently; preparation is retried once its lease lapses.</summary>
internal sealed class EtlSqlTransientException(Exception cause) : Exception("A SQL snapshot read failed transiently.", cause);

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

    /// <summary>
    /// The pipeline as it is processed: a SQL source (spec 1198) or a RabbitMQ source (spec 1201) becomes the JSON Lines view of
    /// its retained snapshot; any other pipeline is unchanged.
    /// </summary>
    internal static EtlPipelineV2 ProcessingView(EtlPipelineV2 pipeline) => pipeline.Source switch
    {
        EtlSqlSource sql => pipeline with { Source = AsJsonl(sql) },
        EtlRabbitMqSource queue => pipeline with { Source = new EtlJsonlSource(queue.Binding, queue.Fields) },
        _ => pipeline
    };

    /// <summary>
    /// Streams the ordered rows in one transaction at the engine's snapshot isolation. A deterministic refusal is a
    /// preparation failure; a transient one is an <see cref="EtlSqlTransientException"/>.
    /// </summary>
    internal static async ValueTask WriteAsync(HostSqlResolvedBinding binding, EtlSqlSource source, long maxBytes,
        Stream destination, CancellationToken cancellationToken, long? watermarkFloor = null)
    {
        var engine = binding.Engine;
        var columns = source.Columns.Select(column => column.Name).ToArray();
        try
        {
            await using var connection = engine.CreateConnection(binding.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            // The same session as the writer's, so a session option such as a search path reads what the writer writes.
            if (engine.SessionSetup is { } session)
                await ExecuteAsync(connection, null, session, cancellationToken);
            await using var transaction = await BeginSnapshotAsync(connection, engine, source.Table, cancellationToken);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                // Without a checkpoint every row is read; with one, only rows above it (spec 1199).
                var filter = source.Watermark is not null && watermarkFloor is not null ? source.Watermark : null;
                command.CommandText = engine.SelectSnapshot(source.Table, columns, source.Key, filter);
                if (filter is not null)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@watermark";
                    parameter.DbType = System.Data.DbType.Int64;
                    parameter.Value = watermarkFloor!.Value;
                    command.Parameters.Add(parameter);
                }
                // A large ordered read may run longer than the driver's default timeout, so none applies; the heartbeat keeps
                // the lease while the worker lives, so only a lost lease or a stopping worker cancels the read.
                command.CommandTimeout = 0;
                // The reader is closed before the commit: PostgreSQL and SQL Server allow one open reader per connection.
                var reader = await command.ExecuteReaderAsync(cancellationToken);
                ExceptionDispatchInfo? failure = null;
                try
                {
                    await CopyRowsAsync(reader, source, columns, maxBytes, engine.ExtremeDatesAreInfinite, destination, cancellationToken);
                }
                catch (Exception exception)
                {
                    // Closing a reader drains every remaining row; cancel first so a doomed read stops at once.
                    failure = ExceptionDispatchInfo.Capture(exception);
                    try { command.Cancel(); } catch (Exception cancel) when (cancel is DbException or InvalidOperationException) { }
                }
                try
                {
                    await reader.DisposeAsync();
                }
                catch (Exception exception) when (failure is not null && exception is DbException or InvalidOperationException)
                {
                    // The cancellation surfaces while closing; the failure that stopped the read is the one to report.
                }
                failure?.Throw();
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
                throw new EtlSqlTransientException(exception);
            throw new EtlConnectorPreparationFailure(EtlPreparationDiagnostic.Fault("etl.sql-read-rejected", exception));
        }
        catch (Exception exception) when (exception is not (IOException or OutOfMemoryException or OperationCanceledException or EtlSqlTransientException))
        {
            // Any other provider failure is deterministic: retrying the same read would fail the same way. Snapshot-root I/O
            // failures propagate unwrapped, so the preparer reports them as write failures.
            throw new EtlConnectorPreparationFailure(EtlPreparationDiagnostic.Fault("etl.sql-read-rejected", exception));
        }
    }

    /// <summary>
    /// The highest non-null value of the watermark column in a published snapshot, or null when it has none (spec 1199). Read
    /// back from the snapshot itself, so a reused snapshot reports the same value as the read that published it.
    /// </summary>
    internal static async ValueTask<long?> ReadHighWatermarkAsync(string snapshotPath, string column, CancellationToken cancellationToken)
    {
        long? high = null;
        await foreach (var line in File.ReadLinesAsync(snapshotPath, cancellationToken))
        {
            if (line.Length == 0) continue;
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException exception)
            {
                // A retained snapshot that no longer parses is an integrity failure, as for any other snapshot read.
                throw new InvalidDataException("The SQL snapshot is not valid JSON lines.", exception);
            }
            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("The SQL snapshot holds a line that is not a JSON object.");
                if (document.RootElement.TryGetProperty(column, out var value) && value.ValueKind == JsonValueKind.Number &&
                    value.TryGetInt64(out var watermark) && (high is null || watermark > high))
                    high = watermark;
            }
        }
        return high;
    }

    /// <summary>
    /// Begins the snapshot transaction and takes the engine's table lock before any snapshot. Where the engine refuses the
    /// lock but might still allow the read (column-level grants, a relation that cannot be locked), the read runs without it.
    /// </summary>
    private static async ValueTask<DbTransaction> BeginSnapshotAsync(DbConnection connection, IEtlSqlEngine engine, string table,
        CancellationToken cancellationToken)
    {
        var transaction = await StartSnapshotAsync(connection, engine, cancellationToken);
        if (engine.SnapshotLock(table) is not { } lockStatement)
            return transaction;
        try
        {
            // The lock waits for any rewrite in progress as long as the read itself would.
            await ExecuteAsync(connection, transaction, lockStatement, cancellationToken, timeoutSeconds: 0);
            return transaction;
        }
        catch (DbException exception) when (engine.IsLockRefusal(exception))
        {
            // A failed statement aborts the transaction, so the unlocked read starts a new one.
            await transaction.DisposeAsync();
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
        return await StartSnapshotAsync(connection, engine, cancellationToken);
    }

    private static async ValueTask<DbTransaction> StartSnapshotAsync(DbConnection connection, IEtlSqlEngine engine,
        CancellationToken cancellationToken)
    {
        var transaction = await connection.BeginTransactionAsync(engine.SnapshotIsolation, cancellationToken);
        try
        {
            if (engine.SnapshotSetup is { } setup)
                await ExecuteAsync(connection, transaction, setup, cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private static async ValueTask CopyRowsAsync(DbDataReader reader, EtlSqlSource source, string[] columns, long maxBytes, bool infiniteExtremes,
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
                        WriteValue(writer, reader, index, source.Columns[index], maxBytes - written, infiniteExtremes);
                    }
                    writer.WriteEndObject();
                }
            }
            catch (Exception exception) when (exception is IOException or ArgumentException && exception is not ArgumentNullException)
            {
                // A row too large for one in-memory line (the line buffer or a JSON token limit) cannot fit the snapshot.
                throw Failure("etl.snapshot-too-large", cause: exception);
            }
            line.Write(LineFeed);
            if (line.Length > maxBytes - written)
                throw Failure("etl.snapshot-too-large");
            await destination.WriteAsync(line.GetBuffer().AsMemory(0, (int)line.Length), cancellationToken);
            written += line.Length;
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, DbDataReader reader, int index, EtlColumn column, long remainingBytes,
        bool infiniteExtremes)
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
        catch (Exception exception) when (exception is InvalidCastException or OverflowException or NotSupportedException or IOException or
            System.Text.DecoderFallbackException)
        {
            // A value the provider cannot materialise: a numeric beyond decimal range, an unmapped type, a type whose
            // assembly is not installed (SqlClient's spatial and hierarchy types), or text that is not valid in its encoding.
            throw Failure("etl.sql-column-type-unsupported", column.Name, exception);
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
                    // Where the driver maps infinity and -infinity to the extreme values, they are not dates, so refuse them.
                    case DateTime extreme when infiniteExtremes && (extreme == DateTime.MaxValue || extreme == DateTime.MinValue): break;
                    case DateOnly extreme when infiniteExtremes && (extreme == DateOnly.MaxValue || extreme == DateOnly.MinValue): break;
                    case DateTimeOffset extreme when infiniteExtremes &&
                        (extreme == DateTimeOffset.MaxValue || extreme == DateTimeOffset.MinValue): break;
                    // SqlClient returns DateTime for a date column (Npgsql returns DateOnly, written below); write the date
                    // alone so it compares as one.
                    case DateTime date when string.Equals(reader.GetDataTypeName(index), "date", StringComparison.OrdinalIgnoreCase):
                        writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                        return;
                    case DateTime dateTime: writer.WriteStringValue(dateTime.ToString("O", CultureInfo.InvariantCulture)); return;
                    // Npgsql returns a zoned time of day as DateTimeOffset with a placeholder date; refuse it rather than
                    // invent a timestamp.
                    case DateTimeOffset offset when !IsZonedTimeOfDay(reader.GetDataTypeName(index)):
                        writer.WriteStringValue(offset.ToString("O", CultureInfo.InvariantCulture));
                        return;
                    case DateOnly date: writer.WriteStringValue(date.ToString("O", CultureInfo.InvariantCulture)); return;
                    case TimeOnly time: writer.WriteStringValue(time.ToString("O", CultureInfo.InvariantCulture)); return;
                    // A time of day arrives as TimeSpan from SqlClient (and older Npgsql); an interval is a duration, not
                    // ISO 8601 time text, so it falls through to the refusal below.
                    case TimeSpan span when IsTimeOfDay(reader.GetDataTypeName(index)) && span >= TimeSpan.Zero && span < TimeSpan.FromDays(1):
                        writer.WriteStringValue(TimeOnly.FromTimeSpan(span).ToString("O", CultureInfo.InvariantCulture));
                        return;
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

    // SQL Server names it "time"; PostgreSQL "time without time zone", or "time(3) without time zone" with a precision.
    private static bool IsTimeOfDay(string dataTypeName) =>
        string.Equals(dataTypeName, "time", StringComparison.OrdinalIgnoreCase) ||
        IsTimeType(dataTypeName) && dataTypeName.EndsWith("without time zone", StringComparison.OrdinalIgnoreCase);

    // PostgreSQL "time with time zone", or "time(3) with time zone"; never a timestamp.
    private static bool IsZonedTimeOfDay(string dataTypeName) =>
        IsTimeType(dataTypeName) && dataTypeName.EndsWith(" with time zone", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimeType(string dataTypeName) =>
        dataTypeName.StartsWith("time", StringComparison.OrdinalIgnoreCase) &&
        !dataTypeName.StartsWith("timestamp", StringComparison.OrdinalIgnoreCase);

    private static async ValueTask ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql,
        CancellationToken cancellationToken, int? timeoutSeconds = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (timeoutSeconds is { } timeout)
            command.CommandTimeout = timeout;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static EtlConnectorPreparationFailure Failure(string code, string? field = null, Exception? cause = null) =>
        new(new EtlPreparationDiagnostic(code, Field: field) { Cause = cause });
}

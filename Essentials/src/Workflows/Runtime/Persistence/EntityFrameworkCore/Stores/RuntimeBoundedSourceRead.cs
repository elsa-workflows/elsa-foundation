using System.Data;
using System.Data.Common;
using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>The explicit current-schema SQLite source profile, applied before any owner JSON hydration.</summary>
internal static partial class RuntimeBoundedSourceRead
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly ConditionalWeakTable<IModel, Dictionary<Type, Shape>> Shapes = new();
    internal sealed record Row(Type EntityType, string Id);

    public static async ValueTask<T> ExecuteAsync<T>(RuntimeDbContext context, RuntimeSourceReadLimits limits,
        IReadOnlyList<Row> rows, Func<IReadOnlyDictionary<Row, int>, CancellationToken, ValueTask<T>> read, CancellationToken token,
        Func<DbConnection, DbTransaction, CancellationToken, ValueTask<IReadOnlyList<Row>>>? selectRows = null)
    {
        ArgumentNullException.ThrowIfNull(limits); limits.Validate(); token.ThrowIfCancellationRequested();
        if (context.Database.ProviderName != EfProviderNames.Sqlite || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || System.Transactions.Transaction.Current is not null || context.ChangeTracker.HasChanges())
            throw Refuse("runtime_source_storage_profile_unsupported", true);
        var opened = false;
        try
        {
            await context.Database.OpenConnectionAsync(token); opened = true;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var connection = context.Database.GetDbConnection(); var physical = transaction.GetDbTransaction();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = physical; command.CommandText = "PRAGMA encoding";
                if (await command.ExecuteScalarAsync(token) is not string encoding || !encoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
                    throw Refuse("runtime_source_storage_encoding_unsupported", true);
            }
            var lengths = new Dictionary<Row, int>(); var loadedBytes = 0L;
            var selectedRows = selectRows is null ? rows : await selectRows(connection, physical, token);
            if (selectedRows.Count > limits.MaxMaterials * 3) throw Refuse("runtime_source_row_limit");
            foreach (var row in selectedRows)
            {
                token.ThrowIfCancellationRequested();
                var shapes = Shapes.GetValue(context.Model, _ => new()); Shape shape;
                lock (shapes) if (!shapes.TryGetValue(row.EntityType, out shape!)) shapes.Add(row.EntityType, shape = new(context, row.EntityType));
                await using (var command = Command(shape.Preflight))
                await using (var reader = await command.ExecuteReaderAsync(token))
                {
                    if (!await reader.ReadAsync(token)) continue;
                    var column = 0;
                    foreach (var field in shape.Fields)
                    {
                        var type = reader.GetString(column++);
                        if (!field.Text)
                        { if (!(type == "integer" || type == "null" && field.Nullable)) throw Refuse("runtime_source_stored_type_invalid"); continue; }
                        var length = reader.IsDBNull(column) ? (long?)null : reader.GetInt64(column); column++;
                        if (type == "null" && field.Nullable && length is null) continue;
                        var maximum = field.Name == "ContentJson" ? limits.MaxPayloadBytes : limits.MaxMetadataBytes;
                        if (type != "text" || length is null || length < 0 || length > maximum) throw Refuse("runtime_source_stored_byte_limit");
                        if (field.Name == "ContentJson")
                        {
                            loadedBytes = checked(loadedBytes + length.Value);
                            if (loadedBytes > limits.MaxAggregatePayloadBytes) throw Refuse("runtime_source_aggregate_byte_limit");
                            lengths.Add(row, checked((int)length));
                        }
                    }
                    if (await reader.ReadAsync(token)) throw Refuse("runtime_source_stored_identity_ambiguous");
                }
                // Fetch bounded raw bytes, rather than GetString: a provider must not repair invalid UTF8 silently.
                await using (var command = Command(shape.Content))
                await using (var reader = await command.ExecuteReaderAsync(token))
                {
                    if (!await reader.ReadAsync(token)) throw Refuse("runtime_source_stored_snapshot_lost");
                    var schema = Utf8.GetString(reader.GetFieldValue<byte[]>(0));
                    if (schema != RuntimeArtifactEfModule.SchemaVersion) throw Refuse("runtime_source_schema_profile_unsupported", true);
                    if (shape.HasPayload)
                    {
                        var payload = reader.GetFieldValue<byte[]>(1);
                        if (payload.Length != lengths[row]) throw Refuse("runtime_source_stored_snapshot_lost");
                        if (payload.AsSpan().StartsWith("elsaz1."u8)) throw Refuse("runtime_source_payload_frame_unsupported", true);
                        CheckJson(payload, limits.MaxJsonDepth);
                    }
                    if (await reader.ReadAsync(token)) throw Refuse("runtime_source_stored_identity_ambiguous");
                }
                continue;
                DbCommand Command(string sql)
                {
                    var command = connection.CreateCommand(); command.Transaction = physical; command.CommandText = sql;
                    var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.DbType = DbType.String; parameter.Value = row.Id;
                    command.Parameters.Add(parameter); return command;
                }
            }
            var result = await read(lengths, token);
            await transaction.CommitAsync(token); return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (RuntimeSourceReadException) { throw; }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidCastException or OverflowException or NullReferenceException or FormatException)
        { throw Refuse("runtime_source_stored_content_invalid"); }
        finally { if (opened) await context.Database.CloseConnectionAsync(); }
    }

    internal static void CheckJson(ReadOnlySpan<byte> payload, int maximumDepth)
    {
        _ = Utf8.GetCharCount(payload);
        var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = maximumDepth });
        var objects = new Stack<HashSet<string>?>(); var count = 0;
        while (reader.Read())
        {
            if (++count > 2_000_000) throw Refuse("runtime_source_json_work_limit");
            if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.StartArray) objects.Push(null);
            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) objects.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName && !objects.Peek()!.Add(reader.GetString()!)) throw Refuse("runtime_source_duplicate_json_property");
            else if (reader.TokenType == JsonTokenType.String) _ = reader.GetString();
        }
        if (count == 0 || objects.Count != 0) throw Refuse("runtime_source_stored_content_invalid");
    }
    private static RuntimeSourceReadException Refuse(string code, bool unsupported = false) => new(code, unsupported);
    private sealed record Field(string Name, bool Text, bool Nullable);
    private sealed class Shape
    {
        public Field[] Fields { get; }
        public string Preflight { get; }
        public string Content { get; }
        public bool HasPayload { get; }
        public Shape(RuntimeDbContext context, Type type)
        {
            var entity = context.Model.FindEntityType(type) ?? throw Refuse("runtime_source_mapping_unsupported", true);
            var table = entity.GetTableName() ?? throw Refuse("runtime_source_mapping_unsupported", true);
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema()); var quote = context.GetService<ISqlGenerationHelper>();
            Fields = entity.GetProperties().Select(property => new Field(property.Name, property.ClrType == typeof(string), property.IsNullable)).ToArray();
            foreach (var property in entity.GetProperties())
                if ((property.GetTypeMapping().Converter is not null || property.GetValueConverter() is not null) &&
                        !(property.Name == "ContentJson" && property.GetValueConverter() is null or EfPayloadValueConverter &&
                          property.GetTypeMapping().Converter is null or EfPayloadValueConverter) ||
                    property.ClrType != typeof(string) && property.ClrType != typeof(bool) && property.ClrType != typeof(long) && property.ClrType != typeof(long?) &&
                    property.ClrType != typeof(int) && property.ClrType != typeof(int?))
                    throw Refuse("runtime_source_mapping_unsupported", true);
            string Column(string name) => quote.DelimitIdentifier(entity.FindProperty(name)?.GetColumnName(store) ?? throw Refuse("runtime_source_mapping_unsupported", true));
            var from = " FROM " + quote.DelimitIdentifier(table, entity.GetSchema()) + " WHERE " + Column("Id") + "=@id";
            Preflight = "SELECT " + string.Join(",", Fields.SelectMany(field => field.Text ? new[] { "typeof(" + Column(field.Name) + ")", "length(CAST(" + Column(field.Name) + " AS BLOB))" } : new[] { "typeof(" + Column(field.Name) + ")" })) + from;
            HasPayload = entity.FindProperty("ContentJson") is not null;
            Content = "SELECT CAST(" + Column("SchemaVersion") + " AS BLOB)" + (HasPayload ? ",CAST(" + Column("ContentJson") + " AS BLOB)" : "") + from;
        }
    }
}

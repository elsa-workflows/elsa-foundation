using System.Data;
using System.Data.Common;
using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;
using System.Runtime.CompilerServices;
using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfPublicationRecordStore
{
    private static readonly ConditionalWeakTable<IModel, SourceSnapshotShape> SourceSnapshotShapes = new();
    private static readonly UTF8Encoding SourceSnapshotUtf8 = new(false, true);

    /// <exception cref="PublicationSourceSnapshotPersistenceException">The scoped provider read failed; its original cause is retained.</exception>
    public async ValueTask<PublicationOriginSnapshot?> FindOriginBoundedAsync(string publicationId, PersistenceScope expectedScope,
        RuntimeSourceReadLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicationId); ArgumentNullException.ThrowIfNull(expectedScope); ArgumentNullException.ThrowIfNull(limits);
        limits.Validate(); cancellationToken.ThrowIfCancellationRequested(); var captured = accessContextAccessor.Current;
        if (captured.RequireScope(admitPrivileged: true) != expectedScope) throw SourceRefused("publication_source_scope_mismatch");
        if (context.Database.ProviderName != EfProviderNames.Sqlite || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || System.Transactions.Transaction.Current is not null || context.ChangeTracker.HasChanges())
            throw SourceRefused("publication_source_storage_profile_unsupported", true);
        var shape = SourceSnapshotShapes.GetValue(context.Model, _ => new(context)); var opened = false;
        try
        {
            if (SourceSnapshotUtf8.GetByteCount(publicationId) > limits.MaxMetadataBytes) throw SourceRefused("publication_source_identity_limit");
            await context.Database.OpenConnectionAsync(cancellationToken); opened = true;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var connection = context.Database.GetDbConnection(); var physical = transaction.GetDbTransaction();
            await using (var encoding = Command("PRAGMA encoding", false))
                if (await encoding.ExecuteScalarAsync(cancellationToken) is not string name || !name.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
                    throw SourceRefused("publication_source_storage_encoding_unsupported", true);
            var bytes = 0;
            await using (var command = Command(shape.Preflight))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) return null;
                var column = 0;
                foreach (var field in shape.Fields)
                {
                    var type = reader.GetString(column++);
                    if (field.Integer)
                    {
                        if (type != "integer" && !(field.Nullable && type == "null")) throw SourceRefused("publication_source_stored_type_invalid");
                        if (type == "integer") bytes = checked(bytes + sizeof(long));
                        if (bytes > limits.MaxAggregatePayloadBytes) throw SourceRefused("publication_source_aggregate_byte_limit");
                        continue;
                    }
                    var length = reader.IsDBNull(column) ? (long?)null : reader.GetInt64(column); column++;
                    if (field.Nullable && type == "null" && length is null) continue;
                    if (type != "text" || length is null || length < 0 || length > limits.MaxMetadataBytes) throw SourceRefused("publication_source_stored_byte_limit");
                    bytes = checked(bytes + (int)length.Value);
                    if (bytes > limits.MaxAggregatePayloadBytes) throw SourceRefused("publication_source_aggregate_byte_limit");
                }
                if (await reader.ReadAsync(cancellationToken)) throw SourceRefused("publication_source_ambiguous");
            }
            PublicationOriginSnapshot snapshot;
            await using (var command = Command(shape.Values))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) throw SourceRefused("publication_source_snapshot_lost");
                var text = new Dictionary<string, string?>(StringComparer.Ordinal); var numbers = new Dictionary<string, long?>(StringComparer.Ordinal);
                for (var index = 0; index < shape.Fields.Length; index++)
                {
                    var field = shape.Fields[index];
                    if (field.Integer) numbers.Add(field.Name, reader.IsDBNull(index) ? null : reader.GetInt64(index));
                    else text.Add(field.Name, reader.IsDBNull(index) ? null : SourceSnapshotUtf8.GetString(reader.GetFieldValue<byte[]>(index)));
                }
                if (await reader.ReadAsync(cancellationToken)) throw SourceRefused("publication_source_ambiguous");
                string Raw(string field) => text[field] ?? throw SourceRefused("publication_source_stored_content_invalid");
                string? Decoded(string field)
                {
                    if (text[field] is not { } encoded) return null;
                    var decoded = EfPublishingStoreSupport.DecodeIdentity(encoded, field);
                    if (EfPublishingStoreSupport.Encode(decoded) != encoded) throw SourceRefused("publication_source_stored_content_invalid");
                    return decoded;
                }
                if (Raw("SchemaVersion") != PublishingLedgerEfModule.ContentSchemaVersion) throw SourceRefused("publication_source_schema_unsupported", true);
                var id = Decoded("PublicationId")!; var tenant = Decoded("TenantId"); var slot = Decoded("SlotId")!;
                if (id != publicationId || tenant != expectedScope.Value || Raw("Id") != EfPublishingStoreSupport.PhysicalId(tenant, id) ||
                    Raw("TenantIdHash") != EfPublishingStoreSupport.TenantHash(tenant) || Raw("PublicationIdHash") != EfPublishingStoreSupport.Hash(id) ||
                    Raw("SlotIdHash") != EfPublishingStoreSupport.Hash(slot) ||
                    !Enum.TryParse<PublicationStatus>(Raw("Status"), false, out var status) || !Enum.IsDefined(status) || Raw("Status") != status.ToString() ||
                    numbers["Revision"] is null or < 1) throw SourceRefused("publication_source_stored_identity_invalid");
                var ticks = numbers["ActivatedAtUtcTicks"]; var offset = numbers["ActivatedAtOffsetMinutes"];
                if ((ticks is null) != (offset is null)) throw SourceRefused("publication_source_stored_content_invalid");
                DateTimeOffset? activatedAt = ticks is null ? null : EfPublishingStoreSupport.DateTimeOffset(ticks.Value, checked((int)offset!.Value));
                snapshot = new(id, slot, Decoded("WorkflowDefinitionId")!, Decoded("WorkflowDefinitionVersionId")!, Decoded("ArtifactId")!,
                    Decoded("SourceReferenceId"), status, activatedAt, expectedScope, bytes);
            }
            if (accessContextAccessor.Current != captured) throw SourceRefused("publication_source_scope_changed");
            await transaction.CommitAsync(cancellationToken); return snapshot;
            DbCommand Command(string sql, bool parameters = true)
            {
                var command = connection.CreateCommand(); command.Transaction = physical; command.CommandText = sql;
                if (parameters) { Add("@id", EfPublishingStoreSupport.PhysicalId(expectedScope.Value, publicationId)); }
                return command;
                void Add(string key, string value) { var item = command.CreateParameter(); item.ParameterName = key; item.Value = value; item.DbType = DbType.String; command.Parameters.Add(item); }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (RuntimeSourceReadException) { throw; }
        catch (Exception exception) when (exception is DbException or IOException or UnauthorizedAccessException or NotSupportedException)
        { throw new PublicationSourceSnapshotPersistenceException(publicationId, expectedScope, exception); }
        catch (Exception exception) when (exception is EncoderFallbackException or DecoderFallbackException or InvalidCastException or OverflowException or ArgumentException or InvalidOperationException)
        { throw SourceRefused("publication_source_stored_content_invalid"); }
        finally
        {
            if (opened)
                try { await context.Database.CloseConnectionAsync(); }
                catch (Exception exception) when (exception is DbException or IOException or UnauthorizedAccessException or NotSupportedException)
                { throw new PublicationSourceSnapshotPersistenceException(publicationId, expectedScope, exception); }
        }
    }

    private static RuntimeSourceReadException SourceRefused(string code, bool unsupported = false) => new(code, unsupported);
    private sealed record SourceSnapshotField(string Name, bool Nullable = false, bool Integer = false);
    private sealed class SourceSnapshotShape
    {
        public SourceSnapshotField[] Fields { get; } = [new("Id"), new("PublicationId"), new("PublicationIdHash"), new("SlotId"), new("SlotIdHash"),
            new("WorkflowDefinitionId"), new("WorkflowDefinitionVersionId"), new("ArtifactId"), new("SourceReferenceId", Nullable: true), new("Status"),
            new("TenantId", Nullable: true), new("TenantIdHash"), new("SchemaVersion"), new("Revision", Integer: true),
            new("ActivatedAtUtcTicks", Nullable: true, Integer: true), new("ActivatedAtOffsetMinutes", Nullable: true, Integer: true)];
        public string Preflight { get; }
        public string Values { get; }
        public SourceSnapshotShape(PublishingSnapshotReviewDbContext db)
        {
            var entity = db.Model.FindEntityType(typeof(PublicationRecordEntity)) ?? throw SourceRefused("publication_source_mapping_unsupported", true);
            var table = entity.GetTableName() ?? throw SourceRefused("publication_source_mapping_unsupported", true);
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema()); var quote = db.GetService<ISqlGenerationHelper>();
            string Column(string name)
            {
                var property = entity.FindProperty(name) ?? throw SourceRefused("publication_source_mapping_unsupported", true);
                if (property.GetTypeMapping().Converter is not null || property.GetValueConverter() is not null) throw SourceRefused("publication_source_mapping_unsupported", true);
                return quote.DelimitIdentifier(property.GetColumnName(store) ?? throw SourceRefused("publication_source_mapping_unsupported", true));
            }
            var from = " FROM " + quote.DelimitIdentifier(table, entity.GetSchema()) + " WHERE " + Column("Id") + "=@id";
            Preflight = "SELECT " + string.Join(",", Fields.SelectMany(field => field.Integer ? new[] { "typeof(" + Column(field.Name) + ")" } :
                new[] { "typeof(" + Column(field.Name) + ")", "length(CAST(" + Column(field.Name) + " AS BLOB))" })) + from;
            Values = "SELECT " + string.Join(",", Fields.Select(field => field.Integer ? Column(field.Name) : "CAST(" + Column(field.Name) + " AS BLOB)")) + from;
        }
    }
}

using System.Data;
using System.Data.Common;
using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;
using System.Runtime.CompilerServices;
using System.Text;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfActivityDesignStores : IActivityPublicationSnapshotReader
{
    private static readonly ConditionalWeakTable<IModel, PublicationSnapshotShape> PublicationSnapshotShapes = new();
    private static readonly UTF8Encoding SnapshotUtf8 = new(false, true);

    ValueTask<ActivityPublicationSnapshot?> IActivityPublicationSnapshotReader.FindAsync(string versionId, string expectedPartition,
        ActivityPublicationSnapshotLimits limits, CancellationToken cancellationToken) => ReadPublicationSnapshotAsync(versionId, expectedPartition, limits, cancellationToken);

    private async ValueTask<ActivityPublicationSnapshot?> ReadPublicationSnapshotAsync(string versionId, string expectedPartition,
        ActivityPublicationSnapshotLimits limits, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId); ArgumentException.ThrowIfNullOrWhiteSpace(expectedPartition); ArgumentNullException.ThrowIfNull(limits); limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (SnapshotUtf8.GetByteCount(versionId) > limits.MaxIdentityBytes) throw SnapshotRefused("activity_publication_identity_limit");
        if (db.Database.ProviderName != EfProviderNames.Sqlite || db.Database.CurrentTransaction is not null ||
            db.Database.GetEnlistedTransaction() is not null || System.Transactions.Transaction.Current is not null || db.ChangeTracker.HasChanges() || access is null)
            throw SnapshotRefused("activity_publication_snapshot_profile_unsupported", true);
        var captured = access.Current; var partition = captured.RequireScope(admitPrivileged: true).Value;
        if (partition != expectedPartition) throw SnapshotRefused("activity_publication_snapshot_scope_mismatch");
        var visible = ActivitiesDesignDbContext.NormalizeTenantKey(partition);
        var shape = PublicationSnapshotShapes.GetValue(db.Model, _ => new(db));
        var opened = false;
        try
        {
            await db.Database.OpenConnectionAsync(cancellationToken); opened = true;
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var connection = db.Database.GetDbConnection(); var physical = transaction.GetDbTransaction();
            await using (var encoding = connection.CreateCommand())
            {
                encoding.Transaction = physical; encoding.CommandText = "PRAGMA encoding";
                if (await encoding.ExecuteScalarAsync(cancellationToken) is not string name || !name.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
                    throw SnapshotRefused("activity_publication_snapshot_encoding_unsupported", true);
            }
            await using (var encoding = connection.CreateCommand())
            {
                encoding.Transaction = physical; encoding.CommandText = "PRAGMA encoding";
                if (await encoding.ExecuteScalarAsync(cancellationToken) is not string name || !name.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
                    throw SnapshotRefused("activity_publication_snapshot_encoding_unsupported", true);
            }
            var storedBytes = 0; var jsonBytes = 0;
            // Scalar-only preflight: no Contract/Provider string or typed contract has been materialized yet.
            await using (var command = Command(shape.PreflightSql))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) return null;
                var column = 0;
                foreach (var field in shape.Fields)
                {
                    var type = reader.GetString(column++);
                    if (field.Integer) { if (type != "integer") throw SnapshotRefused("activity_publication_snapshot_type_invalid"); storedBytes = checked(storedBytes + sizeof(long)); continue; }
                    var length = reader.IsDBNull(column) ? (long?)null : reader.GetInt64(column); column++;
                    if (field.Nullable && type == "null" && length is null) continue;
                    var maximum = field.Json ? limits.MaxJsonBytes : limits.MaxIdentityBytes;
                    if (type != "text" || length is null || length < 0 || length > maximum) throw SnapshotRefused("activity_publication_snapshot_byte_limit");
                    storedBytes = checked(storedBytes + (int)length.Value);
                    if (field.Json && (jsonBytes = checked(jsonBytes + (int)length.Value)) > limits.MaxJsonBytes) throw SnapshotRefused("activity_publication_snapshot_byte_limit");
                }
                if (await reader.ReadAsync(cancellationToken)) throw SnapshotRefused("activity_publication_snapshot_ambiguous");
            }
            ActivityPublicationSnapshot snapshot;
            await using (var command = Command(shape.ValuesSql))
            await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) throw SnapshotRefused("activity_publication_snapshot_lost");
                var values = new Dictionary<string, byte[]?>(StringComparer.Ordinal); var integers = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var index = 0; index < shape.Fields.Length; index++)
                {
                    var field = shape.Fields[index];
                    if (field.Integer) integers.Add(field.Name, checked((int)reader.GetInt64(index)));
                    else
                    {
                        var bytes = reader.IsDBNull(index) ? null : reader.GetFieldValue<byte[]>(index);
                        if (field.Json && bytes is not null && EfPayloadCodec.IsFrame(SnapshotUtf8.GetString(bytes)))
                            throw SnapshotRefused("activity_publication_snapshot_payload_frame_unsupported", true);
                        values.Add(field.Name, bytes);
                    }
                }
                if (await reader.ReadAsync(cancellationToken)) throw SnapshotRefused("activity_publication_snapshot_ambiguous");
                string? Optional(string key) => values[key] is { } value ? SnapshotUtf8.GetString(value) : null;
                string Text(string key) => Optional(key) ?? throw SnapshotRefused("activity_publication_snapshot_invalid");
                var tenant = Optional(nameof(ActivityDefinitionVersionPublication.TenantId));
                if (Text(EfSchemaVersionMaterializationInterceptor.PropertyName) != ActivitiesDesignEfModule.SchemaVersion)
                    throw SnapshotRefused("activity_publication_snapshot_schema_unsupported", true);
                if (Text(nameof(ActivityDefinitionVersionPublication.DefinitionVersionId)) != versionId ||
                    Text("DefinitionVersionIdIdentityHash") != ActivitiesDesignDbContext.ComputeIdentityHash(versionId) ||
                    Text("IdIdentityHash") != ActivitiesDesignDbContext.ComputeIdentityHash(Text("Id")) ||
                    Text("TenantScopeKey") != ActivitiesDesignDbContext.NormalizeTenantKey(tenant) || tenant is not null && tenant != partition)
                    throw SnapshotRefused("activity_publication_snapshot_identity_invalid");
                snapshot = new(Text(nameof(ActivityDefinitionVersionPublication.DefinitionId)), versionId, Text(nameof(ActivityDefinitionVersionPublication.Version)),
                    Text(nameof(ActivityDefinitionVersionPublication.ActivityTypeKey)), (ActivityDefinitionVersionResolutionKind)integers[nameof(ActivityDefinitionVersionPublication.ResolutionKind)],
                    Text(nameof(ActivityDefinitionVersionPublication.TemplateId)), Text(nameof(ActivityDefinitionVersionPublication.TemplateHash)),
                    Text(nameof(ActivityDefinitionVersionPublication.SourceReferenceId)), Text(nameof(ActivityDefinitionVersionPublication.ProviderFingerprint)),
                    (ActivityDefinitionVersionLifecycle)integers[nameof(ActivityDefinitionVersionPublication.Lifecycle)], tenant,
                    values[nameof(ActivityDefinitionVersionPublication.Contract)]!, values[nameof(ActivityDefinitionVersionPublication.Provider)]!,
                    limits, Optional(nameof(ActivityDefinitionVersionPublication.SourceDraftId)),
                    new(Text("Id"), Optional(nameof(ActivityDefinitionVersionPublication.SourceVersionId)),
                        integers[nameof(ActivityDefinitionVersionPublication.DirectDependencyCount)], integers[nameof(ActivityDefinitionVersionPublication.ClosedTemplateCount)],
                        integers[nameof(ActivityDefinitionVersionPublication.ResumeTargetCount)], Text(nameof(ActivityDefinitionVersionPublication.PublishedAt)),
                        values[nameof(ActivityDefinitionVersionPublication.RuntimeRequirements)]!, values[nameof(ActivityDefinitionVersionPublication.ResourceMeasurements)]!, limits), storedBytes);
            }
            if (access.Current != captured) throw SnapshotRefused("activity_publication_snapshot_scope_changed");
            await transaction.CommitAsync(cancellationToken);
            return snapshot;

            DbCommand Command(string sql)
            {
                var command = connection.CreateCommand(); command.Transaction = physical; command.CommandText = sql;
                Parameter(command, "@scope", visible); Parameter(command, "@global", ActivitiesDesignDbContext.GlobalTenantKey);
                Parameter(command, "@versionHash", ActivitiesDesignDbContext.ComputeIdentityHash(versionId)); return command;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (ActivityPublicationSnapshotException) { throw; }
        catch (Exception exception) when (exception is DbException or IOException)
        { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "load-snapshot", nameof(ActivityDefinitionVersionPublication), exception); }
        catch (Exception exception) when (exception is EncoderFallbackException or DecoderFallbackException or OverflowException or InvalidCastException)
        { throw SnapshotRefused("activity_publication_snapshot_invalid"); }
        finally
        {
            if (opened)
                try { await db.Database.CloseConnectionAsync(); }
                catch (Exception exception) when (exception is DbException or IOException)
                { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "close-snapshot", nameof(ActivityDefinitionVersionPublication), exception); }
        }
    }

    private static void Parameter(DbCommand command, string name, string value)
    { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.DbType = DbType.String; parameter.Value = value; command.Parameters.Add(parameter); }
    private static ActivityPublicationSnapshotException SnapshotRefused(string code, bool unsupported = false) => new(code, unsupported);
    private sealed record PublicationSnapshotField(string Name, bool Nullable = false, bool Json = false, bool Integer = false);
    private sealed class PublicationSnapshotShape
    {
        public PublicationSnapshotField[] Fields { get; } = [new("Id"), new("IdIdentityHash"), new("TenantScopeKey"), new("DefinitionVersionIdIdentityHash"),
            new(nameof(ActivityDefinitionVersionPublication.DefinitionId)), new(nameof(ActivityDefinitionVersionPublication.DefinitionVersionId)),
            new(nameof(ActivityDefinitionVersionPublication.Version)), new(nameof(ActivityDefinitionVersionPublication.ActivityTypeKey)),
            new(nameof(ActivityDefinitionVersionPublication.TemplateId)), new(nameof(ActivityDefinitionVersionPublication.TemplateHash)),
            new(nameof(ActivityDefinitionVersionPublication.SourceReferenceId)), new(nameof(ActivityDefinitionVersionPublication.ProviderFingerprint)),
            new(nameof(ActivityDefinitionVersionPublication.TenantId), Nullable: true), new(nameof(ActivityDefinitionVersionPublication.SourceDraftId), Nullable: true),
            new(nameof(ActivityDefinitionVersionPublication.SourceVersionId), Nullable: true), new(nameof(ActivityDefinitionVersionPublication.PublishedAt)),
            new(EfSchemaVersionMaterializationInterceptor.PropertyName), new(nameof(ActivityDefinitionVersionPublication.ResolutionKind), Integer: true),
            new(nameof(ActivityDefinitionVersionPublication.DirectDependencyCount), Integer: true), new(nameof(ActivityDefinitionVersionPublication.ClosedTemplateCount), Integer: true),
            new(nameof(ActivityDefinitionVersionPublication.ResumeTargetCount), Integer: true),
            new(nameof(ActivityDefinitionVersionPublication.Lifecycle), Integer: true), new(nameof(ActivityDefinitionVersionPublication.Contract), Json: true),
            new(nameof(ActivityDefinitionVersionPublication.Provider), Json: true), new(nameof(ActivityDefinitionVersionPublication.RuntimeRequirements), Json: true),
            new(nameof(ActivityDefinitionVersionPublication.ResourceMeasurements), Json: true)];
        public string PreflightSql { get; }
        public string ValuesSql { get; }
        public PublicationSnapshotShape(ActivitiesDesignDbContext context)
        {
            var entity = context.Model.FindEntityType(typeof(ActivityDefinitionVersionPublication)) ?? throw SnapshotRefused("activity_publication_snapshot_mapping_unsupported", true);
            var table = entity.GetTableName() ?? throw SnapshotRefused("activity_publication_snapshot_mapping_unsupported", true);
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema()); var quote = context.GetService<ISqlGenerationHelper>();
            string Column(string name)
            {
                var property = entity.FindProperty(name) ?? throw SnapshotRefused("activity_publication_snapshot_mapping_unsupported", true);
                return quote.DelimitIdentifier(property.GetColumnName(store) ?? throw SnapshotRefused("activity_publication_snapshot_mapping_unsupported", true));
            }
            var from = " FROM " + quote.DelimitIdentifier(table, entity.GetSchema()) + " WHERE (" + Column("TenantScopeKey") + "=@scope OR " + Column("TenantScopeKey") + "=@global) AND " + Column("DefinitionVersionIdIdentityHash") + "=@versionHash";
            PreflightSql = "SELECT " + string.Join(",", Fields.SelectMany(field => field.Integer ? new[] { "typeof(" + Column(field.Name) + ")" } :
                new[] { "typeof(" + Column(field.Name) + ")", "length(CAST(" + Column(field.Name) + " AS BLOB))" })) + from;
            ValuesSql = "SELECT " + string.Join(",", Fields.Select(field => field.Integer ? Column(field.Name) : "CAST(" + Column(field.Name) + " AS BLOB)")) + from;
        }
    }
}

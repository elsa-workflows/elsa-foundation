using System.Data;
using System.Data.Common;
using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;
using System.Runtime.CompilerServices;
using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

/// <summary>Current-schema SQLite source profile; never invokes IPayloadSerializer or builds a WorkflowDefinitionState.</summary>
internal sealed class EfWorkflowVersionSnapshotReader(WorkflowsDesignDbContext context, IPersistenceAccessContextAccessor access) : IWorkflowVersionSnapshotReader
{
    private static readonly ConditionalWeakTable<IModel, Shape> Shapes = new();
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public async ValueTask<WorkflowVersionSnapshot?> FindAsync(string versionId, string expectedPartition, WorkflowVersionSnapshotLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId); ArgumentException.ThrowIfNullOrWhiteSpace(expectedPartition); ArgumentNullException.ThrowIfNull(limits); limits.Validate();
        cancellationToken.ThrowIfCancellationRequested(); var captured = access.Current;
        if (captured.RequireScope(admitPrivileged: true).Value != expectedPartition) throw Refuse("workflow_version_snapshot_scope_mismatch");
        if (context.Database.ProviderName != EfProviderNames.Sqlite || context.Database.CurrentTransaction is not null ||
            context.Database.GetEnlistedTransaction() is not null || System.Transactions.Transaction.Current is not null || context.ChangeTracker.HasChanges())
            throw Refuse("workflow_version_snapshot_profile_unsupported", true);
        var shape = Shapes.GetValue(context.Model, _ => new(context)); var opened = false;
        try
        {
            if (Utf8.GetByteCount(versionId) > limits.MaxIdentityBytes) throw Refuse("workflow_version_snapshot_identity_invalid");
            await context.Database.OpenConnectionAsync(cancellationToken); opened = true;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var connection = context.Database.GetDbConnection(); var physical = transaction.GetDbTransaction();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = physical; command.CommandText = "PRAGMA encoding";
                if (await command.ExecuteScalarAsync(cancellationToken) is not string encoding || !encoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
                    throw Refuse("workflow_version_snapshot_encoding_unsupported", true);
            }
            await using (var command = Command(shape.Preflight))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) return null;
                for (var index = 0; index < shape.Fields.Length; index++)
                {
                    var name = shape.Fields[index]; var type = reader.GetString(index * 2); var length = reader.IsDBNull(index * 2 + 1) ? (long?)null : reader.GetInt64(index * 2 + 1);
                    var maximum = name == nameof(WorkflowDefinitionVersion.StateSource) ? limits.MaxSourceBytes : limits.MaxIdentityBytes;
                    if (type != "text" || length is null || length < 0 || length > maximum) throw Refuse("workflow_version_snapshot_byte_limit");
                }
                if (await reader.ReadAsync(cancellationToken)) throw Refuse("workflow_version_snapshot_ambiguous");
            }
            WorkflowVersionSnapshot snapshot;
            await using (var command = Command(shape.Values))
            await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) throw Refuse("workflow_version_snapshot_lost");
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var index = 0; index < shape.Fields.Length; index++) values.Add(shape.Fields[index], Utf8.GetString(reader.GetFieldValue<byte[]>(index)));
                if (await reader.ReadAsync(cancellationToken)) throw Refuse("workflow_version_snapshot_ambiguous");
                if (EfPayloadCodec.IsFrame(values["StateSource"])) throw Refuse("workflow_version_snapshot_payload_frame_unsupported", true);
                if (values[EfSchemaVersionMaterializationInterceptor.PropertyName] != WorkflowsDesignEfModule.SchemaVersion)
                    throw Refuse("workflow_version_snapshot_schema_unsupported", true);
                if (values["Id"] != versionId || values["TenantId"] != expectedPartition || values["ScopeKey"] != EfDesignSupport.ScopeKey(expectedPartition) ||
                    values["IdLookupHash"] != EfDesignSupport.LookupHash(versionId) || values["DefinitionIdLookupHash"] != EfDesignSupport.LookupHash(EfDesignSupport.SearchKey(values["DefinitionId"])))
                    throw Refuse("workflow_version_snapshot_identity_invalid");
                snapshot = new(values["DefinitionId"], versionId, values["Version"], values["TenantId"], values["StateSource"], limits);
            }
            if (access.Current != captured) throw Refuse("workflow_version_snapshot_scope_changed");
            await transaction.CommitAsync(cancellationToken); return snapshot;
            DbCommand Command(string sql)
            {
                var command = connection.CreateCommand(); command.Transaction = physical; command.CommandText = sql;
                Parameter(command, "@scope", EfDesignSupport.ScopeKey(expectedPartition)); Parameter(command, "@version", EfDesignSupport.LookupHash(versionId)); return command;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (WorkflowVersionSnapshotException) { throw; }
        catch (Exception exception) when (exception is DbException or IOException)
        { throw new DesignPersistenceException(DesignPersistenceDomain.Workflow, DesignPersistenceFailureKind.Provider, "load-snapshot", nameof(WorkflowDefinitionVersion), exception); }
        catch (Exception exception) when (exception is EncoderFallbackException or DecoderFallbackException or InvalidCastException or OverflowException)
        { throw Refuse("workflow_version_snapshot_invalid"); }
        finally
        {
            if (opened)
                try { await context.Database.CloseConnectionAsync(); }
                catch (Exception exception) when (exception is DbException or IOException)
                { throw new DesignPersistenceException(DesignPersistenceDomain.Workflow, DesignPersistenceFailureKind.Provider, "close-snapshot", nameof(WorkflowDefinitionVersion), exception); }
        }
    }
    private static void Parameter(DbCommand command, string name, string value)
    { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.DbType = DbType.String; parameter.Value = value; command.Parameters.Add(parameter); }
    private static WorkflowVersionSnapshotException Refuse(string code, bool unsupported = false) => new(code, unsupported);
    private sealed class Shape
    {
        public string[] Fields { get; } = ["Id", "IdLookupHash", "DefinitionId", "DefinitionIdLookupHash", "Version", "TenantId", "ScopeKey", "StateSource", EfSchemaVersionMaterializationInterceptor.PropertyName];
        public string Preflight { get; }
        public string Values { get; }
        public Shape(WorkflowsDesignDbContext context)
        {
            var entity = context.Model.FindEntityType(typeof(WorkflowDefinitionVersion)) ?? throw Refuse("workflow_version_snapshot_mapping_unsupported", true);
            var table = entity.GetTableName() ?? throw Refuse("workflow_version_snapshot_mapping_unsupported", true);
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema()); var quote = context.GetService<ISqlGenerationHelper>();
            string Column(string name)
            {
                var property = entity.FindProperty(name) ?? throw Refuse("workflow_version_snapshot_mapping_unsupported", true);
                if (property.ClrType != typeof(string) || (property.GetValueConverter() is not null || property.GetTypeMapping().Converter is not null) &&
                    !(name == "StateSource" && property.GetValueConverter() is null or EfPayloadValueConverter &&
                      property.GetTypeMapping().Converter is null or EfPayloadValueConverter))
                    throw Refuse("workflow_version_snapshot_mapping_unsupported", true);
                return quote.DelimitIdentifier(property.GetColumnName(store) ?? throw Refuse("workflow_version_snapshot_mapping_unsupported", true));
            }
            var from = " FROM " + quote.DelimitIdentifier(table, entity.GetSchema()) + " WHERE " + Column("ScopeKey") + "=@scope AND " + Column("IdLookupHash") + "=@version";
            Preflight = "SELECT " + string.Join(",", Fields.SelectMany(name => new[] { "typeof(" + Column(name) + ")", "length(CAST(" + Column(name) + " AS BLOB))" })) + from;
            Values = "SELECT " + string.Join(",", Fields.Select(name => "CAST(" + Column(name) + " AS BLOB)")) + from;
        }
    }
}

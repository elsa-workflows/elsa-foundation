using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Shared lossless projections used by the dispatch store and atomic outbox transitions.</summary>
internal static class WorkflowDispatchEfSupport
{
    public static string RowId(string scope, string dispatchId) => EfRuntimeOperationalStoreSupport.CompositeId(scope, dispatchId);

    public static string DispatchOrderKey(string value)
        => EfRelationalIdentity.CreateOrdinalTextOrderKey(value);

    public static string OrderKey(string value)
    {
        var prefix = value[..Math.Min(value.Length, RuntimeWorkflowDispatchEfModule.OrderKeyPrefixMaximumLength)];
        return Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(
                   prefix,
                   RuntimeWorkflowDispatchEfModule.OrderKeyPrefixMaximumLength)) +
               EfRelationalIdentity.Hash(value);
    }

    public static WorkflowDispatchEntity ToEntity(
        WorkflowDispatchRecord record,
        string scope,
        string id,
        long revision) => new()
    {
        Id = id,
        ScopeKey = EfRelationalIdentity.Encode(scope),
        ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        DispatchId = EfRelationalIdentity.Encode(record.DispatchId),
        DispatchIdHash = EfRelationalIdentity.Hash(record.DispatchId),
        DispatchIdOrderKey = DispatchOrderKey(record.DispatchId),
        ParentWorkflowExecutionId = EfRelationalIdentity.Encode(record.ParentWorkflowExecutionId),
        ParentWorkflowExecutionIdHash = EfRelationalIdentity.Hash(record.ParentWorkflowExecutionId),
        ParentWorkflowExecutionIdOrderKey = OrderKey(record.ParentWorkflowExecutionId),
        ParentActivityExecutionId = EfRelationalIdentity.Encode(record.ParentActivityExecutionId),
        ParentActivityExecutionIdHash = EfRelationalIdentity.Hash(record.ParentActivityExecutionId),
        ParentActivityExecutionIdOrderKey = OrderKey(record.ParentActivityExecutionId),
        ChildWorkflowExecutionId = EfRelationalIdentity.Encode(record.ChildWorkflowExecutionId),
        ChildWorkflowExecutionIdHash = EfRelationalIdentity.Hash(record.ChildWorkflowExecutionId),
        ChildWorkflowExecutionIdOrderKey = OrderKey(record.ChildWorkflowExecutionId),
        ChildArtifactId = EfRelationalIdentity.Encode(record.ChildExecutable.ArtifactId),
        ChildArtifactIdHash = EfRelationalIdentity.Hash(record.ChildExecutable.ArtifactId),
        ChildArtifactIdOrderKey = OrderKey(record.ChildExecutable.ArtifactId),
        TestScopeId = record.TestScope is null ? null : EfRelationalIdentity.Encode(record.TestScope.ScopeId),
        TestScopeIdHash = record.TestScope is null ? null : EfRelationalIdentity.Hash(record.TestScope.ScopeId),
        TestScopeIdOrderKey = record.TestScope is null ? null : OrderKey(record.TestScope.ScopeId),
        TenantId = record.TenantId is null ? null : EfRelationalIdentity.Encode(record.TenantId),
        TenantIdHash = record.TenantId is null ? null : EfRelationalIdentity.Hash(record.TenantId),
        Mode = (int)record.Mode,
        Status = (int)record.Status,
        CreatedAtUtcTicks = record.CreatedAt.UtcTicks,
        UpdatedAtUtcTicks = record.UpdatedAt.UtcTicks,
        ContentJson = RuntimeArtifactJson.Serialize(record),
        SchemaVersion = RuntimeWorkflowDispatchEfModule.SchemaVersion,
        Revision = revision
    };

    public static void Copy(WorkflowDispatchEntity row, WorkflowDispatchRecord record, string scope, long revision)
    {
        var replacement = ToEntity(record, scope, row.Id, revision);
        row.ScopeKey = replacement.ScopeKey;
        row.ScopeKeyHash = replacement.ScopeKeyHash;
        row.DispatchId = replacement.DispatchId;
        row.DispatchIdHash = replacement.DispatchIdHash;
        row.DispatchIdOrderKey = replacement.DispatchIdOrderKey;
        row.ParentWorkflowExecutionId = replacement.ParentWorkflowExecutionId;
        row.ParentWorkflowExecutionIdHash = replacement.ParentWorkflowExecutionIdHash;
        row.ParentWorkflowExecutionIdOrderKey = replacement.ParentWorkflowExecutionIdOrderKey;
        row.ParentActivityExecutionId = replacement.ParentActivityExecutionId;
        row.ParentActivityExecutionIdHash = replacement.ParentActivityExecutionIdHash;
        row.ParentActivityExecutionIdOrderKey = replacement.ParentActivityExecutionIdOrderKey;
        row.ChildWorkflowExecutionId = replacement.ChildWorkflowExecutionId;
        row.ChildWorkflowExecutionIdHash = replacement.ChildWorkflowExecutionIdHash;
        row.ChildWorkflowExecutionIdOrderKey = replacement.ChildWorkflowExecutionIdOrderKey;
        row.ChildArtifactId = replacement.ChildArtifactId;
        row.ChildArtifactIdHash = replacement.ChildArtifactIdHash;
        row.ChildArtifactIdOrderKey = replacement.ChildArtifactIdOrderKey;
        row.TestScopeId = replacement.TestScopeId;
        row.TestScopeIdHash = replacement.TestScopeIdHash;
        row.TestScopeIdOrderKey = replacement.TestScopeIdOrderKey;
        row.TenantId = replacement.TenantId;
        row.TenantIdHash = replacement.TenantIdHash;
        row.Mode = replacement.Mode;
        row.Status = replacement.Status;
        row.CreatedAtUtcTicks = replacement.CreatedAtUtcTicks;
        row.UpdatedAtUtcTicks = replacement.UpdatedAtUtcTicks;
        row.ContentJson = replacement.ContentJson;
        row.SchemaVersion = replacement.SchemaVersion;
        row.Revision = revision;
    }

    public static WorkflowDispatchRecord ReadChecked(
        WorkflowDispatchEntity row,
        string scope,
        string? expectedDispatchId = null)
    {
        if (EfSchemaVersion.NotReadable("RuntimeWorkflowDispatch", row.SchemaVersion, RuntimeWorkflowDispatchEfModule.SchemaVersion) ||
            row.Revision <= 0 ||
            row.ScopeKey != EfRelationalIdentity.Encode(scope) ||
            row.ScopeKeyHash != EfRelationalIdentity.Hash(scope))
            throw new InvalidDataException("The workflow dispatch row scope, schema, or revision projection is corrupt.");

        string dispatchId;
        try
        {
            dispatchId = EfRelationalIdentity.Decode(row.DispatchId);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("The workflow dispatch logical identity projection is invalid.", exception);
        }

        if (expectedDispatchId is not null && !StringComparer.Ordinal.Equals(dispatchId, expectedDispatchId))
            throw new InvalidOperationException($"Workflow dispatch physical identity collision detected for '{expectedDispatchId}'.");

        WorkflowDispatchRecord record;
        try
        {
            record = RuntimeArtifactJson.Deserialize<WorkflowDispatchRecord>(row.ContentJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("The persisted workflow dispatch is not valid current data.", exception);
        }

        var valid =
            row.Id == RowId(scope, dispatchId) &&
            row.DispatchIdHash == EfRelationalIdentity.Hash(dispatchId) &&
            row.DispatchIdOrderKey == DispatchOrderKey(dispatchId) &&
            StringComparer.Ordinal.Equals(record.DispatchId, dispatchId) &&
            row.ParentWorkflowExecutionId == EfRelationalIdentity.Encode(record.ParentWorkflowExecutionId) &&
            row.ParentWorkflowExecutionIdHash == EfRelationalIdentity.Hash(record.ParentWorkflowExecutionId) &&
            row.ParentWorkflowExecutionIdOrderKey == OrderKey(record.ParentWorkflowExecutionId) &&
            row.ParentActivityExecutionId == EfRelationalIdentity.Encode(record.ParentActivityExecutionId) &&
            row.ParentActivityExecutionIdHash == EfRelationalIdentity.Hash(record.ParentActivityExecutionId) &&
            row.ParentActivityExecutionIdOrderKey == OrderKey(record.ParentActivityExecutionId) &&
            row.ChildWorkflowExecutionId == EfRelationalIdentity.Encode(record.ChildWorkflowExecutionId) &&
            row.ChildWorkflowExecutionIdHash == EfRelationalIdentity.Hash(record.ChildWorkflowExecutionId) &&
            row.ChildWorkflowExecutionIdOrderKey == OrderKey(record.ChildWorkflowExecutionId) &&
            row.ChildArtifactId == EfRelationalIdentity.Encode(record.ChildExecutable.ArtifactId) &&
            row.ChildArtifactIdHash == EfRelationalIdentity.Hash(record.ChildExecutable.ArtifactId) &&
            row.ChildArtifactIdOrderKey == OrderKey(record.ChildExecutable.ArtifactId) &&
            row.TestScopeId == (record.TestScope is null ? null : EfRelationalIdentity.Encode(record.TestScope.ScopeId)) &&
            row.TestScopeIdHash == (record.TestScope is null ? null : EfRelationalIdentity.Hash(record.TestScope.ScopeId)) &&
            row.TestScopeIdOrderKey == (record.TestScope is null ? null : OrderKey(record.TestScope.ScopeId)) &&
            row.TenantId == (record.TenantId is null ? null : EfRelationalIdentity.Encode(record.TenantId)) &&
            row.TenantIdHash == (record.TenantId is null ? null : EfRelationalIdentity.Hash(record.TenantId)) &&
            row.Mode == (int)record.Mode &&
            row.Status == (int)record.Status &&
            row.CreatedAtUtcTicks == record.CreatedAt.UtcTicks &&
            row.UpdatedAtUtcTicks == record.UpdatedAt.UtcTicks;
        if (!valid)
            throw new InvalidDataException("The workflow dispatch identity or lifecycle projection does not match its content.");

        return record;
    }
}

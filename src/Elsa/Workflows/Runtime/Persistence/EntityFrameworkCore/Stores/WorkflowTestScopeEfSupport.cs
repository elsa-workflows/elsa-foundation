using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Shared lossless projections and staging helpers for the EF workflow test-scope ledger.</summary>
internal static class WorkflowTestScopeEfSupport
{
    public static string Id(string accessScope, string scopeId) =>
        EfRelationalIdentity.Hash(accessScope + "\u001f" + scopeId);

    public static string Order(string value) =>
        Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, WorkflowTestScope.MaximumScopeIdLength));

    public static WorkflowTestScopeEntity ToEntity(
        WorkflowTestScopeRecord record,
        string accessScope,
        string id) => new()
    {
        Id = id,
        AccessScopeKey = EfRelationalIdentity.Encode(accessScope),
        AccessScopeKeyHash = EfRelationalIdentity.Hash(accessScope),
        ScopeId = EfRelationalIdentity.Encode(record.Scope.ScopeId),
        ScopeIdHash = EfRelationalIdentity.Hash(record.Scope.ScopeId),
        ScopeIdOrderKey = Order(record.Scope.ScopeId),
        TenantId = record.Scope.TenantId is null ? null : EfRelationalIdentity.Encode(record.Scope.TenantId),
        TenantIdHash = record.Scope.TenantId is null ? null : EfRelationalIdentity.Hash(record.Scope.TenantId),
        Partition = EfRelationalIdentity.Encode(record.Scope.Partition.Value),
        PartitionHash = EfRelationalIdentity.Hash(record.Scope.Partition.Value),
        PartitionOrderKey = Order(record.Scope.Partition.Value),
        ExpiresAtUtcTicks = record.Scope.ExpiresAt.UtcTicks,
        State = (int)record.State,
        Revision = 0,
        ContentJson = RuntimeArtifactJson.Serialize(record),
        SchemaVersion = RuntimeWorkflowTestScopeEfModule.SchemaVersion
    };

    public static WorkflowTestScopeRecord Read(
        WorkflowTestScopeEntity row,
        string accessScope,
        string? expectedScopeId = null)
    {
        var record = RuntimeArtifactJson.Deserialize<WorkflowTestScopeRecord>(row.ContentJson);
        var tenant = record.Scope.TenantId;
        var partition = record.Scope.Partition.Value;
        var valid =
            (expectedScopeId is null || StringComparer.Ordinal.Equals(record.Scope.ScopeId, expectedScopeId)) &&
            (tenant is null || StringComparer.Ordinal.Equals(tenant, accessScope)) &&
            row.Id == Id(accessScope, record.Scope.ScopeId) &&
            row.SchemaVersion == RuntimeWorkflowTestScopeEfModule.SchemaVersion &&
            row.AccessScopeKey == EfRelationalIdentity.Encode(accessScope) &&
            row.AccessScopeKeyHash == EfRelationalIdentity.Hash(accessScope) &&
            row.ScopeId == EfRelationalIdentity.Encode(record.Scope.ScopeId) &&
            row.ScopeIdHash == EfRelationalIdentity.Hash(record.Scope.ScopeId) &&
            row.ScopeIdOrderKey == Order(record.Scope.ScopeId) &&
            row.TenantId == (tenant is null ? null : EfRelationalIdentity.Encode(tenant)) &&
            row.TenantIdHash == (tenant is null ? null : EfRelationalIdentity.Hash(tenant)) &&
            row.Partition == EfRelationalIdentity.Encode(partition) &&
            row.PartitionHash == EfRelationalIdentity.Hash(partition) &&
            row.PartitionOrderKey == Order(partition) &&
            row.ExpiresAtUtcTicks == record.Scope.ExpiresAt.UtcTicks &&
            row.State == (int)record.State &&
            row.Revision >= 0;
        if (!valid)
            throw new InvalidDataException("The workflow test-scope projections do not match its durable content.");

        return record;
    }

    public static void Copy(
        WorkflowTestScopeEntity row,
        WorkflowTestScopeRecord record,
        long revision)
    {
        row.State = (int)record.State;
        row.ExpiresAtUtcTicks = record.Scope.ExpiresAt.UtcTicks;
        row.Revision = revision;
        row.ContentJson = RuntimeArtifactJson.Serialize(record);
    }

    /// <summary>Stages the provider-neutral revision touch used by test-scoped admission.</summary>
    public static void StageAdmission(WorkflowTestScopeEntity row) => row.Revision = checked(row.Revision + 1);
}

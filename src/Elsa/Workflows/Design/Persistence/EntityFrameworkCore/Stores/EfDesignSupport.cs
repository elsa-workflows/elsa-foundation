using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Serialization.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

internal static class EfDesignSupport
{
    public static IQueryable<T> InScope<T>(IQueryable<T> query, IPersistenceAccessContextAccessor access, Func<T, string?> tenant) where T : class
    {
        if (access.Current.Scope is { } scope)
            return query.Where(row => EF.Property<string>(row, "TenantId") == scope.Value);
        if (access.Current.AcrossScopes && access.Current.AccessPolicy == PersistenceAccessPolicy.Privileged)
            return query;
        throw new InvalidOperationException("Workflow design persistence requires an explicit scope or privileged across-scope access.");
    }

    public static void EnsureTenant(IPersistenceAccessContextAccessor access, string? tenant) => access.Current.EnsureTenantScope(tenant);

    public static WorkflowDefinitionState ReadState(IPayloadSerializer serializer, string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new InvalidDataException("Workflow design state source is missing.");
        try { return serializer.Deserialize<WorkflowDefinitionState>(source); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        { throw new InvalidDataException("Workflow design state source is corrupt.", ex); }
    }

    public static string WriteState(IPayloadSerializer serializer, WorkflowDefinitionState state) =>
        serializer.Serialize(state);

    public static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
    public static T ReadJson<T>(string value) => JsonSerializer.Deserialize<T>(value, JsonSerializerOptions.Web) ?? throw new InvalidDataException("Workflow design JSON is empty.");

    public static void Stamp(Elsa.Primitives.Entities.Entity entity, DateTimeOffset now, DateTimeOffset? created = null)
    {
        entity.CreatedAt = created ?? (entity.CreatedAt == default ? now : entity.CreatedAt);
        entity.LastModifiedAt = now;
    }

    public static string Fingerprint<T>(string operationKind, T value)
    {
        var json = Json(value);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"elsa-design-material:v1\n{operationKind}\n{json}"));
        return $"sha256:{Convert.ToHexStringLower(bytes)}";
    }

    public static string OperationKey(string operationKind, string operationKey) => operationKind + "\u001f" + operationKey;

    public static WorkflowDefinition MapDefinition(WorkflowDefinition row) => row;

    public static WorkflowDefinitionVersion MapVersion(IPayloadSerializer serializer, WorkflowDefinitionVersion row)
    {
        row.State = ReadState(serializer, row.StateSource);
        return row;
    }

    public static WorkflowDefinitionDraft MapDraft(IPayloadSerializer serializer, WorkflowDefinitionDraft row)
    {
        row.State = ReadState(serializer, row.StateSource);
        return row;
    }

    public static IReadOnlyCollection<DesignMetadataRecord> ReadLayout(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : ReadJson<DesignMetadataRecord[]>(json);

    public static IReadOnlyCollection<ActivityPresentationRecord> ReadPresentation(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : ReadJson<ActivityPresentationRecord[]>(json);

    public static void SetLayout(DbContext context, object row, IReadOnlyCollection<DesignMetadataRecord> records, IReadOnlyCollection<ActivityPresentationRecord>? presentation = null)
    {
        context.Entry(row).Property("RecordsJson").CurrentValue = Json(records.ToArray());
        context.Entry(row).Property("ActivityPresentationJson").CurrentValue = Json((presentation ?? []).ToArray());
    }
}

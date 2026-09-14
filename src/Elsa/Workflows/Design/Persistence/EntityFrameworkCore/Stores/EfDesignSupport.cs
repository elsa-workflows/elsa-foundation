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

    public static bool IsResultFingerprintValid(string operationKind, string fingerprint, string json)
    {
        if (StringComparer.Ordinal.Equals(fingerprint, FingerprintJson(operationKind, json)))
            return true;

        // Groundwork's provider-neutral atomic command supplies authoritative result markers
        // using its framed material identity. Accept that format so the same command remains
        // usable with the EF writer without coupling this provider to Groundwork.
        return StringComparer.Ordinal.Equals(fingerprint, GroundworkFingerprintJson(operationKind, json));
    }

    private static string FingerprintJson(string operationKind, string json)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"elsa-design-material:v1\n{operationKind}\n{json}"));
        return $"sha256:{Convert.ToHexStringLower(bytes)}";
    }

    private static string GroundworkFingerprintJson(string operationKind, string json)
    {
        using var document = JsonDocument.Parse(json);
        var canonical = Canonical(document.RootElement);
        var material = string.Concat(
            Frame("elsa-design-material:v1"),
            Frame(operationKind),
            Frame("1"),
            Frame(canonical));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"sha256:{Convert.ToHexStringLower(bytes)}";
    }

    private static string Frame(string value) => $"{Encoding.UTF8.GetByteCount(value)}:{value}";

    private static string Canonical(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
                WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else
            element.WriteTo(writer);
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

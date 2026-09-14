using System.Security.Cryptography;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Serialization.Core;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

internal static class EfDesignSupport
{
    public static async Task<T> ReadAsync<T>(string operation, Func<Task<T>> read)
    {
        try
        {
            return await read();
        }
        catch (OperationCanceledException) { throw; }
        catch (DesignPersistenceException) { throw; }
        catch (DbUpdateException exception) { throw ProviderFailure(operation, exception); }
        catch (DbException exception) { throw ProviderFailure(operation, exception); }
    }

    public static void ValidateOperationIdentity(DesignOperationKey key, string operationKind)
        => DesignOperationKey.Validate(key, operationKind);

    public static string SearchKey(string value)
        => WorkflowDefinitionIdentity.Fold(value);

    public static void SetDefinitionSearchKeys(DbContext context, WorkflowDefinition definition)
    {
        WorkflowDefinitionLimits.Validate(definition);
        context.Entry(definition).Property<string?>("IdSearchKey").CurrentValue = SearchKey(definition.Id);
        context.Entry(definition).Property<string>("IdLookupHash").CurrentValue = LookupHash(SearchKey(definition.Id));
        context.Entry(definition).Property<string?>("NameSearchKey").CurrentValue = SearchKey(definition.Name);
        context.Entry(definition).Property<string?>("DescriptionSearchKey").CurrentValue = definition.Description is null ? null : SearchKey(definition.Description);
    }

    public static string LookupHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static DesignPersistenceException ProviderFailure(string operation, Exception exception) =>
        new(DesignPersistenceDomain.Workflow, DesignPersistenceFailureKind.Provider, operation, null, exception.InnerException ?? exception);

    public static IQueryable<T> InScope<T>(IQueryable<T> query, IPersistenceAccessContextAccessor access, Func<T, string?> tenant) where T : class
    {
        if (access.Current.Scope is { } scope)
            return query.Where(row => EF.Property<string>(row, "TenantId") == scope.Value);
        if (access.Current.AcrossScopes && access.Current.AccessPolicy == PersistenceAccessPolicy.Privileged)
            return query;
        throw new InvalidOperationException("Workflow design persistence requires an explicit scope or privileged across-scope access.");
    }

    public static void EnsureTenant(IPersistenceAccessContextAccessor access, string? tenant) => access.Current.EnsureTenantScope(tenant);

    public static WorkflowDefinitionState ReadState(IPayloadSerializer serializer, string? source, string operation = "workflow.state.read")
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new DesignPersistenceException(DesignPersistenceDomain.Workflow, DesignPersistenceFailureKind.Serialization, operation, "workflow state", new InvalidDataException("Workflow design state source is missing."));
        try { return serializer.Deserialize<WorkflowDefinitionState>(source); }
        catch (DesignPersistenceException) { throw; }
        catch (Exception ex) when (IsSerializationFailure(ex))
        { throw new DesignPersistenceException(DesignPersistenceDomain.Workflow, DesignPersistenceFailureKind.Serialization, operation, "workflow state", ex); }
    }

    public static string WriteState(IPayloadSerializer serializer, WorkflowDefinitionState state, string operation = "workflow.state.write")
    {
        try { return serializer.Serialize(state); }
        catch (DesignPersistenceException) { throw; }
        catch (Exception ex) when (IsSerializationFailure(ex))
        { throw new DesignPersistenceException(DesignPersistenceDomain.Workflow, DesignPersistenceFailureKind.Serialization, operation, "workflow state", ex); }
    }

    private static bool IsSerializationFailure(Exception exception) =>
        exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException or AccessViolationException);

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
        return FingerprintJson(operationKind, json);
    }

    /// <summary>
    /// Computes the pre-canonical EF marker format used before the provider-neutral framing was
    /// introduced. It remains available only to read existing markers during the transition.
    /// </summary>
    public static string LegacyFingerprint<T>(string operationKind, T value) =>
        LegacyFingerprintJson(operationKind, Json(value));

    public static bool IsResultFingerprintValid(string operationKind, string fingerprint, string json)
    {
        if (StringComparer.Ordinal.Equals(fingerprint, FingerprintJson(operationKind, json)))
            return true;

        // Preserve replay of markers written by the initial EF implementation.
        if (StringComparer.Ordinal.Equals(fingerprint, LegacyFingerprintJson(operationKind, json)))
            return true;

        return false;
    }

    private static string FingerprintJson(string operationKind, string json)
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

    private static string LegacyFingerprintJson(string operationKind, string json) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"elsa-design-material:v1\n{operationKind}\n{json}")))}";

    private static string Frame(string value) => $"{Encoding.UTF8.GetByteCount(value).ToString(System.Globalization.CultureInfo.InvariantCulture)}:{value}";

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

    public static DesignLayoutMaterial[] LayoutMaterial(IReadOnlyCollection<DesignMetadataRecord> records) =>
        records.Select(record => new DesignLayoutMaterial(
            record.NodeId,
            record.X,
            record.Y,
            record.Width,
            record.Height,
            record.AdditionalProperties?.GetRawText())).ToArray();

    public static DesignActivityPresentationMaterial[] PresentationMaterial(IReadOnlyCollection<ActivityPresentationRecord> records) =>
        records.Select(record => new DesignActivityPresentationMaterial(
            record.NodeId,
            record.DisplayName,
            record.Description)).ToArray();

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

    public static IReadOnlyCollection<DesignMetadataRecord> ReadLayout(string? json) => ReadJsonCollection<DesignMetadataRecord>(json, "workflow layout");

    public static IReadOnlyCollection<ActivityPresentationRecord> ReadPresentation(string? json) => ReadJsonCollection<ActivityPresentationRecord>(json, "workflow activity presentation");

    private static IReadOnlyCollection<T> ReadJsonCollection<T>(string? json, string identity)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try { return ReadJson<T[]>(json); }
        catch (DesignPersistenceException) { throw; }
        catch (Exception exception) when (IsSerializationFailure(exception))
        { throw new DesignPersistenceException(DesignPersistenceDomain.Workflow, DesignPersistenceFailureKind.Serialization, "workflow.layout.read", identity, exception); }
    }

    public static void SetLayout(DbContext context, object row, IReadOnlyCollection<DesignMetadataRecord> records, IReadOnlyCollection<ActivityPresentationRecord>? presentation = null)
    {
        context.Entry(row).Property("RecordsJson").CurrentValue = Json(records.ToArray());
        context.Entry(row).Property("ActivityPresentationJson").CurrentValue = Json((presentation ?? []).ToArray());
    }
}

internal sealed record DesignLayoutMaterial(
    string NodeId,
    double X,
    double Y,
    double? Width,
    double? Height,
    string? AdditionalPropertiesJson);

internal sealed record DesignActivityPresentationMaterial(
    string NodeId,
    string? DisplayName,
    string? Description);

using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

internal static class GroundworkV2WorkflowActivationSlotStorageConventions
{
    public static StorageValues Values(WorkflowActivationSlot slot, bool clearInactiveProjection = false)
    {
        Validate(slot);
        var projections = Projections(slot);
        // Groundwork row updates are sparse: omitting a column preserves its prior value. A
        // deactivation therefore needs one explicit null assignment to clear an existing owner;
        // new inactive rows and the public projection contract remain omission-based.
        if (clearInactiveProjection && slot.ActiveActivationId is null)
            projections = new Dictionary<string, object?>(projections, StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowActivationSlotActiveActivationIdField] = null
            };
        return GroundworkRuntimeRowStore.Values(
            slot.SlotId,
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(slot),
            projections);
    }

    public static IReadOnlyDictionary<string, object?> Projections(WorkflowActivationSlot slot)
    {
        var projections = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDefinitionIdField] = slot.WorkflowDefinitionId,
            [ElsaRuntimeV2StorageManifest.WorkflowActivationSlotNameField] = slot.SlotName
        };
        if (slot.ActiveActivationId is not null)
            projections[ElsaRuntimeV2StorageManifest.WorkflowActivationSlotActiveActivationIdField] = slot.ActiveActivationId;
        return projections;
    }

    public static WorkflowActivationSlot Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schema = ReadString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schema, ElsaRuntimeV2StorageManifest.SchemaVersion))
            throw new InvalidDataException($"Groundwork activation-slot row returned unsupported schema version '{schema}'.");
        var raw = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var value) ? value switch
        {
            string text => text,
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            _ => throw new InvalidDataException("Groundwork activation-slot row content is not JSON.")
        } : throw new InvalidDataException("Groundwork activation-slot row did not contain JSON content.");
        var slot = GroundworkV2RuntimeJson.Deserialize<WorkflowActivationSlot>(raw)
                   ?? throw new InvalidDataException("Groundwork activation-slot content was empty.");
        Validate(slot);
        Ensure(values, ElsaRuntimeV2StorageManifest.IdField, slot.SlotId);
        Ensure(values, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotDefinitionIdField, slot.WorkflowDefinitionId);
        Ensure(values, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotNameField, slot.SlotName);
        EnsureNullable(values, ElsaRuntimeV2StorageManifest.WorkflowActivationSlotActiveActivationIdField, slot.ActiveActivationId);
        return slot;
    }

    public static void Validate(WorkflowActivationSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot.SlotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot.WorkflowDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot.SlotName);
        ArgumentOutOfRangeException.ThrowIfNegative(slot.Revision);
        if (!StringComparer.Ordinal.Equals(slot.SlotId, WorkflowActivationSlotIdentity.Create(slot.WorkflowDefinitionId, slot.SlotName)))
            throw new ArgumentException("Activation slot identity does not match its definition and lane.", nameof(slot));
        if (slot.ActiveActivationId is null && slot.Source is not null)
            throw new ArgumentException("An inactive activation slot cannot retain an owner.", nameof(slot));
        if (slot.ActiveActivationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(slot.ActiveActivationId);
        if (slot.ActiveActivationId is not null && slot.Source is null)
            throw new ArgumentException("An active activation slot must retain its owner.", nameof(slot));
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> values, string field) => values.TryGetValue(field, out var value) switch
    {
        true when value is string text && !string.IsNullOrWhiteSpace(text) => text,
        true when value is JsonElement { ValueKind: JsonValueKind.String } element && !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString()!,
        _ => throw new InvalidDataException($"Groundwork activation-slot row is missing required string field '{field}'.")
    };

    private static void Ensure(IReadOnlyDictionary<string, object?> values, string field, string expected)
    {
        if (!values.TryGetValue(field, out var value) || !StringComparer.Ordinal.Equals(value switch { string text => text, JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(), _ => null }, expected))
            throw new InvalidDataException($"Groundwork activation-slot projection '{field}' does not match its content.");
    }

    private static void EnsureNullable(IReadOnlyDictionary<string, object?> values, string field, string? expected)
    {
        if (!values.TryGetValue(field, out var value))
        {
            if (expected is null) return;
            throw new InvalidDataException($"Groundwork activation-slot projection '{field}' does not match its content.");
        }
        var actual = value switch { string text => text, JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(), JsonElement { ValueKind: JsonValueKind.Null } => null, null or DBNull => null, _ => null };
        if (!StringComparer.Ordinal.Equals(actual, expected)) throw new InvalidDataException($"Groundwork activation-slot projection '{field}' does not match its content.");
    }
}

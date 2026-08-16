using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current trigger-binding row envelope and lookup projections.</summary>
internal static class GroundworkV2WorkflowTriggerBindingStorageConventions
{
    public static StorageValues Values(WorkflowTriggerBinding binding) =>
        GroundworkRuntimeRowStore.Values(
            binding.TriggerBindingId,
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(binding),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.TriggerBindingIdField] = binding.TriggerBindingId,
                [ElsaRuntimeV2StorageManifest.ArtifactIdField] = binding.ArtifactId,
                [ElsaRuntimeV2StorageManifest.PublicationIdField] = binding.PublicationId,
                [ElsaRuntimeV2StorageManifest.StimulusHashField] = binding.StimulusHash,
                [ElsaRuntimeV2StorageManifest.StimulusTypeField] = binding.StimulusType,
                [ElsaRuntimeV2StorageManifest.StimulusLookupKeyField] =
                    GroundworkV2BookmarkStorageConventions.StimulusLookupKey(binding.StimulusType, binding.StimulusHash),
                [ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField] =
                    GroundworkV2BookmarkStorageConventions.StimulusTypeLookupKey(binding.StimulusType),
                [ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingIsActiveField] = binding.IsActive
            });

    public static WorkflowTriggerBinding Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork workflow trigger-binding row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork workflow trigger-binding row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork workflow trigger-binding row did not contain JSON content.");

        WorkflowTriggerBinding binding;
        try
        {
            binding = GroundworkV2RuntimeJson.Deserialize<WorkflowTriggerBinding>(content)
                ?? throw new InvalidDataException("Groundwork workflow trigger-binding row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Groundwork workflow trigger-binding row content was not valid current JSON.", exception);
        }

        Validate(binding);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.IdField, binding.TriggerBindingId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.TriggerBindingIdField, binding.TriggerBindingId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.ArtifactIdField, binding.ArtifactId);
        EnsureOptionalProjection(values, ElsaRuntimeV2StorageManifest.PublicationIdField, binding.PublicationId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.StimulusHashField, binding.StimulusHash);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.StimulusTypeField, binding.StimulusType);
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.StimulusLookupKeyField,
            GroundworkV2BookmarkStorageConventions.StimulusLookupKey(binding.StimulusType, binding.StimulusHash));
        EnsureProjection(
            values,
            ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField,
            GroundworkV2BookmarkStorageConventions.StimulusTypeLookupKey(binding.StimulusType));
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowTriggerBindingIsActiveField, binding.IsActive);
        return binding;
    }

    public static void Validate(WorkflowTriggerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        WorkflowTriggerBinding.ValidateId(binding.TriggerBindingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.DefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.ArtifactVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.ArtifactHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.ExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.StimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding.StimulusHash);
        ArgumentNullException.ThrowIfNull(binding.Metadata);
        if (binding.PublicationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.PublicationId);
        if (binding.SlotId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(binding.SlotId);
        if (binding.PublicationId is null && binding.SlotId is not null)
            throw new ArgumentException("A trigger binding slot requires a publication.", nameof(binding));
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        object expected)
    {
        if (!values.TryGetValue(field, out var actual) || !Equivalent(actual, expected))
            throw new InvalidDataException(
                $"Groundwork workflow trigger-binding row projection '{field}' does not match its current content.");
    }

    private static void EnsureOptionalProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string? expected)
    {
        if (!values.TryGetValue(field, out var actual))
            throw new InvalidDataException(
                $"Groundwork workflow trigger-binding row is missing projection '{field}'.");
        if (expected is null)
        {
            if (actual is not null && actual is not JsonElement { ValueKind: JsonValueKind.Null })
                throw new InvalidDataException(
                    $"Groundwork workflow trigger-binding row projection '{field}' does not match its current content.");
            return;
        }

        EnsureProjection(values, field, expected);
    }

    private static bool Equivalent(object? actual, object expected) =>
        actual switch
        {
            JsonElement element when expected is string text =>
                element.ValueKind == JsonValueKind.String && element.GetString() == text,
            JsonElement element when expected is bool boolean =>
                element.ValueKind is JsonValueKind.True or JsonValueKind.False && element.GetBoolean() == boolean,
            _ => Equals(actual, expected)
        };

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field)
    {
        if (values.TryGetValue(field, out var value))
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
                return text;
            if (value is JsonElement { ValueKind: JsonValueKind.String } element &&
                !string.IsNullOrWhiteSpace(element.GetString()))
            {
                return element.GetString()!;
            }
        }

        throw new InvalidDataException(
            $"Groundwork workflow trigger-binding row is missing required string field '{field}'.");
    }
}

internal sealed record GroundworkV2WorkflowTriggerBindingProjectionState(
    string ProjectionKind,
    string PublicationId,
    bool IsActive);

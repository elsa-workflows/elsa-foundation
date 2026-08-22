using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Owns the current source-reference row envelope and its bounded lookup projections.</summary>
internal static class GroundworkV2WorkflowExecutableSourceReferenceStorageConventions
{
    public static StorageValues Values(WorkflowExecutableSourceReference reference)
    {
        Validate(reference);
        return GroundworkRuntimeRowStore.Values(
            reference.SourceReferenceId,
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(reference),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] =
                    ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
                [ElsaRuntimeV2StorageManifest.ArtifactIdField] = reference.ArtifactId,
                [ElsaRuntimeV2StorageManifest.ScopeField] = reference.Scope.ToString(),
                [ElsaRuntimeV2StorageManifest.ExpiresAtField] = reference.ExpiresAt ?? DateTimeOffset.MaxValue,
                [ElsaRuntimeV2StorageManifest.IsRetiredField] = reference.DeletedAt is not null,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceIdField] = reference.SourceReferenceId,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDefinitionIdField] = reference.DefinitionId
            });
    }

    public static WorkflowExecutableSourceReference Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        var schemaVersion = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schemaVersion, ElsaRuntimeV2StorageManifest.SchemaVersion))
        {
            throw new InvalidDataException(
                $"Groundwork source-reference row returned unsupported schema version '{schemaVersion}'; " +
                $"this adapter requires '{ElsaRuntimeV2StorageManifest.SchemaVersion}'.");
        }

        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException("Groundwork source-reference row content is not JSON.")
            }
            : throw new InvalidDataException("Groundwork source-reference row did not contain JSON content.");

        WorkflowExecutableSourceReference reference;
        try
        {
            reference = GroundworkV2RuntimeJson.Deserialize<WorkflowExecutableSourceReference>(content)
                ?? throw new InvalidDataException("Groundwork source-reference row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Groundwork source-reference row content was not valid current JSON.", exception);
        }

        Validate(reference);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.IdField, reference.SourceReferenceId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.CollectionField,
            ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.ArtifactIdField, reference.ArtifactId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.ScopeField, reference.Scope.ToString());
        EnsureDateTimeProjection(values, ElsaRuntimeV2StorageManifest.ExpiresAtField,
            reference.ExpiresAt ?? DateTimeOffset.MaxValue);
        EnsureBooleanProjection(values, ElsaRuntimeV2StorageManifest.IsRetiredField, reference.DeletedAt is not null);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceIdField,
            reference.SourceReferenceId);
        EnsureProjection(values, ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDefinitionIdField,
            reference.DefinitionId);
        return reference;
    }

    public static void Validate(WorkflowExecutableSourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.SourceReferenceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.SourceKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.DefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.DefinitionVersionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference.ArtifactVersion);
        if (reference.SourceVersion is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(reference.SourceVersion);
        if (!Enum.IsDefined(reference.Scope))
            throw new ArgumentOutOfRangeException(nameof(reference.Scope), reference.Scope,
                "The source-reference scope is not defined.");
    }

    private static void EnsureProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        string expected)
    {
        var actual = RequiredString(values, field);
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidDataException(
                $"Groundwork source-reference row projection '{field}' does not match its current content.");
    }

    private static void EnsureBooleanProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        bool expected)
    {
        if (!values.TryGetValue(field, out var value))
            throw new InvalidDataException($"Groundwork source-reference row is missing projection '{field}'.");

        var actual = value switch
        {
            bool boolean => boolean,
            JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } element => element.GetBoolean(),
            string text when bool.TryParse(text, out var boolean) => boolean,
            _ => throw new InvalidDataException(
                $"Groundwork source-reference row projection '{field}' is not Boolean.")
        };
        if (actual != expected)
            throw new InvalidDataException(
                $"Groundwork source-reference row projection '{field}' does not match its current content.");
    }

    private static void EnsureDateTimeProjection(
        IReadOnlyDictionary<string, object?> values,
        string field,
        DateTimeOffset expected)
    {
        if (!values.TryGetValue(field, out var value))
            throw new InvalidDataException($"Groundwork source-reference row is missing projection '{field}'.");

        DateTimeOffset actual;
        try
        {
            actual = value switch
            {
                DateTimeOffset timestamp => timestamp,
                DateTime timestamp => new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
                JsonElement { ValueKind: JsonValueKind.String } element =>
                    DateTimeOffset.Parse(element.GetString()!, CultureInfo.InvariantCulture),
                string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture),
                _ => throw new InvalidDataException(
                    $"Groundwork source-reference row projection '{field}' is not DateTimeOffset.")
            };
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException(
                $"Groundwork source-reference row projection '{field}' is not DateTimeOffset.", exception);
        }

        if (actual != expected)
            throw new InvalidDataException(
                $"Groundwork source-reference row projection '{field}' does not match its current content.");
    }

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
            $"Groundwork source-reference row is missing required string field '{field}'.");
    }
}

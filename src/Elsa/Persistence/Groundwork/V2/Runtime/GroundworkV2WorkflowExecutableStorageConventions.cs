using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;
using System.Text.Json;

namespace Elsa.Persistence.Groundwork.Runtime;

internal static class GroundworkV2WorkflowExecutableStorageConventions
{
    public static StorageValues Values(WorkflowExecutable executable)
    {
        Validate(executable);
        return GroundworkRuntimeRowStore.Values(
            executable.Identity.ArtifactId,
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(executable),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.CollectionField] = ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
                [ElsaRuntimeV2StorageManifest.WorkflowExecutableArtifactIdField] = executable.Identity.ArtifactId
            });
    }

    public static StorageValues EmptyCoordinationValues(string artifactId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        return CoordinationValues(artifactId, CoordinationState.Empty);
    }

    public static StorageValues CoordinationValues(string artifactId, CoordinationState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentNullException.ThrowIfNull(state);
        return GroundworkRuntimeRowStore.Values(
            artifactId,
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            GroundworkV2RuntimeJson.Serialize(state));
    }

    public static WorkflowExecutable Deserialize(IReadOnlyDictionary<string, object?> values)
    {
        EnsureSchema(values, "workflow executable");
        var executable = DeserializeContent<WorkflowExecutable>(values, "workflow executable");
        Validate(executable);
        EnsureString(values, ElsaRuntimeV2StorageManifest.IdField, executable.Identity.ArtifactId);
        EnsureString(values, ElsaRuntimeV2StorageManifest.CollectionField, ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind);
        EnsureString(values, ElsaRuntimeV2StorageManifest.WorkflowExecutableArtifactIdField, executable.Identity.ArtifactId);
        return executable;
    }

    public static CoordinationState DeserializeCoordination(
        IReadOnlyDictionary<string, object?> values,
        string expectedArtifactId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedArtifactId);
        EnsureSchema(values, "workflow executable coordination");
        var state = DeserializeContent<CoordinationState>(values, "workflow executable coordination");
        ValidateCoordination(state);
        EnsureString(values, ElsaRuntimeV2StorageManifest.IdField, expectedArtifactId);
        return state;
    }

    public static void Validate(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactId);
    }

    private static void ValidateCoordination(CoordinationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.RootWriteLeases is null)
            throw new InvalidDataException("Groundwork workflow executable coordination omitted its root-write leases.");
        foreach (var (leaseId, lease) in state.RootWriteLeases)
        {
            if (lease is null || string.IsNullOrWhiteSpace(leaseId) ||
                !StringComparer.Ordinal.Equals(leaseId, lease.LeaseId) ||
                string.IsNullOrWhiteSpace(lease.FencingToken))
            {
                throw new InvalidDataException("Groundwork workflow executable coordination contains an invalid root-write lease.");
            }
        }

        if (state.DeletionGuard is { } guard &&
            (string.IsNullOrWhiteSpace(guard.OperationId) || string.IsNullOrWhiteSpace(guard.FencingToken)))
        {
            throw new InvalidDataException("Groundwork workflow executable coordination contains an invalid deletion guard.");
        }
    }

    private static T DeserializeContent<T>(IReadOnlyDictionary<string, object?> values, string rowKind)
    {
        var content = values.TryGetValue(ElsaRuntimeV2StorageManifest.ContentField, out var rawContent)
            ? rawContent switch
            {
                string text => text,
                JsonElement element => element.GetRawText(),
                JsonDocument document => document.RootElement.GetRawText(),
                _ => throw new InvalidDataException($"Groundwork {rowKind} row content is not JSON.")
            }
            : throw new InvalidDataException($"Groundwork {rowKind} row did not contain JSON content.");
        try
        {
            return GroundworkV2RuntimeJson.Deserialize<T>(content)
                   ?? throw new InvalidDataException($"Groundwork {rowKind} row content was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Groundwork {rowKind} row content was not valid current JSON.", exception);
        }
    }

    private static void EnsureSchema(IReadOnlyDictionary<string, object?> values, string rowKind)
    {
        var schema = RequiredString(values, ElsaRuntimeV2StorageManifest.SchemaVersionField);
        if (!StringComparer.Ordinal.Equals(schema, ElsaRuntimeV2StorageManifest.SchemaVersion))
            throw new InvalidDataException($"Groundwork {rowKind} row returned unsupported schema version '{schema}'.");
    }

    private static void EnsureString(IReadOnlyDictionary<string, object?> values, string field, string expected)
    {
        if (!StringComparer.Ordinal.Equals(RequiredString(values, field), expected))
            throw new InvalidDataException($"Groundwork workflow executable row projection '{field}' does not match its current content.");
    }

    private static string RequiredString(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) switch
        {
            true when value is string text && !string.IsNullOrWhiteSpace(text) => text,
            true when value is JsonElement { ValueKind: JsonValueKind.String } element &&
                       !string.IsNullOrWhiteSpace(element.GetString()) => element.GetString()!,
            _ => throw new InvalidDataException($"Groundwork workflow executable row is missing required string field '{field}'.")
        };

    internal sealed record CoordinationState(
        IReadOnlyDictionary<string, RootWriteLeaseState> RootWriteLeases,
        DeletionGuardState? DeletionGuard)
    {
        public static CoordinationState Empty { get; } =
            new(new Dictionary<string, RootWriteLeaseState>(StringComparer.Ordinal), null);
    }

    internal sealed record RootWriteLeaseState(string LeaseId, string FencingToken, DateTimeOffset ExpiresAt);

    internal sealed record DeletionGuardState(string OperationId, string FencingToken, DateTimeOffset ExpiresAt);

}

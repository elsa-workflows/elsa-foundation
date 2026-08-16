using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Shared current bookmark identity, JSON, and lookup projection conventions.</summary>
internal static class GroundworkV2BookmarkStorageConventions
{
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    public static string PhysicalId(string workflowExecutionId, string bookmarkId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
        EnsureProjectionLength(workflowExecutionId, nameof(workflowExecutionId));
        EnsureProjectionLength(bookmarkId, nameof(bookmarkId));
        var id = string.Concat(
            workflowExecutionId.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            workflowExecutionId,
            bookmarkId.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            bookmarkId);
        if (id.Length > ElsaRuntimeV2StorageManifest.IdMaximumLength)
        {
            throw new InvalidOperationException(
                $"Groundwork bookmark physical identity exceeds the admitted ID length of {ElsaRuntimeV2StorageManifest.IdMaximumLength}.");
        }

        return id;
    }

    public static StorageValues Values(BookmarkState state) =>
        GroundworkRuntimeRowStore.Values(
            PhysicalId(state.WorkflowExecutionId, state.BookmarkId),
            ElsaRuntimeV2StorageManifest.SchemaVersion,
            Serialize(state),
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField] = state.WorkflowExecutionId,
                [ElsaRuntimeV2StorageManifest.StimulusHashField] = state.StimulusHash,
                [ElsaRuntimeV2StorageManifest.StimulusTypeField] = state.StimulusType,
                [ElsaRuntimeV2StorageManifest.StimulusLookupKeyField] = StimulusLookupKey(state.StimulusType, state.StimulusHash),
                [ElsaRuntimeV2StorageManifest.StimulusTypeLookupKeyField] = StimulusTypeLookupKey(state.StimulusType),
                [ElsaRuntimeV2StorageManifest.BookmarkIdField] = state.BookmarkId
            });

    private static void EnsureProjectionLength(string value, string parameterName)
    {
        if (value.Length > ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"Groundwork bookmark identity parts cannot exceed {ElsaRuntimeV2StorageManifest.RuntimeExecutionIdProjectionLength} characters.");
        }
    }

    public static string StimulusLookupKey(string stimulusType, string stimulusHash) =>
        Hash(string.Concat(
            stimulusType.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            stimulusType,
            stimulusHash));

    public static string StimulusTypeLookupKey(string stimulusType) => Hash(stimulusType);

    public static string Serialize(BookmarkState state) => JsonSerializer.Serialize(state, Json);

    public static BookmarkState Deserialize(string content) =>
        JsonSerializer.Deserialize<BookmarkState>(content, Json) ??
        throw new InvalidDataException("Groundwork bookmark row content was empty.");

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

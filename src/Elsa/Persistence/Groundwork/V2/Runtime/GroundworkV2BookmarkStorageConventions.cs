using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>Shared current bookmark identity, JSON, and lookup projection conventions.</summary>
internal static class GroundworkV2BookmarkStorageConventions
{
    public static string PhysicalId(string workflowExecutionId, string bookmarkId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
        return GroundworkV2CompositeIdentityCodec.From(workflowExecutionId, bookmarkId);
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

    public static string StimulusLookupKey(string stimulusType, string stimulusHash) =>
        Hash(string.Concat(
            stimulusType.Length.ToString(CultureInfo.InvariantCulture),
            ":",
            stimulusType,
            stimulusHash));

    public static string StimulusTypeLookupKey(string stimulusType) => Hash(stimulusType);

    public static string Serialize(BookmarkState state) => GroundworkV2RuntimeJson.Serialize(state);

    public static BookmarkState Deserialize(string content) =>
        GroundworkV2RuntimeJson.Deserialize<BookmarkState>(content) ??
        throw new InvalidDataException("Groundwork bookmark row content was empty.");

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

}

using Elsa.UnifiedExecution.WorkProof;

namespace Elsa.UnifiedExecution.ModelContracts;

/// <summary>Artifact bytes and their externally pinned identity, shared by the host and model package.</summary>
public sealed record PinnedModelArtifact(
    string ModelId,
    string ModelVersion,
    string StateSchema,
    string Json,
    string Fingerprint);

/// <summary>Host-readable inspection summary; model-specific detail is returned as canonical JSON.</summary>
public sealed record ModelInspection(
    string ModelId,
    string ModelVersion,
    string StateSchema,
    string ArtifactFingerprint,
    string Status,
    string DetailJson);

public sealed record ModelWorkHandler(string Id, string Version);

/// <summary>Extension seam owned by this experiment, not a change to the WorkProof substrate.</summary>
public interface IExecutionModelPackage
{
    string PackageId { get; }
    string PackageVersion { get; }
    IReadOnlyCollection<string> SupportedStateSchemas { get; }
    IReadOnlyCollection<ModelWorkHandler> WorkHandlers { get; }

    ICompositionPolicy CreatePolicy(PinnedModelArtifact artifact);
    string ExecuteWork(PinnedModelArtifact artifact, string handlerId, string handlerVersion, string inputJson);
    void ValidateState(PinnedModelArtifact artifact, string stateJson);
    ModelInspection Inspect(PinnedModelArtifact artifact, string stateJson);
}

using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Replacement port for exact immutable material staging. Legacy providers and decorators refuse every operation by default.</summary>
/// <remarks>The host selects exactly one applicable owner per scope and rejects duplicates before I/O/effects; no keyed strategy or default owner is implied.</remarks>
public interface IRuntimeMaterialStagingStore
{
    ValueTask<RuntimeMaterialStageReceipt> RegisterAsync(RuntimeMaterialStageCommand command, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeMaterialStageReceipt>(new RuntimeMaterialStagingException("runtime_stage_profile_unsupported", true));
    ValueTask<RuntimeMaterialStageReceipt> StageAsync(RuntimeMaterialStageCommand command, long fence, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeMaterialStageReceipt>(new RuntimeMaterialStagingException("runtime_stage_profile_unsupported", true));
    ValueTask<RuntimeMaterialStageReceipt?> FindReceiptAsync(string commandId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeMaterialStageReceipt?>(new RuntimeMaterialStagingException("runtime_stage_profile_unsupported", true));
    ValueTask<RuntimeMaterialStageReceipt> AdvanceFenceAsync(RuntimeMaterialStageCommand command, long expectedFence, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeMaterialStageReceipt>(new RuntimeMaterialStagingException("runtime_stage_profile_unsupported", true));
    ValueTask<RuntimeMaterialStageReceipt> RevokeAsync(RuntimeMaterialStageCommand command, long expectedFence, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeMaterialStageReceipt>(new RuntimeMaterialStagingException("runtime_stage_profile_unsupported", true));
}

/// <summary>Required current host authorization, independent of source provenance or a saved actor identifier.</summary>
/// <remarks>Replacement port: select exactly one applicable live host policy per scope and reject duplicate selections before owner I/O/effects. There is no default grant.</remarks>
public interface IRuntimeMaterialStagingAuthorization
{
    ValueTask<bool> AuthorizeAsync(RuntimeMaterialStageCommand command, CancellationToken cancellationToken = default);
    ValueTask<bool> AuthorizeReadAsync(RuntimeDeploymentScope targetScope, string commandId, CancellationToken cancellationToken = default);
}

/// <summary>Actual owner retention used by collection. Missing/unknown evidence must retain conservatively.</summary>
/// <remarks>Replacement port: select the one actual material-owner retention reader per scope and reject duplicate selections. This is not an aggregating contributor contract.</remarks>
public interface IRuntimeMaterialStageRetentionReader
{
    ValueTask<IReadOnlyList<RuntimeMaterialStageRetentionRoot>> ReadStageRetentionRootsAsync(int maximumRoots, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<IReadOnlyList<RuntimeMaterialStageRetentionRoot>>(new RuntimeMaterialStagingException("runtime_stage_retention_profile_unsupported", true));
}

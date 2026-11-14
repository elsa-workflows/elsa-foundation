using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Opt-in exact immutable material staging. Legacy providers and decorators refuse every operation by default.</summary>
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
public interface IRuntimeMaterialStagingAuthorization
{
    ValueTask<bool> AuthorizeAsync(RuntimeMaterialStageCommand command, CancellationToken cancellationToken = default);
    ValueTask<bool> AuthorizeReadAsync(RuntimeDeploymentScope targetScope, string commandId, CancellationToken cancellationToken = default);
}

/// <summary>Actual owner retention used by collection. Missing/unknown evidence must retain conservatively.</summary>
public interface IRuntimeMaterialStageRetentionReader
{
    ValueTask<IReadOnlyList<RuntimeMaterialStageRetentionRoot>> ReadStageRetentionRootsAsync(int maximumRoots, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<IReadOnlyList<RuntimeMaterialStageRetentionRoot>>(new RuntimeMaterialStagingException("runtime_stage_retention_profile_unsupported", true));
}

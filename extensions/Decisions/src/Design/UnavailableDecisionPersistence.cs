using Elsa.Decisions.Persistence.Core;

namespace Elsa.Decisions.Design;

/// <summary>Composition remains inspectable without a selected store; all operations fail closed.</summary>
internal sealed class UnavailableDecisionPersistence : IDecisionReader, IDecisionWriter
{
    private static ValueTask<T> Unavailable<T>() => ValueTask.FromException<T>(new DecisionDesignException("unavailable", "This host has no selected Decisions persistence provider."));
    public ValueTask<IReadOnlyList<DecisionAsset>> ListAssetsAsync(string tenantId, CancellationToken cancellationToken = default) => Unavailable<IReadOnlyList<DecisionAsset>>();
    public ValueTask<DecisionAsset?> FindAssetAsync(string tenantId, string assetId, CancellationToken cancellationToken = default) => Unavailable<DecisionAsset?>();
    public ValueTask<IReadOnlyList<DecisionVersion>> ListVersionsAsync(string tenantId, string assetId, CancellationToken cancellationToken = default) => Unavailable<IReadOnlyList<DecisionVersion>>();
    public ValueTask<DecisionVersion?> FindVersionAsync(string tenantId, string assetId, string versionId, CancellationToken cancellationToken = default) => Unavailable<DecisionVersion?>();
    public ValueTask<IReadOnlyList<DecisionCase>> ListCasesAsync(string tenantId, string assetId, CancellationToken cancellationToken = default) => Unavailable<IReadOnlyList<DecisionCase>>();
    public ValueTask<DecisionCase?> FindCaseAsync(string tenantId, string assetId, string caseId, CancellationToken cancellationToken = default) => Unavailable<DecisionCase?>();
    public ValueTask<IReadOnlyList<DecisionTestReceipt>> ListRunsAsync(string tenantId, string assetId, CancellationToken cancellationToken = default) => Unavailable<IReadOnlyList<DecisionTestReceipt>>();
    public ValueTask<DecisionTestReceipt?> FindRunAsync(string tenantId, string assetId, string runId, CancellationToken cancellationToken = default) => Unavailable<DecisionTestReceipt?>();
    public ValueTask<DecisionOperationReceipt?> FindOperationAsync(DecisionOperation operation, CancellationToken cancellationToken = default) => Unavailable<DecisionOperationReceipt?>();
    public ValueTask<DecisionTestReceipt?> FindCompletedOperationAsync(DecisionOperation operation, string assetId, CancellationToken cancellationToken = default) => Unavailable<DecisionTestReceipt?>();
    public ValueTask<DecisionWriteConfirmation> SaveAssetAsync(DecisionOperation operation, SaveDecision command, CancellationToken cancellationToken = default) => Unavailable<DecisionWriteConfirmation>();
    public ValueTask<DecisionWriteConfirmation> PublishAsync(DecisionOperation operation, PublishDecision command, CancellationToken cancellationToken = default) => Unavailable<DecisionWriteConfirmation>();
    public ValueTask<DecisionWriteConfirmation> SaveCaseAsync(DecisionOperation operation, SaveDecisionCase command, CancellationToken cancellationToken = default) => Unavailable<DecisionWriteConfirmation>();
    public ValueTask<DecisionWriteConfirmation> DeleteCaseAsync(DecisionOperation operation, string assetId, string caseId, string expectedRevision, CancellationToken cancellationToken = default) => Unavailable<DecisionWriteConfirmation>();
    public ValueTask<DecisionWriteConfirmation> SaveCompletedRunAsync(DecisionOperation operation, DecisionTestReceipt receipt, CancellationToken cancellationToken = default) => Unavailable<DecisionWriteConfirmation>();
    public ValueTask<DecisionWriteConfirmation> DeleteRunAsync(DecisionOperation operation, string assetId, string runId, CancellationToken cancellationToken = default) => Unavailable<DecisionWriteConfirmation>();
}

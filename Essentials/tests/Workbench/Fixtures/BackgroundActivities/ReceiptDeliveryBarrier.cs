using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workbench.Tests.BackgroundActivities;

/// <summary>
/// A fault-injection barrier after the real provider's receipt commit and before notification. It changes no durable
/// data. Killing the owned host here proves that the production repair sweep can recover an unannounced receipt.
/// </summary>
internal sealed class ReceiptDeliveryBarrier(IBackgroundActivityWorkStore inner) : IBackgroundActivityWorkStore
{
    private static string Journal => Path.Combine(Environment.CurrentDirectory, "background-witness");
    private static bool HoldDelivery => File.Exists(Path.Combine(Journal, "hold-receipts"));

    public ValueTask<BackgroundActivityWorkItem?> FindAsync(string workflowExecutionId, string workId, CancellationToken cancellationToken = default) =>
        inner.FindAsync(workflowExecutionId, workId, cancellationToken);

    public ValueTask<BackgroundActivityWorkClaim?> ClaimNextAsync(string ownerId, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) =>
        inner.ClaimNextAsync(ownerId, now, visibilityTimeout, cancellationToken);

    public ValueTask<BackgroundActivityWorkClaim?> RenewClaimAsync(BackgroundActivityWorkClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) =>
        inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

    public async ValueTask<BackgroundActivityWorkItem> RecordResultAsync(BackgroundActivityWorkClaim claim,
        BackgroundActivityWorkTerminalResult result, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var receipt = await inner.RecordResultAsync(claim, result, now, cancellationToken);
        Directory.CreateDirectory(Journal);
        await File.WriteAllTextAsync(Path.Combine(Journal, receipt.Request.ExecutableNodeId + ".receipt"), receipt.ReceiptDigest, cancellationToken);
        while (HoldDelivery)
            await Task.Delay(25, cancellationToken);
        return receipt;
    }

    public ValueTask<IReadOnlyCollection<BackgroundActivityWorkItem>> ListResultReadyAsync(int limit,
        string? workflowExecutionId = null, CancellationToken cancellationToken = default) =>
        HoldDelivery ? ValueTask.FromResult<IReadOnlyCollection<BackgroundActivityWorkItem>>([]) :
            inner.ListResultReadyAsync(limit, workflowExecutionId, cancellationToken);
}

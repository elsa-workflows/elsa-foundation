using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workbench.Tests.BackgroundActivities;

/// <summary>
/// Captures the real worker claims used by the process-recovery witnesses, replays a cancelled claim when explicitly
/// armed, and can hold a real receipt after commit but before notification to simulate process loss.
/// </summary>
internal sealed class ReceiptDeliveryBarrier(IBackgroundActivityWorkStore inner) : IBackgroundActivityWorkStore
{
    private static string Journal => Path.Combine(Environment.CurrentDirectory, "background-witness");
    private static bool HoldDelivery => File.Exists(Path.Combine(Journal, "hold-receipts"));

    public ValueTask<BackgroundActivityWorkItem?> FindAsync(string workflowExecutionId, string workId, CancellationToken cancellationToken = default) =>
        inner.FindAsync(workflowExecutionId, workId, cancellationToken);

    public async ValueTask<BackgroundActivityWorkClaim?> ClaimNextAsync(string ownerId, DateTimeOffset now,
        TimeSpan visibilityTimeout, CancellationToken cancellationToken = default)
    {
        if (File.Exists(Path.Combine(Journal, "attempt-late-receipt-after-restart")))
            await ReplayCancelledClaimsAsync(cancellationToken);

        var claim = await inner.ClaimNextAsync(ownerId, now, visibilityTimeout, cancellationToken);
        if (claim is null)
            return null;

        // Keep the first real claim for each admitted body. It is the worker's pre-cancellation lease,
        // not a test-constructed approximation of the claim protocol.
        var item = await inner.FindAsync(claim.WorkflowExecutionId, claim.WorkId, cancellationToken);
        if (item is not null)
        {
            Directory.CreateDirectory(Journal);
            var claimPath = Path.Combine(Journal, item.Request.ExecutableNodeId + ".claim.json");
            if (!File.Exists(claimPath))
                await WriteJsonAtomicallyAsync(claimPath, claim, cancellationToken);
        }

        return claim;
    }

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

    private async Task ReplayCancelledClaimsAsync(CancellationToken cancellationToken)
    {
        foreach (var key in new[] { "left", "right" })
        {
            var claimPath = Path.Combine(Journal, key + ".claim.json");
            var resultPath = Path.Combine(Journal, key + ".late-receipt.json");
            if (!File.Exists(claimPath) || File.Exists(resultPath))
                continue;

            var claim = JsonSerializer.Deserialize<BackgroundActivityWorkClaim>(await File.ReadAllTextAsync(claimPath, cancellationToken))
                ?? throw new InvalidDataException($"The captured {key} background claim was empty.");
            try
            {
                await inner.RecordResultAsync(claim,
                    new BackgroundActivityWorkTerminalResult(BackgroundActivityWorkResultKind.Complete,
                        resultJson: JsonSerializer.Serialize(key)), DateTimeOffset.UtcNow, cancellationToken);
                throw new InvalidOperationException($"The cancelled {key} claim unexpectedly accepted a late receipt.");
            }
            catch (BackgroundActivityWorkConflictException exception)
            {
                var current = await inner.FindAsync(claim.WorkflowExecutionId, claim.WorkId, cancellationToken);
                await WriteJsonAtomicallyAsync(resultPath, new
                {
                    ProcessId = Environment.ProcessId,
                    claim.WorkflowExecutionId,
                    claim.WorkId,
                    claim.ClaimOwnerId,
                    claim.ClaimFence,
                    claim.Revision,
                    exception.Message,
                    Status = current?.Status.ToString(),
                    HasResult = current?.Result is not null
                }, cancellationToken);
            }
        }
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(value), cancellationToken);
            File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

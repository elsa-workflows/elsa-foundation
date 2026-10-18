using System.Security.Cryptography;
using System.Text;
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

    public async ValueTask<BackgroundActivityWorkClaim?> RenewClaimAsync(BackgroundActivityWorkClaim claim, DateTimeOffset now,
        TimeSpan visibilityTimeout, CancellationToken cancellationToken = default)
    {
        await TryWriteWorkStageAsync(claim, "renewal", "entered");
        try
        {
            var renewed = await inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);
            await TryWriteWorkStageAsync(claim, "renewal", renewed is null ? "lost" : "returned", renewed?.Revision);
            return renewed;
        }
        catch (Exception exception)
        {
            await TryWriteWorkStageAsync(claim, "renewal", "failed", exception: exception);
            throw;
        }
    }

    public async ValueTask<BackgroundActivityWorkItem> RecordResultAsync(BackgroundActivityWorkClaim claim,
        BackgroundActivityWorkTerminalResult result, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await TryWriteWorkStageAsync(claim, "receipt", "entered");

        BackgroundActivityWorkItem receipt;
        try
        {
            receipt = await inner.RecordResultAsync(claim, result, now, cancellationToken);
        }
        catch (Exception exception)
        {
            await TryWriteWorkStageAsync(claim, "receipt", "failed", exception: exception);
            throw;
        }

        Directory.CreateDirectory(Journal);
        await File.WriteAllTextAsync(Path.Combine(Journal, receipt.Request.ExecutableNodeId + ".receipt"), receipt.ReceiptDigest, cancellationToken);
        await TryWriteWorkStageAsync(claim, "receipt", "committed", receipt.Revision, receipt.Status.ToString());
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

    private static async Task TryWriteWorkStageAsync(BackgroundActivityWorkClaim claim, string operation, string stage,
        long? resultRevision = null, string? resultStatus = null, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(Journal);
            // Keep old and replacement claims separate; each file is only that claim's latest observation.
            var workHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{claim.WorkflowExecutionId}|{claim.WorkId}|{claim.ClaimOwnerId}|{claim.ClaimFence}"))).ToLowerInvariant();
            var path = Path.Combine(Journal, $"work-{workHash}.{operation}.json");
            var message = exception?.Message;
            await BackgroundWitnessJournal.TryWriteJsonAtomicallyAsync(path, new
            {
                Stage = stage,
                ProcessId = Environment.ProcessId,
                RecordedAt = DateTimeOffset.UtcNow,
                claim.WorkflowExecutionId,
                claim.WorkId,
                claim.ClaimOwnerId,
                claim.ClaimFence,
                claim.ClaimedAt,
                claim.VisibleUntil,
                PreviousRevision = claim.Revision,
                Revision = resultRevision ?? claim.Revision,
                ResultStatus = resultStatus,
                ExceptionType = exception?.GetType().FullName,
                Message = message is { Length: > 512 } ? message[..512] + "…" : message
            });
        }
        catch (Exception ioException) when (ioException is IOException or UnauthorizedAccessException)
        {
            // Diagnostic filesystem failures must not replace the store outcome.
        }
    }
}

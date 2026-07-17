using System.Collections.Concurrent;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

public sealed class InMemoryActivityPublicationReceiptStore : IActivityPublicationReceiptStore
{
    private readonly ConcurrentDictionary<string, ActivityPublicationReceipt> _receipts =
        new(StringComparer.Ordinal);

    public ValueTask<ActivityPublicationReceipt?> FindAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _receipts.TryGetValue(idempotencyKey, out var receipt);
        return ValueTask.FromResult(receipt);
    }

    public ValueTask<bool> TryCreateAsync(
        ActivityPublicationReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_receipts.TryAdd(receipt.IdempotencyKey, receipt));
    }
}

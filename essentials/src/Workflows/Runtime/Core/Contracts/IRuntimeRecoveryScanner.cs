using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeRecoveryScanner
{
    /// <summary>
    /// Retains the original collection-shaped recovery surface. Implementations must honor
    /// <see cref="RuntimeRecoveryScanRequest.Limit"/>; callers that need to traverse beyond one bounded result
    /// must use <see cref="ScanPageAsync"/> when the scanner advertises
    /// <see cref="IRuntimeRecoveryPagedScanner"/>.
    /// </summary>
    ValueTask<IReadOnlyCollection<RuntimeRecoveryCandidate>> ScanAsync(RuntimeRecoveryScanRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one bounded, stably ordered recovery page. The continuation on the request is opaque and
    /// must be passed unchanged to obtain the next page.
    /// </summary>
    async ValueTask<RuntimeRecoveryPage> ScanPageAsync(
        RuntimeRecoveryScanRequest request,
        CancellationToken cancellationToken = default)
    {
        // Deliberate source compatibility for scanners compiled before the page contract. New production scanners
        // must override this member; the fallback has no continuation and therefore cannot provide complete paging.
        ArgumentNullException.ThrowIfNull(request);
        if (request.ContinuationToken is not null)
        {
            throw new NotSupportedException(
                "This recovery scanner predates the bounded page contract and cannot consume a continuation.");
        }

        var items = await ScanAsync(request, cancellationToken);
        return new RuntimeRecoveryPage(request, items.Take(request.Limit).ToArray());
    }
}

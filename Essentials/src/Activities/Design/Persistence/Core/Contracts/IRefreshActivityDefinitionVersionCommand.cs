using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Activities.Design.Persistence.Core.Contracts;

/// <summary>
/// The single sanctioned in-place write to an activity definition version's write-once
/// <c>Hash</c> and <c>InputsSource</c> columns. Reconciliation uses this when the same
/// <c>(DefinitionId, Version)</c> is contributed again with a hash that differs only by
/// input <c>Tier</c> (or another newly hashed catalog field).
/// Identity, provenance, and every other write-once key stay on the original row.
/// Implementations compare-and-swap on <paramref name="expectedOldHash"/>, stamp a new
/// concurrency token and <c>LastModifiedAt</c>, and treat a row already at the incoming
/// hash as success so two nodes reconciling during a rolling deploy do not clobber each other.
/// </summary>
public interface IRefreshActivityDefinitionVersionCommand
{
    /// <param name="operationKey">
    /// Stable across retries of the same incoming contribution. Implementations may ignore it
    /// when the hash compare-and-swap is the idempotency token.
    /// </param>
    /// <param name="version">The existing row identity with the incoming inputs and hash.</param>
    /// <param name="expectedOldHash">The hash currently believed to be stored. The update matches this value.</param>
    Task RefreshAsync(
        DesignOperationKey operationKey,
        ActivityDefinitionVersion version,
        string? expectedOldHash,
        CancellationToken cancellationToken = default);
}

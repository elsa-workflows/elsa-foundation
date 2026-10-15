using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Activities.Design.Persistence.Core.Contracts;

/// <summary>
/// Replaces the hashed content of an existing activity definition version in place.
/// Reconciliation uses this when the same <c>(DefinitionId, Version)</c> is contributed again
/// with a hash that differs only by input <c>Tier</c> (or another newly hashed catalog field).
/// Identity, provenance, and write-once keys stay on the original row.
/// </summary>
public interface IRefreshActivityDefinitionVersionCommand
{
    Task RefreshAsync(
        DesignOperationKey operationKey,
        ActivityDefinitionVersion version,
        CancellationToken cancellationToken = default);
}

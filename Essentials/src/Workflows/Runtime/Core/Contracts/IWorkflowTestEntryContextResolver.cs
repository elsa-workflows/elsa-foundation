using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Single-provider replacement port for resolving an immutable admitted test context in the current trusted
/// tenant/partition and actor scope. The provider authorizes the caller and returns its exact retained descriptor,
/// or null when unavailable. No default provider exists. Runtime refuses missing or duplicate registrations.
/// </summary>
public interface IWorkflowTestEntryContextResolver
{
    ValueTask<WorkflowTestEntry?> FindAsync(string contextId, CancellationToken cancellationToken = default);
}

using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Single-provider replacement port for exact immutable setup in the current authorized test-entry scope.
/// No default exists; a missing/duplicate source refuses context initialization rather than inventing values.
/// </summary>
public interface IRuntimeTestContextSeedSource
{
    ValueTask<RuntimeTestContextSeed?> ResolveAsync(WorkflowTestEntry entry, WorkflowExecutable executable,
        string workflowExecutionId, CancellationToken cancellationToken = default);
}

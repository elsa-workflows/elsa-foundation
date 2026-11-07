using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Single-provider replacement port for read-only preparation of an authorized exact test context.
/// This does not create a scope, execution, external fixture or publication. Queued start revalidates.
/// </summary>
public interface IRuntimeTestContextPreparer
{
    ValueTask<RuntimeTestContextPreparation> PrepareAsync(WorkflowTestEntry entry, WorkflowExecutable executable,
        string workflowExecutionId, IReadOnlyDictionary<string, JsonElement> workflowInputs,
        IReadOnlyDictionary<string, JsonElement> rootVariableSeeds, CancellationToken cancellationToken = default);
}

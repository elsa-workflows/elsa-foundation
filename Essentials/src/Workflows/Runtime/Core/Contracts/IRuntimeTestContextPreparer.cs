using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Single-provider replacement port for read-only preparation of an authorized exact test context.
/// This does not create a scope, execution, external fixture or publication. Future queued starts must
/// revalidate independently; this read-only operation grants no authority. The trusted resolver binds the
/// current caller here. A queued start must pass caller-derived scope and authority to admission, rather
/// than using the descriptor's own facts as proof of caller identity.
/// </summary>
public interface IRuntimeTestContextPreparer
{
    ValueTask<RuntimeTestContextPreparation> PrepareAsync(WorkflowTestEntry entry, WorkflowExecutable executable,
        string workflowExecutionId, IReadOnlyDictionary<string, JsonElement> workflowInputs,
        IReadOnlyDictionary<string, JsonElement> rootVariableSeeds, CancellationToken cancellationToken = default);
}

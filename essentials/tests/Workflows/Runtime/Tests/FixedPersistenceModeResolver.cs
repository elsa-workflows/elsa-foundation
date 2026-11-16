using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Resolves every run, and every workflow, to one mode, for tests that need a resolver but not its rules.</summary>
internal sealed class FixedPersistenceModeResolver(PersistenceMode mode, bool runHasRecord = true) : IRuntimePersistenceModeResolver
{
    public ValueTask<ResolvedPersistenceMode> ResolveAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ResolvedPersistenceMode(mode, runHasRecord));

    public PersistenceMode Resolve(WorkflowExecutable executable) => mode;
}

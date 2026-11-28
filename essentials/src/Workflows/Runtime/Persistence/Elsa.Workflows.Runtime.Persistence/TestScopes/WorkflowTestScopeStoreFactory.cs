using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Persistence.TestScopes;

/// <summary>Creates standalone Workflow TestRun scope stores.</summary>
public static class WorkflowTestScopeStoreFactory
{
    /// <summary>Creates a process-local store with independent TestRun scope state.</summary>
    public static IWorkflowTestScopeStore CreateInMemory() => new InMemoryWorkflowTestScopeLifecycleStore();
}

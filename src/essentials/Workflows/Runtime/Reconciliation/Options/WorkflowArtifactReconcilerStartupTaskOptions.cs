namespace Elsa.Workflows.Runtime.Reconciliation.Options;

/// <summary>Options for the artifact reconciler's startup pass.</summary>
public sealed class WorkflowArtifactReconcilerStartupTaskOptions
{
    /// <summary>
    /// How long the startup task waits for the distributed reconcile lock before giving up. A node that does not
    /// get the lock is not failing — another node is already reconciling the same mounted set, and both doing it
    /// would race on the same activation slots.
    /// </summary>
    public int LockTimeoutMs { get; set; } = 5000;
}

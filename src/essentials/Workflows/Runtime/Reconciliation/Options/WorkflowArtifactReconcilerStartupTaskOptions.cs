namespace Elsa.Workflows.Runtime.Reconciliation.Options;

/// <summary>The artifact reconciler's retired startup-task settings, kept for one release so a configured value is refused rather than ignored.</summary>
public sealed class WorkflowArtifactReconcilerStartupTaskOptions
{
    /// <summary>
    /// Retired (#2192). The startup pass is a <c>[SingleNodeTask]</c>, which now waits for its lock for the lock provider's
    /// acquisition timeout and then runs, instead of giving up after this one. A shell that still sets this refuses to start.
    /// </summary>
    /// <remarks>
    /// Kept nullable and ordinarily settable, because deleting it would be silent: CShells binds a configuration key only to a
    /// property that still exists, so a configured value would be dropped without a word. A <see cref="string"/>, so that any
    /// configured value binds and is refused.
    /// </remarks>
    [Obsolete("The artifact reconcile pass waits for the lock provider's acquisition timeout (#2192). A non-null value here refuses to start.")]
    public string? LockTimeoutMs { get; set; }
}

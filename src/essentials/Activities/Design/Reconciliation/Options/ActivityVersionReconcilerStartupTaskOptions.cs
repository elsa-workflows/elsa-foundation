namespace Elsa.Activities.Design.Reconciliation.Options;

/// <summary>The activity version reconciler's retired startup-task settings, kept for one release so a configured value is refused rather than ignored.</summary>
public sealed class ActivityVersionReconcilerStartupTaskOptions
{
    /// <summary>
    /// Retired (#2192). The startup task takes no lock any more: it runs on every node, because each node must reconcile the
    /// assemblies and catalogs it loaded itself. A shell that still sets this refuses to start.
    /// </summary>
    /// <remarks>
    /// Kept nullable and ordinarily settable, because deleting it would be silent: CShells binds a configuration key only to a
    /// property that still exists, so a configured value would be dropped without a word. A <see cref="string"/>, so that any
    /// configured value binds and is refused.
    /// </remarks>
    [Obsolete("The activity version reconciler takes no lock any more (#2192). A non-null value here refuses to start.")]
    public string? LockTimeoutMs { get; set; }
}

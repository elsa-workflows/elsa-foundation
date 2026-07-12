namespace Elsa.Workflows.Runtime.Configuration;

/// <summary>Runtime-owned safety policy for physical executable artifact reclamation.</summary>
public sealed class WorkflowExecutableGarbageCollectionOptions
{
    /// <summary>
    /// Provider-backed lease duration granted to a writer while it establishes a durable source-reference or
    /// workflow-execution retention root. The lease is released immediately after the write; this duration is a
    /// conservative crash-recovery ceiling.
    /// </summary>
    public TimeSpan RootWriteLeaseDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Minimum age of an executable before it may be considered for collection. This protects the interval
    /// between staging an immutable artifact and establishing its first durable retention root.
    /// </summary>
    public TimeSpan ArtifactCreationGracePeriod { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum time a conditional deletion operation owns its provider-backed guard. An abandoned guard expires so
    /// a later root writer or garbage-collection sweep can recover without manual intervention.
    /// </summary>
    public TimeSpan DeletionGuardTimeout { get; set; } = TimeSpan.FromMinutes(1);
}

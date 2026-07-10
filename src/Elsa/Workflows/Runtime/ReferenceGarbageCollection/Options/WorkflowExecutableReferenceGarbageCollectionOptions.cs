namespace Elsa.Workflows.Runtime.ReferenceGarbageCollection.Options;

/// <summary>Tuning for the periodic reference GC sweep (ADR 0040).</summary>
public sealed class WorkflowExecutableReferenceGarbageCollectionOptions
{
    /// <summary>Baseline interval between sweeps while the host is healthy.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Upper bound the sweep interval widens to under sustained failure.</summary>
    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromMinutes(30);
}

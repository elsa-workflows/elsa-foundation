namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Controls process-local caching of immutable workflow executable artifacts.
/// </summary>
public sealed class WorkflowExecutableCacheOptions
{
    public const int DefaultCapacity = 256;

    /// <summary>Whether a durable provider registration should select the cache decorator.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum number of positive executable artifacts retained by one cache instance.</summary>
    public int Capacity { get; set; } = DefaultCapacity;

    /// <summary>Validates settings used by durable-provider composition.</summary>
    public void Validate()
    {
        if (Enabled && Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(Capacity), Capacity, "Workflow executable cache capacity must be positive when caching is enabled.");
    }
}

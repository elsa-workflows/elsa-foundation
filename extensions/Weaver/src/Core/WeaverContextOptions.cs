namespace Elsa.Weaver.Core;

/// <summary>Host-owned correctness bounds for installed module context collection.</summary>
public sealed class WeaverContextOptions
{
    /// <summary>Maximum time allowed for each applicable source, independently of its cancellation cooperation.</summary>
    public TimeSpan SourceTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

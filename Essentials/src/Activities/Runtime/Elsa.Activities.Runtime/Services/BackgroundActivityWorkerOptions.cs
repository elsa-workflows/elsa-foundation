namespace Elsa.Activities.Runtime.Services;

/// <summary>Bounds the opt-in background activity worker on one host.</summary>
public sealed class BackgroundActivityWorkerOptions
{
    public int MaxConcurrency { get; set; } = 4;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RenewalInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int ResultSweepSize { get; set; } = 100;
    public int MaximumResultBytes { get; set; } = 1024 * 1024;

    internal void Validate()
    {
        if (MaxConcurrency is < 1 or > 256 || PollInterval <= TimeSpan.Zero ||
            LeaseDuration <= TimeSpan.Zero || RenewalInterval <= TimeSpan.Zero || RenewalInterval >= LeaseDuration ||
            ResultSweepSize is < 1 or > 1000 || MaximumResultBytes is < 1 or > 16 * 1024 * 1024)
            throw new InvalidOperationException("Background activity worker limits must be positive and bounded; renewal must occur before lease expiry.");
    }
}

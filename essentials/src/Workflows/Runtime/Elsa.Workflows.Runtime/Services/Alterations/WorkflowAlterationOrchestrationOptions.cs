namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Bounds and cadence for the restart-safe alteration-plan recurring coordinator.</summary>
public sealed class WorkflowAlterationOrchestrationOptions
{
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxBackoffInterval { get; set; } = TimeSpan.FromMinutes(1);
    public int MaxPlansPerSweep { get; set; } = 32;
    public int CapturePageSize { get; set; } = 100;
    public int MaxJobClaimsPerSweep { get; set; } = 64;
    public TimeSpan JobLeaseDuration { get; set; } = TimeSpan.FromMinutes(1);
    public string? WorkerId { get; set; }

    internal void Validate()
    {
        if (SweepInterval <= TimeSpan.Zero)
            throw new InvalidOperationException("Workflow alteration orchestration requires a positive sweep interval.");
        if (MaxBackoffInterval < SweepInterval)
            throw new InvalidOperationException("Workflow alteration orchestration max backoff cannot be less than the sweep interval.");
        if (MaxPlansPerSweep <= 0 || CapturePageSize <= 0 || MaxJobClaimsPerSweep <= 0)
            throw new InvalidOperationException("Workflow alteration orchestration batch limits must be positive.");
        if (JobLeaseDuration <= TimeSpan.Zero)
            throw new InvalidOperationException("Workflow alteration job lease duration must be positive.");
        if (WorkerId is not null && string.IsNullOrWhiteSpace(WorkerId))
            throw new InvalidOperationException("Workflow alteration worker ID cannot be blank when configured.");
    }
}

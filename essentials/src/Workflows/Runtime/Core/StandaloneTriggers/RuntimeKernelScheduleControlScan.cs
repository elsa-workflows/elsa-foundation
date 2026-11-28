namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>A bounded page awaiting acknowledgement from the managed schedule reconciler.</summary>
public interface IRuntimeKernelScheduleControlScanPage
{
    IReadOnlyList<RuntimeKernelScheduleControl> Items { get; }
    bool HasMore { get; }
}

/// <summary>Retains a finite, restartable pass over desired controls for a bound scope.</summary>
public interface IRuntimeKernelScheduleControlScanStore
{
    /// <summary>Reads the next page of the committed pass, opening a new pass when needed.</summary>
    ValueTask<IRuntimeKernelScheduleControlScanPage?> ReadScanPageAsync(TriggerScope scope, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Advances past the attempted page, or returns false when another reconciler has advanced it.</summary>
    ValueTask<bool> TryAdvanceScanAsync(TriggerScope scope, IRuntimeKernelScheduleControlScanPage page,
        CancellationToken cancellationToken = default);
}

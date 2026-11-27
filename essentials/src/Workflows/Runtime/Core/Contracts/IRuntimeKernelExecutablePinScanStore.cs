using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>An exact page of the currently persisted pin cleanup pass.</summary>
public interface IRuntimeKernelExecutablePinScanPage
{
    IReadOnlyList<RuntimeKernelExecutablePin> Items { get; }
    bool HasMore { get; }
}

/// <summary>Visits the open pins in a finite, restartable pass under their ordinary persistence scope.</summary>
/// <remarks>
/// Stop predecessor pin writers and apply the Runtime migrations before enabling this scan. Existing pins share
/// a fixed starting ordinal, and upgraded writers allocate a positive ordinal in the transaction that adds each pin.
/// The preceding packaged preview kept executable protection disabled.
/// </remarks>
public interface IRuntimeKernelExecutablePinScanStore
{
    /// <summary>Reads the next page from a pass whose inclusive insertion boundary was fixed when it opened.</summary>
    ValueTask<IRuntimeKernelExecutablePinScanPage?> ReadScanPageAsync(PersistenceScope scope, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Advances the exact persisted position after the caller has attempted every pin in the page.</summary>
    ValueTask<bool> TryAdvanceScanAsync(PersistenceScope scope, IRuntimeKernelExecutablePinScanPage page,
        CancellationToken cancellationToken = default);
}

using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.Persistence.Draining;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

internal static class GroundworkOpenTelemetryStoreTestExtensions
{
    public static ValueTask WriteDurablyAsync(
        this GroundworkOpenTelemetryStore store,
        OpenTelemetryBatch batch,
        CancellationToken cancellationToken = default) =>
        store.WriteAsync(DiagnosticsDrainBatchId.New(), batch, cancellationToken);
}

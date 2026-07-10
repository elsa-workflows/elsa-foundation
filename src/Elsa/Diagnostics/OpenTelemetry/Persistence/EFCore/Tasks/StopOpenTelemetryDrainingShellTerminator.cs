using CShells.Lifecycle;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Storage;
using JetBrains.Annotations;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Tasks;

/// <summary>
/// Flushes buffered telemetry while the shell service provider, including its DbContext factory, is still usable.
/// </summary>
[UsedImplicitly]
public sealed class StopOpenTelemetryDrainingShellTerminator(EfCoreOpenTelemetryStore store) : IShellTerminator
{
    /// <inheritdoc />
    public Task TerminateAsync(CancellationToken cancellationToken = default) =>
        store.CompleteDrainingIfStartedAsync(cancellationToken);
}

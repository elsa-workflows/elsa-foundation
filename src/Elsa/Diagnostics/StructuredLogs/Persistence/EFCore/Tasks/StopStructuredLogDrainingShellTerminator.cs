using CShells.Lifecycle;
using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Storage;
using JetBrains.Annotations;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Tasks;

/// <summary>
/// Flushes buffered structured logs while the shell service provider, including its DbContext factory, is still usable.
/// </summary>
[UsedImplicitly]
public sealed class StopStructuredLogDrainingShellTerminator(EfCoreStructuredLogStore store) : IShellTerminator
{
    /// <inheritdoc />
    public Task TerminateAsync(CancellationToken cancellationToken = default) =>
        store.CompleteDrainingIfStartedAsync(cancellationToken);
}

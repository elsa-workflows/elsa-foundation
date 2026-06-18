using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Storage;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using JetBrains.Annotations;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Tasks;

/// <summary>
/// Starts the background drain loop of the <see cref="EfCoreStructuredLogStore"/> once the schema is in
/// place. Ordered after the EF Core migration task (<c>Order(-100)</c>) so the log table exists before the
/// first batch insert. <see cref="EfCoreStructuredLogStore.StartDraining"/> is idempotent, so running this
/// task once per tenant cannot spawn more than one loop on the singleton store.
/// </summary>
[UsedImplicitly]
[Order(0)]
public sealed class StartStructuredLogDrainingStartupTask(EfCoreStructuredLogStore store) : IStartupTask
{
    /// <inheritdoc />
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        store.StartDraining();
        return Task.CompletedTask;
    }
}

using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Scoped CQS reads of committed model state/receipts. All writes enter checkpoint participation.</summary>
/// <remarks>Unavailable authoritative storage raises <see cref="RuntimeSchedulerWorkUnavailableException"/>
/// with the operation and scoped execution/input context, preserving the provider cause. Requested cancellation remains cancellation.</remarks>
public interface IRuntimeModelExecutionReader
{
    ValueTask<RuntimeModelExecutionState?> FindAsync(string executionId, CancellationToken cancellationToken = default);
    ValueTask<RuntimeModelInputReceipt?> FindInputAsync(string executionId, string inputId, CancellationToken cancellationToken = default);
    ValueTask<RuntimeStorePage<RuntimeModelExecutionState>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default);
}

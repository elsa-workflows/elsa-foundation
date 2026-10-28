using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Scoped CQS reads of committed model state/receipts. All writes enter checkpoint participation.</summary>
public interface IRuntimeModelExecutionReader
{
    ValueTask<RuntimeModelExecutionState?> FindAsync(string executionId, CancellationToken cancellationToken = default);
    ValueTask<RuntimeModelInputReceipt?> FindInputAsync(string executionId, string inputId, CancellationToken cancellationToken = default);
    ValueTask<RuntimeStorePage<RuntimeModelExecutionState>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default);
}

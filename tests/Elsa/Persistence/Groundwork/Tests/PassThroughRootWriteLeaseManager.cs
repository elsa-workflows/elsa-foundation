using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Persistence.Groundwork.Tests;

internal sealed class PassThroughRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
{
    public static PassThroughRootWriteLeaseManager Instance { get; } = new();

    public ValueTask ExecuteAsync(
        string artifactId,
        string leaseId,
        Func<CancellationToken, ValueTask> write,
        CancellationToken cancellationToken = default) =>
        write(cancellationToken);
}

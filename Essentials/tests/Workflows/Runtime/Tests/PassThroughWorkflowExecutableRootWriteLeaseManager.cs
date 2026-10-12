using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Tests;

internal sealed class PassThroughWorkflowExecutableRootWriteLeaseManager : IWorkflowExecutableRootWriteLeaseManager
{
    public static PassThroughWorkflowExecutableRootWriteLeaseManager Instance { get; } = new();

    public ValueTask ExecuteAsync(
        string artifactId,
        string leaseId,
        Func<CancellationToken, ValueTask> write,
        CancellationToken cancellationToken = default) =>
        write(cancellationToken);
}

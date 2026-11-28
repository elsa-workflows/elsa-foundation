namespace Elsa.Workflows.Dashboard;

public sealed class UnavailableWorkflowRunHealthDataSource : IWorkflowRunHealthDataSource
{
    public bool IsAvailable => false;

    public ValueTask<WorkflowRunHealthAggregate> QueryAsync(
        WorkflowRunHealthDataQuery query,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Unavailable workflow run-health providers cannot execute queries.");
}

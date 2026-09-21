namespace Elsa.Workflows.Runtime.Core.Models;

public enum WorkflowDispatchStatus
{
    Pending = 0,
    Started = 1,
    Completed = 2,
    Faulted = 3,
    Cancelled = 4,
    DispatchFailed = 5
}

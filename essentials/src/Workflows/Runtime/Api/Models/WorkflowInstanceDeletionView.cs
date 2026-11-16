namespace Elsa.Workflows.Runtime.Api.Models;

public sealed record WorkflowInstanceDeletionView(string WorkflowExecutionId, string Status = "Deleted");

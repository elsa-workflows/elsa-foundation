namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Generates runtime-owned identifiers for workflow execution command dispatch.
/// </summary>
public interface IRuntimeExecutionIdGenerator
{
    string NewWorkflowExecutionId();

    string NewWorkflowExecutionCommandId();

    string NewWorkflowExecutionCommandEnvelopeId();

    string NewActivityExecutionId();
}
